using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.Flow;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AgilePredict.Services
{
    /// <summary>
    /// Implementação pura (sem dependências externas) do motor de cálculo estatístico de fluxo (Task #213).
    /// Toda a lógica é determinística e testável isoladamente a partir de snapshots já coletados.
    /// </summary>
    public class FlowMetricsCalculator : IFlowMetricsCalculator
    {
        private readonly FlowAnalyticsConfiguration _configuration;

        public FlowMetricsCalculator(IOptions<FlowAnalyticsConfiguration> configuration)
        {
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        }

        public FlowMetricsPayload Calculate(
            string iterationPath,
            IReadOnlyList<WorkItemFlowSnapshot> activeItems,
            IReadOnlyList<WorkItemFlowSnapshot> baselineCompletedItems,
            DateTime? asOfUtc = null)
        {
            ArgumentNullException.ThrowIfNull(activeItems);
            ArgumentNullException.ThrowIfNull(baselineCompletedItems);

            var now = asOfUtc ?? DateTime.UtcNow;

            var statusBaselines = BuildStatusBaselines(baselineCompletedItems);
            var (cycleTimeBaselineMean, cycleTimeBaselineStdDev, cycleTimeSampleSize) =
                BuildCycleTimeBaseline(baselineCompletedItems);

            var cardResults = activeItems
                .Select(item => BuildCardMetrics(item, now, statusBaselines, cycleTimeBaselineMean, cycleTimeBaselineStdDev, cycleTimeSampleSize))
                .ToList();

            var cards = cardResults.Select(r => r.Card).ToList();

            var bottlenecks = cardResults
                .Where(r => r.Card.IsCurrentStatusBottleneck)
                .Select(r => ToBottleneckAlert(r.Card, r.ThresholdHours))
                .ToList();

            return new FlowMetricsPayload
            {
                IterationPath = iterationPath,
                GeneratedAtUtc = now,
                BaselineWindowDays = _configuration.BaselineWindowDays,
                Cards = cards,
                WeeklyThroughput = BuildWeeklyThroughput(baselineCompletedItems),
                StatusBaselines = statusBaselines,
                BottleneckAlerts = bottlenecks
            };
        }

        private (CardFlowMetrics Card, double ThresholdHours) BuildCardMetrics(
            WorkItemFlowSnapshot item,
            DateTime asOfUtc,
            Dictionary<string, StatusBaselineStats> statusBaselines,
            double cycleTimeBaselineMean,
            double cycleTimeBaselineStdDev,
            int cycleTimeSampleSize)
        {
            var cycleTimeHours = SumActiveWorkHours(item.StatusHistory, asOfUtc);
            var leadTimeHours = Math.Max(0, (asOfUtc - item.CreatedAtUtc).TotalHours);
            var currentPeriod = item.StatusHistory.LastOrDefault();
            var timeInCurrentStatusHours = currentPeriod?.DurationInHours(asOfUtc) ?? 0;

            var isAboveCycleTimeBaseline = cycleTimeSampleSize >= _configuration.MinimumSampleSizeForBaseline
                ? cycleTimeHours > cycleTimeBaselineMean + _configuration.StdDevMultiplier * cycleTimeBaselineStdDev
                : cycleTimeHours > _configuration.DefaultCriticalThresholdDays * 24;

            var threshold = ResolveThresholdHours(item.CurrentStatus, statusBaselines);
            var isBottleneck = timeInCurrentStatusHours > threshold;

            var card = new CardFlowMetrics
            {
                ExternalId = item.ExternalId,
                Title = item.Title,
                AssignedTo = item.AssignedTo,
                CurrentStatus = item.CurrentStatus,
                CycleTimeHours = Math.Round(cycleTimeHours, 2),
                LeadTimeHours = Math.Round(leadTimeHours, 2),
                TimeInCurrentStatusHours = Math.Round(timeInCurrentStatusHours, 2),
                IsCycleTimeAboveBaseline = isAboveCycleTimeBaseline,
                IsCurrentStatusBottleneck = isBottleneck
            };

            return (card, threshold);
        }

        private double ResolveThresholdHours(string status, Dictionary<string, StatusBaselineStats> statusBaselines)
        {
            return statusBaselines.TryGetValue(status, out var baseline)
                ? baseline.ThresholdHours
                : _configuration.DefaultCriticalThresholdDays * 24;
        }

        private static BottleneckAlert ToBottleneckAlert(CardFlowMetrics card, double thresholdHours)
        {
            return new BottleneckAlert
            {
                ExternalId = card.ExternalId,
                Title = card.Title,
                AssignedTo = card.AssignedTo,
                Status = card.CurrentStatus,
                HoursInStatus = card.TimeInCurrentStatusHours,
                ThresholdHours = Math.Round(thresholdHours, 2),
                Severity = card.TimeInCurrentStatusHours > thresholdHours * 1.5
                    ? BottleneckSeverity.Critical
                    : BottleneckSeverity.Warning
            };
        }

        private double SumActiveWorkHours(IReadOnlyList<WorkItemStatusPeriod> history, DateTime asOfUtc)
        {
            return history
                .Where(p => _configuration.StartStatuses.Contains(p.Status, StringComparer.OrdinalIgnoreCase))
                .Sum(p => p.DurationInHours(asOfUtc));
        }

        private Dictionary<string, StatusBaselineStats> BuildStatusBaselines(IReadOnlyList<WorkItemFlowSnapshot> baselineCompletedItems)
        {
            var samplesByStatus = baselineCompletedItems
                .SelectMany(item => item.StatusHistory)
                .Where(p => p.ExitedAtUtc.HasValue)
                .GroupBy(p => p.Status, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => p.DurationInHours(p.ExitedAtUtc!.Value)).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, StatusBaselineStats>(StringComparer.OrdinalIgnoreCase);

            foreach (var (status, samples) in samplesByStatus)
            {
                var (mean, stdDev) = MeanAndStdDev(samples);
                var isStatisticallyDerived = samples.Count >= _configuration.MinimumSampleSizeForBaseline;
                var threshold = isStatisticallyDerived
                    ? mean + _configuration.StdDevMultiplier * stdDev
                    : _configuration.DefaultCriticalThresholdDays * 24;

                result[status] = new StatusBaselineStats
                {
                    Status = status,
                    MeanHours = Math.Round(mean, 2),
                    StdDevHours = Math.Round(stdDev, 2),
                    SampleSize = samples.Count,
                    ThresholdHours = Math.Round(threshold, 2),
                    IsStatisticallyDerived = isStatisticallyDerived
                };
            }

            return result;
        }

        private (double Mean, double StdDev, int SampleSize) BuildCycleTimeBaseline(IReadOnlyList<WorkItemFlowSnapshot> baselineCompletedItems)
        {
            var samples = baselineCompletedItems
                .Select(item => SumActiveWorkHours(item.StatusHistory, item.StatusHistory.LastOrDefault()?.EnteredAtUtc ?? item.CreatedAtUtc))
                .ToList();

            var (mean, stdDev) = MeanAndStdDev(samples);
            return (mean, stdDev, samples.Count);
        }

        private static (double Mean, double StdDev) MeanAndStdDev(IReadOnlyList<double> samples)
        {
            if (samples.Count == 0)
            {
                return (0, 0);
            }

            var mean = samples.Average();

            if (samples.Count == 1)
            {
                return (mean, 0);
            }

            var variance = samples.Sum(s => Math.Pow(s - mean, 2)) / (samples.Count - 1);
            return (mean, Math.Sqrt(variance));
        }

        private static List<WeeklyThroughputPoint> BuildWeeklyThroughput(IReadOnlyList<WorkItemFlowSnapshot> baselineCompletedItems)
        {
            return baselineCompletedItems
                .Select(item => item.StatusHistory.LastOrDefault()?.EnteredAtUtc ?? item.CreatedAtUtc)
                .GroupBy(GetWeekStart)
                .OrderBy(g => g.Key)
                .Select(g => new WeeklyThroughputPoint { WeekStartUtc = g.Key, CompletedCount = g.Count() })
                .ToList();
        }

        private static DateTime GetWeekStart(DateTime date)
        {
            var diff = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            return date.Date.AddDays(-diff);
        }
    }
}
