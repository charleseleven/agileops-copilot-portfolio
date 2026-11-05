namespace AgilePredict.Models.DTOs.Flow
{
    /// <summary>
    /// Payload JSON padronizado produzido pelo motor de cálculo estatístico de fluxo (Task #213),
    /// consumido tanto pela camada de IA (Task #214) quanto pelo frontend (Task #215).
    /// </summary>
    public class FlowMetricsPayload
    {
        public string IterationPath { get; set; } = string.Empty;

        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

        public int BaselineWindowDays { get; set; }

        /// <summary>
        /// Métricas de cada card ativo (ainda não concluído) da sprint/iteração analisada.
        /// </summary>
        public List<CardFlowMetrics> Cards { get; set; } = new();

        /// <summary>
        /// Vazão semanal, derivada dos itens concluídos dentro da janela de baseline.
        /// </summary>
        public List<WeeklyThroughputPoint> WeeklyThroughput { get; set; } = new();

        /// <summary>
        /// Linha de base estatística por status, usada para detectar gargalos (AC #2).
        /// </summary>
        public Dictionary<string, StatusBaselineStats> StatusBaselines { get; set; } = new();

        /// <summary>
        /// Cards cujo tempo de permanência no status atual ultrapassa o limite estatístico (AC #2).
        /// </summary>
        public List<BottleneckAlert> BottleneckAlerts { get; set; } = new();
    }
}
