using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Models.SprintRisk;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AgilePredict.Services
{
    /// <summary>
    /// Regra de negócio analítica do bot preditivo (PBI 8.1, Task 1). Não faz I/O: recebe o retrato
    /// da sprint e o relógio (<see cref="TimeProvider"/>) por injeção, por isso é testável sem Azure.
    ///
    /// Cruza as Horas Estimadas entregues com a capacidade diária restante do time (AC 2):
    /// <list type="bullet">
    /// <item>trabalho restante × capacidade restante (por time e por membro) → utilização;</item>
    /// <item>Horas Estimadas entregues ÷ dias úteis decorridos → ritmo observado;</item>
    /// <item>trabalho restante ÷ dias úteis restantes → ritmo exigido; a diferença projeta o atraso.</item>
    /// </list>
    /// </summary>
    public sealed class SprintRiskAnalyzer : ISprintRiskAnalyzer
    {
        private readonly SprintRiskConfiguration _config;
        private readonly TimeProvider _timeProvider;
        private readonly TimeZoneInfo _timeZone;

        public SprintRiskAnalyzer(IOptions<SprintRiskConfiguration> config, TimeProvider timeProvider)
        {
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_config.TimeZoneId);
        }

        public SprintRiskAnalysis Analyze(SprintCapacitySnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeZone).DateTime);
            var iteration = snapshot.Iteration;
            var calendar = new WorkingCalendar(snapshot.WorkingDays, snapshot.TeamDaysOff);

            var remainingFrom = today > iteration.StartDate ? today : iteration.StartDate;
            var elapsedTo = today.AddDays(-1) < iteration.FinishDate ? today.AddDays(-1) : iteration.FinishDate;

            var analysis = new SprintRiskAnalysis
            {
                Organization = snapshot.Organization,
                Project = snapshot.Project,
                Team = snapshot.Team,
                SprintName = iteration.Name,
                IterationPath = iteration.Path,
                StartDate = iteration.StartDate,
                FinishDate = iteration.FinishDate,
                AnalysisDate = today,
                TimelineStatus = today < iteration.StartDate ? SprintTimelineStatus.NotStarted
                    : today > iteration.FinishDate ? SprintTimelineStatus.Ended
                    : SprintTimelineStatus.InProgress,
                TotalWorkingDays = calendar.CountTeamDays(iteration.StartDate, iteration.FinishDate),
                ElapsedWorkingDays = calendar.CountTeamDays(iteration.StartDate, elapsedTo),
                RemainingWorkingDays = calendar.CountTeamDays(remainingFrom, iteration.FinishDate)
            };

            var items = CollectHourItems(snapshot.WorkItems);
            FillHours(analysis, items);
            FillMembers(analysis, snapshot, items, calendar, remainingFrom);
            FillRates(analysis);
            analysis.Forecast = BuildForecast(analysis, calendar, remainingFrom);

            analysis.Alerts = BuildAlerts(analysis, snapshot, items)
                .OrderByDescending(a => a.Severity)
                .ThenBy(a => a.Code)
                .ToList();
            analysis.RiskLevel = analysis.Alerts.Any(a => a.Severity == SprintRiskSeverity.Critical) ? SprintRiskLevel.Critical
                : analysis.Alerts.Any(a => a.Severity == SprintRiskSeverity.Warning) ? SprintRiskLevel.AtRisk
                : SprintRiskLevel.OnTrack;

            return analysis;
        }

        private sealed record HourItem(SprintWorkItem Item, bool IsDone, double? Estimate, double Remaining, double Completed)
        {
            public bool IsUnestimated => !IsDone && Estimate is null && Item.RemainingWork is null;
        }

        private List<HourItem> CollectHourItems(IEnumerable<SprintWorkItem> workItems)
        {
            return workItems
                .Where(w => _config.HourTrackedWorkItemTypes.Contains(w.WorkItemType, StringComparer.OrdinalIgnoreCase))
                .Where(w => !_config.ExcludedStates.Contains(w.State, StringComparer.OrdinalIgnoreCase))
                .Select(w =>
                {
                    var isDone = _config.DoneStates.Contains(w.State, StringComparer.OrdinalIgnoreCase);
                    var completed = Math.Max(0, w.CompletedWork ?? 0);

                    // Horas Estimadas: Original Estimate; sem ele, reconstrói por Completed + Remaining.
                    double? estimate = w.OriginalEstimate is > 0 ? w.OriginalEstimate
                        : (w.CompletedWork ?? 0) + (w.RemainingWork ?? 0) > 0 ? (w.CompletedWork ?? 0) + (w.RemainingWork ?? 0)
                        : null;

                    // Item entregue não tem trabalho restante, mesmo que o Remaining Work não tenha sido zerado.
                    var remaining = isDone ? 0
                        : w.RemainingWork is not null ? Math.Max(0, w.RemainingWork.Value)
                        : Math.Max(0, (estimate ?? 0) - completed);

                    return new HourItem(w, isDone, estimate, remaining, completed);
                })
                .ToList();
        }

        private static void FillHours(SprintRiskAnalysis analysis, List<HourItem> items)
        {
            analysis.HourTrackedItems = items.Count;
            analysis.DoneItems = items.Count(i => i.IsDone);
            analysis.PendingItems = items.Count(i => !i.IsDone);
            analysis.UnestimatedItems = items.Count(i => i.IsUnestimated);
            analysis.TotalEstimatedHours = Round(items.Sum(i => i.Estimate ?? 0));
            analysis.DeliveredEstimatedHours = Round(items.Where(i => i.IsDone).Sum(i => i.Estimate ?? 0));
            analysis.CompletedWorkHours = Round(items.Sum(i => i.Completed));
            analysis.RemainingWorkHours = Round(items.Sum(i => i.Remaining));
            analysis.UnassignedRemainingHours = Round(items.Where(i => string.IsNullOrWhiteSpace(i.Item.AssignedTo)).Sum(i => i.Remaining));
            analysis.DeliveryPercent = analysis.TotalEstimatedHours > 0
                ? Round(analysis.DeliveredEstimatedHours / analysis.TotalEstimatedHours * 100)
                : 0;
        }

        private void FillMembers(
            SprintRiskAnalysis analysis,
            SprintCapacitySnapshot snapshot,
            List<HourItem> items,
            WorkingCalendar calendar,
            DateOnly remainingFrom)
        {
            var workByMember = items
                .Where(i => !string.IsNullOrWhiteSpace(i.Item.AssignedTo))
                .GroupBy(i => i.Item.AssignedTo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Remaining), StringComparer.OrdinalIgnoreCase);

            var members = new List<MemberLoad>();
            foreach (var member in snapshot.Members)
            {
                var days = calendar.CountMemberDays(remainingFrom, snapshot.Iteration.FinishDate, member.DaysOff);
                var capacity = Math.Max(0, member.CapacityPerDay) * days;
                workByMember.TryGetValue(member.DisplayName, out var work);
                members.Add(CreateMemberLoad(member.DisplayName, member.CapacityPerDay, days, capacity, work));
            }

            // Quem tem trabalho atribuído mas não está no planejamento de capacidade.
            foreach (var (name, work) in workByMember)
            {
                if (members.Any(m => string.Equals(m.Member, name, StringComparison.OrdinalIgnoreCase))) continue;
                members.Add(CreateMemberLoad(name, 0, 0, 0, work));
            }

            // Mais arriscado primeiro: severidade, depois utilização (sem capacidade e com trabalho = topo).
            analysis.Members = members
                .OrderByDescending(m => m.Severity is null ? 0 : (int)m.Severity.Value + 1)
                .ThenByDescending(m => m.Utilization ?? (m.RemainingWorkHours > 0 ? double.MaxValue : 0))
                .ThenByDescending(m => m.RemainingWorkHours)
                .ToList();
            analysis.TeamDailyCapacityHours = Round(snapshot.Members.Sum(m => Math.Max(0, m.CapacityPerDay)));
            analysis.RemainingCapacityHours = Round(members.Sum(m => m.RemainingCapacityHours));
        }

        private MemberLoad CreateMemberLoad(string name, double capacityPerDay, int days, double capacity, double work)
        {
            double? utilization = capacity > 0 ? Round(work / capacity) : null;
            return new MemberLoad
            {
                Member = name,
                CapacityPerDay = Round(capacityPerDay),
                AvailableWorkingDays = days,
                RemainingCapacityHours = Round(capacity),
                RemainingWorkHours = Round(work),
                Utilization = utilization,
                Severity = work <= 0 ? null
                    : utilization is null || utilization >= _config.CriticalUtilizationThreshold ? SprintRiskSeverity.Critical
                    : utilization >= _config.WarningUtilizationThreshold ? SprintRiskSeverity.Warning
                    : null
            };
        }

        private static void FillRates(SprintRiskAnalysis a)
        {
            a.CapacityUtilization = a.RemainingCapacityHours > 0 ? Round(a.RemainingWorkHours / a.RemainingCapacityHours) : null;
            a.RequiredDailyBurnHours = a.RemainingWorkingDays > 0 ? Round(a.RemainingWorkHours / a.RemainingWorkingDays) : null;
            a.ObservedDailyDeliveryHours = a.ElapsedWorkingDays > 0 ? Round(a.DeliveredEstimatedHours / a.ElapsedWorkingDays) : null;

            if (a.RemainingWorkHours <= 0)
            {
                a.ProjectedSlipWorkingDays = 0;
            }
            else if (a.ObservedDailyDeliveryHours is > 0)
            {
                a.ProjectedSlipWorkingDays = Round(a.RemainingWorkHours / a.ObservedDailyDeliveryHours.Value - a.RemainingWorkingDays);
            }
        }

        private static List<BurndownForecastPoint> BuildForecast(SprintRiskAnalysis a, WorkingCalendar calendar, DateOnly from)
        {
            var points = new List<BurndownForecastPoint>();
            if (a.TimelineStatus == SprintTimelineStatus.Ended) return points;

            var pace = a.ObservedDailyDeliveryHours ?? 0;
            var capacityPerDay = a.RemainingWorkingDays > 0 ? a.RemainingCapacityHours / a.RemainingWorkingDays : 0;
            double atPace = a.RemainingWorkHours, atCapacity = a.RemainingWorkHours;

            points.Add(new BurndownForecastPoint { Date = from.AddDays(-1), AtObservedPaceHours = Round(atPace), AtFullCapacityHours = Round(atCapacity) });
            foreach (var day in calendar.TeamDays(from, a.FinishDate))
            {
                atPace = Math.Max(0, atPace - pace);
                atCapacity = Math.Max(0, atCapacity - capacityPerDay);
                points.Add(new BurndownForecastPoint { Date = day, AtObservedPaceHours = Round(atPace), AtFullCapacityHours = Round(atCapacity) });
            }

            return points;
        }

        private IEnumerable<SprintRiskAlert> BuildAlerts(SprintRiskAnalysis a, SprintCapacitySnapshot snapshot, List<HourItem> items)
        {
            var pendingIds = items.Where(i => i.Remaining > 0).Select(i => i.Item.Id).ToList();

            if (a.TimelineStatus == SprintTimelineStatus.Ended)
            {
                // Conta itens não concluídos, não só horas: uma task "To Do" sem estimativa também ficou para trás.
                if (a.PendingItems > 0)
                {
                    var notDoneIds = items.Where(i => !i.IsDone).Select(i => i.Item.Id).ToList();
                    yield return Alert(SprintRiskAlertCode.SprintEndedWithPendingWork, SprintRiskSeverity.Critical,
                        "Sprint encerrada com trabalho pendente",
                        $"A sprint terminou em {a.FinishDate:dd/MM/yyyy} com {a.PendingItems} item(ns) não concluído(s)" +
                        (a.RemainingWorkHours > 0 ? $" e {Fmt(a.RemainingWorkHours)} h restantes" : "") +
                        (a.UnestimatedItems > 0 ? $"; {a.UnestimatedItems} deles sem Horas Estimadas." : "."),
                        ids: notDoneIds);
                }
                yield break;
            }

            var hasCapacity = a.TeamDailyCapacityHours > 0;
            if (!hasCapacity)
            {
                yield return Alert(SprintRiskAlertCode.NoCapacityConfigured, SprintRiskSeverity.Warning,
                    "Capacidade do time não cadastrada",
                    "A sprint não tem capacidade cadastrada (Sprints › Capacity). Sem ela não é possível cruzar o trabalho restante com a capacidade diária do time.");
            }

            if (a.HourTrackedItems > 0 && a.TotalEstimatedHours <= 0 && a.RemainingWorkHours <= 0)
            {
                yield return Alert(SprintRiskAlertCode.NoHoursEstimated, SprintRiskSeverity.Warning,
                    "Nenhuma Hora Estimada na sprint",
                    $"Os {a.HourTrackedItems} itens com horas da sprint estão sem Original Estimate e sem Remaining Work; a previsão de atraso fica sem base.");
            }

            if (hasCapacity && a.RemainingWorkHours > 0)
            {
                if (a.RemainingCapacityHours <= 0)
                {
                    yield return Alert(SprintRiskAlertCode.NoCapacityLeft, SprintRiskSeverity.Critical,
                        "Sem capacidade restante",
                        $"Restam {Fmt(a.RemainingWorkHours)} h de trabalho e nenhuma hora de capacidade até {a.FinishDate:dd/MM/yyyy}.",
                        ids: pendingIds);
                }
                else if (a.CapacityUtilization >= _config.WarningUtilizationThreshold)
                {
                    var critical = a.CapacityUtilization >= _config.CriticalUtilizationThreshold;
                    var gap = a.RemainingWorkHours - a.RemainingCapacityHours;
                    yield return Alert(SprintRiskAlertCode.CapacityOverflow,
                        critical ? SprintRiskSeverity.Critical : SprintRiskSeverity.Warning,
                        critical ? "Trabalho restante excede a capacidade" : "Capacidade quase esgotada",
                        $"Restam {Fmt(a.RemainingWorkHours)} h de trabalho para {Fmt(a.RemainingCapacityHours)} h de capacidade " +
                        $"({Pct(a.CapacityUtilization!.Value)} de utilização)" +
                        (gap > 0 ? $"; faltam {Fmt(gap)} h para caber na sprint." : "."));
                }
            }

            if (a.ObservedDailyDeliveryHours is not null && a.RequiredDailyBurnHours is > 0 &&
                a.ObservedDailyDeliveryHours < a.RequiredDailyBurnHours * _config.PaceToleranceRatio)
            {
                var slip = a.ProjectedSlipWorkingDays;
                yield return Alert(SprintRiskAlertCode.DeliveryPaceBelowRequired,
                    slip is null || slip >= _config.CriticalSlipWorkingDays ? SprintRiskSeverity.Critical : SprintRiskSeverity.Warning,
                    "Ritmo de entrega abaixo do necessário",
                    $"O time entregou {Fmt(a.ObservedDailyDeliveryHours.Value)} h/dia de Horas Estimadas e precisa de {Fmt(a.RequiredDailyBurnHours.Value)} h/dia " +
                    (slip is > 0 ? $"para fechar no prazo; no ritmo atual, atraso projetado de {Fmt(slip.Value)} dia(s) útil(eis)." : "para fechar no prazo."));
            }

            foreach (var member in a.Members.Where(m => m.Severity is not null))
            {
                var memberIds = items.Where(i => i.Remaining > 0 &&
                        string.Equals(i.Item.AssignedTo, member.Member, StringComparison.OrdinalIgnoreCase))
                    .Select(i => i.Item.Id).ToList();

                if (member.RemainingCapacityHours <= 0)
                {
                    yield return Alert(SprintRiskAlertCode.MemberWithoutCapacity, SprintRiskSeverity.Warning,
                        $"{member.Member} sem capacidade disponível",
                        $"{member.Member} tem {Fmt(member.RemainingWorkHours)} h atribuídas, mas nenhuma capacidade cadastrada ou disponível até o fim da sprint.",
                        member.Member, memberIds);
                }
                else
                {
                    yield return Alert(SprintRiskAlertCode.MemberOverload, member.Severity!.Value,
                        $"{member.Member} sobrecarregado(a)",
                        $"{member.Member} tem {Fmt(member.RemainingWorkHours)} h restantes para {Fmt(member.RemainingCapacityHours)} h de capacidade " +
                        $"({Pct(member.Utilization!.Value)}).",
                        member.Member, memberIds);
                }
            }

            if (a.UnassignedRemainingHours > 0)
            {
                var ids = items.Where(i => i.Remaining > 0 && string.IsNullOrWhiteSpace(i.Item.AssignedTo)).Select(i => i.Item.Id).ToList();
                yield return Alert(SprintRiskAlertCode.UnassignedWork, SprintRiskSeverity.Warning,
                    "Trabalho sem responsável",
                    $"{Fmt(a.UnassignedRemainingHours)} h restantes em {ids.Count} item(ns) sem responsável; ninguém está comprometido com essas horas.",
                    ids: ids);
            }

            if (a.UnestimatedItems > 0)
            {
                var ids = items.Where(i => i.IsUnestimated).Select(i => i.Item.Id).ToList();
                yield return Alert(SprintRiskAlertCode.UnestimatedWork, SprintRiskSeverity.Info,
                    "Itens sem estimativa",
                    $"{ids.Count} item(ns) ativo(s) sem Horas Estimadas; o trabalho restante real pode ser maior que o calculado.",
                    ids: ids);
            }
        }

        private static SprintRiskAlert Alert(
            SprintRiskAlertCode code, SprintRiskSeverity severity, string title, string message,
            string? member = null, List<int>? ids = null) => new()
        {
            Code = code,
            Severity = severity,
            Title = title,
            Message = message,
            Member = member,
            WorkItemIds = ids ?? new List<int>()
        };

        private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static string Fmt(double value) => value.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

        private static string Pct(double ratio) => (ratio * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>
    /// Calendário de dias úteis da sprint: dias de trabalho do time menos as folgas do time e,
    /// por membro, as folgas individuais.
    /// </summary>
    internal sealed class WorkingCalendar(IReadOnlyList<DayOfWeek> workingDays, IReadOnlyList<DateRange> teamDaysOff)
    {
        public IEnumerable<DateOnly> TeamDays(DateOnly from, DateOnly to)
        {
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (workingDays.Contains(day.DayOfWeek) && !teamDaysOff.Any(off => off.Contains(day)))
                {
                    yield return day;
                }
            }
        }

        public int CountTeamDays(DateOnly from, DateOnly to) => TeamDays(from, to).Count();

        public int CountMemberDays(DateOnly from, DateOnly to, IReadOnlyList<DateRange> memberDaysOff) =>
            TeamDays(from, to).Count(day => !memberDaysOff.Any(off => off.Contains(day)));
    }
}
