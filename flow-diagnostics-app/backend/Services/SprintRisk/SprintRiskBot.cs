using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Models.Flow;
using AgilePredict.Services.Interfaces;

namespace AgilePredict.Services
{
    /// <summary>
    /// Sprint Risk Bot (PBI 8.1): coleta assíncrona no Azure Boards → regra analítica → parecer da IA.
    /// Uma falha na LLM não derruba o relatório; a análise determinística vale por si.
    /// </summary>
    public sealed class SprintRiskBot : ISprintRiskBot
    {
        private readonly ISprintCapacityDataSource _dataSource;
        private readonly ISprintRiskAnalyzer _analyzer;
        private readonly ISprintRiskInsightGenerator _insightGenerator;
        private readonly ILogger<SprintRiskBot> _logger;

        public SprintRiskBot(
            ISprintCapacityDataSource dataSource,
            ISprintRiskAnalyzer analyzer,
            ISprintRiskInsightGenerator insightGenerator,
            ILogger<SprintRiskBot> logger)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _insightGenerator = insightGenerator ?? throw new ArgumentNullException(nameof(insightGenerator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SprintRiskReport> GenerateReportAsync(
            AzureDevOpsConnection connection,
            string? team,
            string? iterationPath,
            bool includeAiInsight,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);

            var snapshot = await _dataSource.GetSnapshotAsync(connection, team, iterationPath, cancellationToken);
            var analysis = _analyzer.Analyze(snapshot);
            var report = new SprintRiskReport { Analysis = analysis, GeneratedAtUtc = snapshot.CollectedAtUtc };

            if (!includeAiInsight)
            {
                return report;
            }

            try
            {
                var insight = await _insightGenerator.GenerateAsync(analysis, cancellationToken);
                if (insight.Success)
                {
                    report.AiSummary = insight.Summary;
                    report.AiRecommendations = insight.Recommendations;
                }
                else
                {
                    report.AiInsightUnavailable = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report.AiInsightUnavailable = true;
                _logger.LogError(ex, "Falha ao gerar o parecer de IA da sprint {Sprint}", analysis.IterationPath);
            }

            return report;
        }
    }
}
