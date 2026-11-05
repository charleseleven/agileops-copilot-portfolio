using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.Flow;
using AgilePredict.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgilePredict.Tests.Services.Flow
{
    public class FlowMetricsCalculatorTests
    {
        private static readonly DateTime BaseDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static FlowMetricsCalculator CreateCalculator(FlowAnalyticsConfiguration? config = null)
        {
            config ??= new FlowAnalyticsConfiguration
            {
                StartStatuses = new List<string> { "In Progress" },
                DoneStatuses = new List<string> { "Done" },
                MinimumSampleSizeForBaseline = 3,
                StdDevMultiplier = 1.0,
                DefaultCriticalThresholdDays = 3.0
            };

            return new FlowMetricsCalculator(Options.Create(config));
        }

        private static WorkItemFlowSnapshot CompletedItem(int id, double inProgressHours)
        {
            var enteredInProgress = BaseDate;
            var enteredDone = BaseDate.AddHours(inProgressHours);

            return new WorkItemFlowSnapshot
            {
                ExternalId = id,
                Title = $"Card {id}",
                AssignedTo = "dev@example.com",
                CreatedAtUtc = BaseDate,
                CurrentStatus = "Done",
                IsDone = true,
                StatusHistory = new List<WorkItemStatusPeriod>
                {
                    new() { Status = "In Progress", EnteredAtUtc = enteredInProgress, ExitedAtUtc = enteredDone },
                    new() { Status = "Done", EnteredAtUtc = enteredDone }
                }
            };
        }

        [Fact]
        public void Calculate_CardCycleTimeAboveBaseline_FlagsRisk()
        {
            // Baseline: 3 concluídos com In Progress = 10h, 20h, 30h => média=20h, desvio=10h, limite=30h
            var baseline = new List<WorkItemFlowSnapshot>
            {
                CompletedItem(1, 10),
                CompletedItem(2, 20),
                CompletedItem(3, 30)
            };

            var activeItem = new WorkItemFlowSnapshot
            {
                ExternalId = 99,
                Title = "Card em risco",
                AssignedTo = "dev@example.com",
                CreatedAtUtc = BaseDate,
                CurrentStatus = "In Progress",
                StatusHistory = new List<WorkItemStatusPeriod>
                {
                    new() { Status = "In Progress", EnteredAtUtc = BaseDate }
                }
            };

            var asOf = BaseDate.AddHours(50); // 50h em "In Progress" > limite de 30h
            var calculator = CreateCalculator();

            var payload = calculator.Calculate("Sprint 1", new[] { activeItem }, baseline, asOf);

            var card = Assert.Single(payload.Cards);
            Assert.Equal(50, card.CycleTimeHours);
            Assert.True(card.IsCycleTimeAboveBaseline);
        }

        [Fact]
        public void Calculate_CardCycleTimeWithinBaseline_DoesNotFlagRisk()
        {
            var baseline = new List<WorkItemFlowSnapshot>
            {
                CompletedItem(1, 10),
                CompletedItem(2, 20),
                CompletedItem(3, 30)
            };

            var activeItem = new WorkItemFlowSnapshot
            {
                ExternalId = 99,
                Title = "Card dentro da baseline",
                CreatedAtUtc = BaseDate,
                CurrentStatus = "In Progress",
                StatusHistory = new List<WorkItemStatusPeriod>
                {
                    new() { Status = "In Progress", EnteredAtUtc = BaseDate }
                }
            };

            var asOf = BaseDate.AddHours(25); // abaixo do limite de 30h
            var calculator = CreateCalculator();

            var payload = calculator.Calculate("Sprint 1", new[] { activeItem }, baseline, asOf);

            var card = Assert.Single(payload.Cards);
            Assert.False(card.IsCycleTimeAboveBaseline);
        }

        [Fact]
        public void Calculate_StatusBaseline_ComputesMeanAndStdDevFromHistoricalSample()
        {
            var baseline = new List<WorkItemFlowSnapshot>
            {
                CompletedItem(1, 10),
                CompletedItem(2, 20),
                CompletedItem(3, 30)
            };

            var calculator = CreateCalculator();
            var payload = calculator.Calculate("Sprint 1", Array.Empty<WorkItemFlowSnapshot>(), baseline, BaseDate);

            var baselineStats = payload.StatusBaselines["In Progress"];
            Assert.Equal(3, baselineStats.SampleSize);
            Assert.Equal(20, baselineStats.MeanHours);
            Assert.Equal(10, baselineStats.StdDevHours);
            Assert.Equal(30, baselineStats.ThresholdHours);
            Assert.True(baselineStats.IsStatisticallyDerived);
        }

        [Fact]
        public void Calculate_StatusWithInsufficientSample_FallsBackToDefaultThreshold()
        {
            // Apenas 2 amostras de "Code Review" (< MinimumSampleSizeForBaseline = 3)
            var baseline = new List<WorkItemFlowSnapshot>
            {
                new()
                {
                    ExternalId = 1,
                    CurrentStatus = "Done",
                    IsDone = true,
                    CreatedAtUtc = BaseDate,
                    StatusHistory = new List<WorkItemStatusPeriod>
                    {
                        new() { Status = "Code Review", EnteredAtUtc = BaseDate, ExitedAtUtc = BaseDate.AddHours(5) },
                        new() { Status = "Done", EnteredAtUtc = BaseDate.AddHours(5) }
                    }
                },
                new()
                {
                    ExternalId = 2,
                    CurrentStatus = "Done",
                    IsDone = true,
                    CreatedAtUtc = BaseDate,
                    StatusHistory = new List<WorkItemStatusPeriod>
                    {
                        new() { Status = "Code Review", EnteredAtUtc = BaseDate, ExitedAtUtc = BaseDate.AddHours(8) },
                        new() { Status = "Done", EnteredAtUtc = BaseDate.AddHours(8) }
                    }
                }
            };

            var activeItem = new WorkItemFlowSnapshot
            {
                ExternalId = 42,
                Title = "Card parado em Code Review",
                AssignedTo = "reviewer@example.com",
                CreatedAtUtc = BaseDate,
                CurrentStatus = "Code Review",
                StatusHistory = new List<WorkItemStatusPeriod>
                {
                    new() { Status = "Code Review", EnteredAtUtc = BaseDate }
                }
            };

            // DefaultCriticalThresholdDays = 3.0 => 72h. 80h > 72h deve gerar alerta (Warning, pois < 72*1.5).
            var asOf = BaseDate.AddHours(80);
            var calculator = CreateCalculator();

            var payload = calculator.Calculate("Sprint 1", new[] { activeItem }, baseline, asOf);

            var baselineStats = payload.StatusBaselines["Code Review"];
            Assert.False(baselineStats.IsStatisticallyDerived);
            Assert.Equal(72, baselineStats.ThresholdHours);

            var card = Assert.Single(payload.Cards);
            Assert.True(card.IsCurrentStatusBottleneck);

            var alert = Assert.Single(payload.BottleneckAlerts);
            Assert.Equal(42, alert.ExternalId);
            Assert.Equal("reviewer@example.com", alert.AssignedTo);
            Assert.Equal("Code Review", alert.Status);
            Assert.Equal(BottleneckSeverity.Warning, alert.Severity);
        }

        [Fact]
        public void Calculate_TimeInStatusFarAboveThreshold_FlagsCriticalSeverity()
        {
            var baseline = new List<WorkItemFlowSnapshot>();

            var activeItem = new WorkItemFlowSnapshot
            {
                ExternalId = 7,
                Title = "Card crítico",
                CreatedAtUtc = BaseDate,
                CurrentStatus = "Code Review",
                StatusHistory = new List<WorkItemStatusPeriod>
                {
                    new() { Status = "Code Review", EnteredAtUtc = BaseDate }
                }
            };

            // Threshold padrão = 72h (sem amostra histórica); 150h > 72h * 1.5 (108h) => Critical
            var asOf = BaseDate.AddHours(150);
            var calculator = CreateCalculator();

            var payload = calculator.Calculate("Sprint 1", new[] { activeItem }, baseline, asOf);

            var alert = Assert.Single(payload.BottleneckAlerts);
            Assert.Equal(BottleneckSeverity.Critical, alert.Severity);
        }

        [Fact]
        public void Calculate_WeeklyThroughput_GroupsCompletedItemsByWeekStart()
        {
            // Segunda-feira, 05/01/2026
            var weekStart = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

            WorkItemFlowSnapshot DoneOn(int id, DateTime doneAt) => new()
            {
                ExternalId = id,
                CurrentStatus = "Done",
                IsDone = true,
                CreatedAtUtc = doneAt.AddDays(-1),
                StatusHistory = new List<WorkItemStatusPeriod>
                {
                    new() { Status = "Done", EnteredAtUtc = doneAt }
                }
            };

            var baseline = new List<WorkItemFlowSnapshot>
            {
                DoneOn(1, weekStart.AddDays(1)),  // mesma semana
                DoneOn(2, weekStart.AddDays(3)),  // mesma semana
                DoneOn(3, weekStart.AddDays(9))   // semana seguinte
            };

            var calculator = CreateCalculator();
            var payload = calculator.Calculate("Sprint 1", Array.Empty<WorkItemFlowSnapshot>(), baseline, BaseDate);

            Assert.Equal(2, payload.WeeklyThroughput.Count);
            Assert.Equal(2, payload.WeeklyThroughput.Single(w => w.WeekStartUtc == weekStart).CompletedCount);
            Assert.Equal(1, payload.WeeklyThroughput.Single(w => w.WeekStartUtc == weekStart.AddDays(7)).CompletedCount);
        }
    }
}
