namespace AgilePredict.Models.DTOs.Flow
{
    /// <summary>
    /// Relatório final de diagnóstico preditivo (US #211): reúne as métricas de fluxo, os alertas
    /// de gargalo por status/responsável (AC #2) e as sugestões textuais geradas por IA (AC #3).
    /// </summary>
    public class FlowDiagnosticReport
    {
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

        public FlowMetricsPayload Metrics { get; set; } = new();

        public List<BottleneckAlert> BottleneckAlerts { get; set; } = new();

        /// <summary>
        /// Diagnóstico textual gerado pela LLM a partir do payload estatístico.
        /// </summary>
        public string AiDiagnosticSummary { get; set; } = string.Empty;

        /// <summary>
        /// Sugestões de ação corretiva geradas pela IA (ex.: redistribuição de carga).
        /// </summary>
        public List<string> AiRecommendations { get; set; } = new();

        /// <summary>
        /// Indica se a geração de insights por IA falhou; o restante do relatório permanece válido
        /// (as métricas estatísticas não dependem da LLM), mas o frontend deve sinalizar a falha.
        /// </summary>
        public bool AiInsightUnavailable { get; set; }
    }
}
