namespace AgilePredict.Models.DTOs.SprintRisk
{
    public enum SprintRiskLevel { OnTrack, AtRisk, Critical }

    public enum SprintRiskSeverity { Info, Warning, Critical }

    public enum SprintTimelineStatus { NotStarted, InProgress, Ended }

    public enum SprintRiskAlertCode
    {
        CapacityOverflow,
        NoCapacityLeft,
        DeliveryPaceBelowRequired,
        MemberOverload,
        MemberWithoutCapacity,
        UnassignedWork,
        UnestimatedWork,
        NoCapacityConfigured,
        NoHoursEstimated,
        SprintEndedWithPendingWork
    }

    /// <summary>
    /// Resultado da regra analítica do bot (PBI 8.1, AC 2): cruza as Horas Estimadas entregues com a
    /// capacidade diária restante do time e projeta o risco de atraso da sprint.
    /// </summary>
    public sealed class SprintRiskAnalysis
    {
        public string Organization { get; set; } = string.Empty;
        public string Project { get; set; } = string.Empty;
        public string Team { get; set; } = string.Empty;
        public string SprintName { get; set; } = string.Empty;
        public string IterationPath { get; set; } = string.Empty;
        public DateOnly StartDate { get; set; }
        public DateOnly FinishDate { get; set; }
        public DateOnly AnalysisDate { get; set; }
        public SprintTimelineStatus TimelineStatus { get; set; }

        public int TotalWorkingDays { get; set; }
        public int ElapsedWorkingDays { get; set; }
        public int RemainingWorkingDays { get; set; }

        /// <summary>Soma das Horas Estimadas (Original Estimate) dos itens com horas.</summary>
        public double TotalEstimatedHours { get; set; }

        /// <summary>Horas Estimadas dos itens já entregues (estado Done/Closed/Resolved).</summary>
        public double DeliveredEstimatedHours { get; set; }

        public double CompletedWorkHours { get; set; }
        public double RemainingWorkHours { get; set; }

        /// <summary>Percentual das Horas Estimadas já entregues (0–100).</summary>
        public double DeliveryPercent { get; set; }

        /// <summary>Capacidade diária do time (soma das horas/dia de cada membro).</summary>
        public double TeamDailyCapacityHours { get; set; }

        /// <summary>Capacidade restante até o fim da sprint, descontando folgas do time e de cada membro.</summary>
        public double RemainingCapacityHours { get; set; }

        /// <summary>Trabalho restante ÷ capacidade restante. Nulo quando não há capacidade restante.</summary>
        public double? CapacityUtilization { get; set; }

        /// <summary>Horas/dia que o time precisa entregar para fechar a sprint no prazo.</summary>
        public double? RequiredDailyBurnHours { get; set; }

        /// <summary>Horas Estimadas entregues por dia útil decorrido.</summary>
        public double? ObservedDailyDeliveryHours { get; set; }

        /// <summary>Dias úteis de atraso projetados no ritmo atual (negativo = folga).</summary>
        public double? ProjectedSlipWorkingDays { get; set; }

        public int HourTrackedItems { get; set; }
        public int DoneItems { get; set; }
        public int PendingItems { get; set; }
        public int UnestimatedItems { get; set; }
        public double UnassignedRemainingHours { get; set; }

        public SprintRiskLevel RiskLevel { get; set; }

        public List<MemberLoad> Members { get; set; } = new();
        public List<SprintRiskAlert> Alerts { get; set; } = new();

        /// <summary>
        /// Projeção do trabalho restante dia a dia até o fim da sprint, no ritmo observado e na
        /// capacidade total do time (base dos gráficos do dashboard).
        /// </summary>
        public List<BurndownForecastPoint> Forecast { get; set; } = new();
    }

    public sealed class MemberLoad
    {
        public string Member { get; set; } = string.Empty;
        public double CapacityPerDay { get; set; }
        public int AvailableWorkingDays { get; set; }
        public double RemainingCapacityHours { get; set; }
        public double RemainingWorkHours { get; set; }
        public double? Utilization { get; set; }
        public SprintRiskSeverity? Severity { get; set; }
    }

    public sealed class SprintRiskAlert
    {
        public SprintRiskAlertCode Code { get; set; }
        public SprintRiskSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Member { get; set; }
        public List<int> WorkItemIds { get; set; } = new();
    }

    public sealed class BurndownForecastPoint
    {
        public DateOnly Date { get; set; }
        public double AtObservedPaceHours { get; set; }
        public double AtFullCapacityHours { get; set; }
    }
}
