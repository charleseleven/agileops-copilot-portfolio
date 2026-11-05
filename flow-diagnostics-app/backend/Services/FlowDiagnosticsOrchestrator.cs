using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.Flow;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AgilePredict.Services
{
    /// <summary>
    /// Implementação do pipeline de diagnóstico preditivo de fluxo (US #211, Feature #210).
    /// </summary>
    public class FlowDiagnosticsOrchestrator : IFlowDiagnosticsOrchestrator
    {
        private readonly IWorkItemFlowDataSource _dataSource;
        private readonly IFlowMetricsCalculator _calculator;
        private readonly IFlowInsightGenerator _insightGenerator;
        private readonly FlowAnalyticsConfiguration _configuration;
        private readonly ILogger<FlowDiagnosticsOrchestrator> _logger;

        public FlowDiagnosticsOrchestrator(
            IWorkItemFlowDataSource dataSource,
            IFlowMetricsCalculator calculator,
            IFlowInsightGenerator insightGenerator,
            IOptions<FlowAnalyticsConfiguration> configuration,
            ILogger<FlowDiagnosticsOrchestrator> logger)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _insightGenerator = insightGenerator ?? throw new ArgumentNullException(nameof(insightGenerator));
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<FlowDiagnosticReport> GenerateReportAsync(
            AzureDevOpsConnection connection,
            string iterationPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (string.IsNullOrWhiteSpace(iterationPath))
            {
                throw new ArgumentException("O caminho da iteração (sprint) é obrigatório", nameof(iterationPath));
            }

            var baselineSinceUtc = DateTime.UtcNow.AddDays(-_configuration.BaselineWindowDays);

            // AC #1: Cycle Time de cada card ativo comparado à linha de base histórica de 90 dias.
            var activeItems = await _dataSource.GetActiveWorkItemsAsync(connection, iterationPath, cancellationToken);
            var baselineItems = await _dataSource.GetCompletedWorkItemsSinceAsync(connection, baselineSinceUtc, cancellationToken);

            var metrics = _calculator.Calculate(iterationPath, activeItems, baselineItems);

            var report = new FlowDiagnosticReport
            {
                GeneratedAtUtc = metrics.GeneratedAtUtc,
                Metrics = metrics,
                // AC #2: destaca o risco de atraso associado ao responsável atual.
                BottleneckAlerts = metrics.BottleneckAlerts
            };

            // AC #3: o painel deve exibir sugestões textuais geradas por IA. Uma falha na LLM não
            // deve derrubar o relatório estatístico, que já é útil por si só.
            try
            {
                var insight = await _insightGenerator.GenerateAsync(metrics, cancellationToken);
                if (insight.Success)
                {
                    report.AiDiagnosticSummary = insight.Summary;
                    report.AiRecommendations = insight.Recommendations;
                }
                else
                {
                    report.AiInsightUnavailable = true;
                    _logger.LogWarning("Insight de IA indisponível para a iteração {IterationPath}: {Error}", iterationPath, insight.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                report.AiInsightUnavailable = true;
                _logger.LogError(ex, "Erro inesperado ao gerar insight de IA para a iteração {IterationPath}", iterationPath);
            }

            return report;
        }
    }
}
