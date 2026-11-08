using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Models.SprintRisk;
using AgilePredict.Services;
using Microsoft.Extensions.Options;
using static AgilePredict.Tests.Services.SprintRisk.SprintRiskTestData;

namespace AgilePredict.Tests.Services.SprintRisk
{
    public class SprintRiskAnalyzerTests
    {
        private static SprintRiskAnalyzer CreateAnalyzer(DateTimeOffset? now = null, SprintRiskConfiguration? config = null) =>
            new(Options.Create(config ?? new SprintRiskConfiguration()), new FixedTimeProvider(now ?? Monday9Nov));

        [Fact]
        public void Analyze_CruzaHorasEntreguesComCapacidadeRestante_QuandoTrabalhoExcedeCapacidade_RiscoCritico()
        {
            // Ana: 6 h/dia × 5 dias restantes = 30 h de capacidade; 40 h restantes; 20 h entregues em 5 dias.
            var snapshot = Snapshot(items: new[]
            {
                Task(1, "Done", "Ana", estimate: 20, remaining: 0, completed: 20),
                Task(2, "In Progress", "Ana", estimate: 24, remaining: 24),
                Task(3, "To Do", "Ana", estimate: 16, remaining: 16)
            });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(SprintTimelineStatus.InProgress, result.TimelineStatus);
            Assert.Equal(10, result.TotalWorkingDays);
            Assert.Equal(5, result.ElapsedWorkingDays);
            Assert.Equal(5, result.RemainingWorkingDays);
            Assert.Equal(60, result.TotalEstimatedHours);
            Assert.Equal(20, result.DeliveredEstimatedHours);
            Assert.Equal(33.33, result.DeliveryPercent);
            Assert.Equal(40, result.RemainingWorkHours);
            Assert.Equal(6, result.TeamDailyCapacityHours);
            Assert.Equal(30, result.RemainingCapacityHours);
            Assert.Equal(1.33, result.CapacityUtilization);
            Assert.Equal(4, result.ObservedDailyDeliveryHours);
            Assert.Equal(8, result.RequiredDailyBurnHours);
            Assert.Equal(5, result.ProjectedSlipWorkingDays);
            Assert.Equal(SprintRiskLevel.Critical, result.RiskLevel);

            var overflow = Assert.Single(result.Alerts, a => a.Code == SprintRiskAlertCode.CapacityOverflow);
            Assert.Equal(SprintRiskSeverity.Critical, overflow.Severity);
            Assert.Contains("faltam 10 h", overflow.Message);
            Assert.Equal(SprintRiskSeverity.Critical, Assert.Single(result.Alerts, a => a.Code == SprintRiskAlertCode.DeliveryPaceBelowRequired).Severity);

            var member = Assert.Single(result.Alerts, a => a.Code == SprintRiskAlertCode.MemberOverload);
            Assert.Equal("Ana", member.Member);
            Assert.Equal(new[] { 2, 3 }, member.WorkItemIds);
        }

        [Fact]
        public void Analyze_TrabalhoCabeNaCapacidadeERitmoSuficiente_NoPrazoSemAlertas()
        {
            var snapshot = Snapshot(items: new[]
            {
                Task(1, "Done", "Ana", estimate: 30),
                Task(2, "In Progress", "Ana", estimate: 12, remaining: 10)
            });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(SprintRiskLevel.OnTrack, result.RiskLevel);
            Assert.Empty(result.Alerts);
            Assert.Equal(0.33, result.CapacityUtilization);
            Assert.True(result.ProjectedSlipWorkingDays < 0);
        }

        [Fact]
        public void Analyze_UtilizacaoEntreAtencaoECritico_EmRisco()
        {
            // 27 h restantes / 30 h de capacidade = 0,9 (≥ 0,85 e < 1,0); ritmo 6 h/dia ≥ 5,4 exigido.
            var snapshot = Snapshot(items: new[]
            {
                Task(1, "Done", "Ana", estimate: 30),
                Task(2, "To Do", "Ana", estimate: 27)
            });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(SprintRiskLevel.AtRisk, result.RiskLevel);
            Assert.Equal(SprintRiskSeverity.Warning, Assert.Single(result.Alerts, a => a.Code == SprintRiskAlertCode.CapacityOverflow).Severity);
            Assert.DoesNotContain(result.Alerts, a => a.Code == SprintRiskAlertCode.DeliveryPaceBelowRequired);
        }

        [Fact]
        public void Analyze_DescontaFolgaDoTimeEFolgaDoMembroDaCapacidadeRestante()
        {
            var snapshot = Snapshot(
                members: new[] { Member("Ana", 6, new DateRange(new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 11))) },
                teamDaysOff: new[] { new DateRange(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10)) });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(4, result.RemainingWorkingDays);         // feriado do time sai do calendário
            var ana = Assert.Single(result.Members);
            Assert.Equal(3, ana.AvailableWorkingDays);             // e a folga individual só da Ana
            Assert.Equal(18, ana.RemainingCapacityHours);
        }

        [Fact]
        public void Analyze_IgnoraTiposSemHorasEItensRemovidos_EZeraRestanteDeItemEntregue()
        {
            var snapshot = Snapshot(items: new[]
            {
                Task(1, "Done", "Ana", estimate: 8, remaining: 3),              // entregue: restante não conta
                Task(2, "Removed", "Ana", estimate: 50, remaining: 50),          // removido: fora
                Task(3, "New", "Ana", estimate: 100, type: "Product Backlog Item") // PBI: não carrega horas
            });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(1, result.HourTrackedItems);
            Assert.Equal(8, result.TotalEstimatedHours);
            Assert.Equal(8, result.DeliveredEstimatedHours);
            Assert.Equal(0, result.RemainingWorkHours);
        }

        [Fact]
        public void Analyze_SemCapacidadeCadastrada_AlertaConfiguracaoEMembroSemCapacidade()
        {
            var snapshot = Snapshot(members: Array.Empty<TeamMemberCapacity>(), items: new[]
            {
                Task(1, "To Do", "Bruno", estimate: 10)
            });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Null(result.CapacityUtilization);
            Assert.Contains(result.Alerts, a => a.Code == SprintRiskAlertCode.NoCapacityConfigured && a.Severity == SprintRiskSeverity.Warning);
            var bruno = Assert.Single(result.Alerts, a => a.Code == SprintRiskAlertCode.MemberWithoutCapacity);
            Assert.Equal("Bruno", bruno.Member);
            Assert.DoesNotContain(result.Alerts, a => a.Code == SprintRiskAlertCode.CapacityOverflow);
        }

        [Fact]
        public void Analyze_TrabalhoSemResponsavelEItemSemEstimativa_GeraAlertasDeQualidadeDoDado()
        {
            var snapshot = Snapshot(items: new[]
            {
                Task(1, "To Do", null, estimate: 5),
                Task(2, "To Do", "Ana", estimate: null)
            });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(5, result.UnassignedRemainingHours);
            Assert.Equal(new[] { 1 }, Assert.Single(result.Alerts, a => a.Code == SprintRiskAlertCode.UnassignedWork).WorkItemIds);
            var unestimated = Assert.Single(result.Alerts, a => a.Code == SprintRiskAlertCode.UnestimatedWork);
            Assert.Equal(SprintRiskSeverity.Info, unestimated.Severity);
            Assert.Equal(new[] { 2 }, unestimated.WorkItemIds);
        }

        [Fact]
        public void Analyze_NenhumaHoraEstimada_AlertaQuePrevisaoFicaSemBase()
        {
            var snapshot = Snapshot(items: new[] { Task(1, "To Do", "Ana", null), Task(2, "To Do", "Ana", null) });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Contains(result.Alerts, a => a.Code == SprintRiskAlertCode.NoHoursEstimated);
            Assert.Equal(SprintRiskLevel.AtRisk, result.RiskLevel);
        }

        [Fact]
        public void Analyze_SprintEncerradaComPendencias_SomenteAlertaCriticoDeEncerramento()
        {
            var snapshot = Snapshot(items: new[] { Task(1, "To Do", "Ana", 10) },
                start: new DateOnly(2026, 10, 19), finish: new DateOnly(2026, 10, 30));

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(SprintTimelineStatus.Ended, result.TimelineStatus);
            Assert.Equal(0, result.RemainingWorkingDays);
            var alert = Assert.Single(result.Alerts);
            Assert.Equal(SprintRiskAlertCode.SprintEndedWithPendingWork, alert.Code);
            Assert.Equal(SprintRiskLevel.Critical, result.RiskLevel);
            Assert.Empty(result.Forecast);
        }

        [Fact]
        public void Analyze_SprintEncerradaComTasksPendentesSemHoras_NaoReportaNoPrazo()
        {
            // Caso real: sprint encerrada, tasks "To Do" sem estimativa nem capacidade cadastrada.
            var snapshot = Snapshot(members: Array.Empty<TeamMemberCapacity>(), items: new[]
            {
                Task(1, "Done", "Ana", null),
                Task(2, "To Do", "Ana", null),
                Task(3, "To Do", null, null)
            }, start: new DateOnly(2026, 9, 16), finish: new DateOnly(2026, 9, 30));

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(2, result.PendingItems);
            Assert.Equal(SprintRiskLevel.Critical, result.RiskLevel);
            var alert = Assert.Single(result.Alerts);
            Assert.Equal(SprintRiskAlertCode.SprintEndedWithPendingWork, alert.Code);
            Assert.Equal(new[] { 2, 3 }, alert.WorkItemIds);
            Assert.Contains("2 item(ns) não concluído(s); 2 deles sem Horas Estimadas.", alert.Message);
        }

        [Fact]
        public void Analyze_SprintNaoIniciada_SemRitmoObservadoECapacidadeIntegral()
        {
            var snapshot = Snapshot(items: new[] { Task(1, "To Do", "Ana", 20) },
                start: new DateOnly(2026, 11, 16), finish: new DateOnly(2026, 11, 27));

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(SprintTimelineStatus.NotStarted, result.TimelineStatus);
            Assert.Equal(0, result.ElapsedWorkingDays);
            Assert.Equal(10, result.RemainingWorkingDays);
            Assert.Null(result.ObservedDailyDeliveryHours);
            Assert.Equal(60, result.RemainingCapacityHours);
        }

        [Fact]
        public void Analyze_HojeEmSaoPauloDiferenteDeUtc_UsaFusoConfigurado()
        {
            // 10/11 01h UTC ainda é 09/11 22h em São Paulo: segunda-feira, 5 dias restantes.
            var result = CreateAnalyzer(new DateTimeOffset(2026, 11, 10, 1, 0, 0, TimeSpan.Zero)).Analyze(Snapshot());

            Assert.Equal(new DateOnly(2026, 11, 9), result.AnalysisDate);
            Assert.Equal(5, result.RemainingWorkingDays);
        }

        [Fact]
        public void Analyze_Projecao_TemUmPontoPorDiaUtilRestanteMaisOPontoInicial()
        {
            var snapshot = Snapshot(items: new[] { Task(1, "Done", "Ana", 20), Task(2, "To Do", "Ana", 30) });

            var forecast = CreateAnalyzer().Analyze(snapshot).Forecast;

            Assert.Equal(6, forecast.Count);
            Assert.Equal(30, forecast[0].AtObservedPaceHours);
            Assert.Equal(new DateOnly(2026, 11, 13), forecast[^1].Date);
            Assert.Equal(0, forecast[^1].AtFullCapacityHours);   // 30 h / 6 h por dia zera no último dia
            Assert.Equal(10, forecast[^1].AtObservedPaceHours);  // 4 h/dia × 5 dias = 20 h entregues
        }

        [Fact]
        public void Analyze_MembrosOrdenadosDoMaisArriscadoParaOMenos()
        {
            var snapshot = Snapshot(
                members: new[] { Member("Ana", 6), Member("Bia", 6) },
                items: new[] { Task(1, "To Do", "Ana", 5), Task(2, "To Do", "Bia", 40) });

            var result = CreateAnalyzer().Analyze(snapshot);

            Assert.Equal(new[] { "Bia", "Ana" }, result.Members.Select(m => m.Member));
        }
    }
}
