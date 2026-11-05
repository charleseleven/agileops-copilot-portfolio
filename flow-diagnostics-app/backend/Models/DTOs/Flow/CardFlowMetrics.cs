namespace AgilePredict.Models.DTOs.Flow
{
    /// <summary>
    /// Métricas de fluxo calculadas para um card (Work Item) ativo da sprint atual.
    /// </summary>
    public class CardFlowMetrics
    {
        public int ExternalId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? AssignedTo { get; set; }

        public string CurrentStatus { get; set; } = string.Empty;

        /// <summary>
        /// Cycle Time acumulado até o momento (soma das durações nos status de trabalho ativo).
        /// </summary>
        public double CycleTimeHours { get; set; }

        /// <summary>
        /// Tempo decorrido desde a criação do card (Lead Time parcial, para cards ainda ativos).
        /// </summary>
        public double LeadTimeHours { get; set; }

        /// <summary>
        /// Há quanto tempo o card está no status atual.
        /// </summary>
        public double TimeInCurrentStatusHours { get; set; }

        /// <summary>
        /// AC #1: Cycle Time do card comparado à linha de base histórica de 90 dias.
        /// True quando o Cycle Time atual excede a linha de base (média + desvio padrão).
        /// </summary>
        public bool IsCycleTimeAboveBaseline { get; set; }

        /// <summary>
        /// AC #2: o card ultrapassou o limite estatístico de permanência no status crítico atual
        /// (ex.: Code Review > 3 dias) e deve ser destacado como risco de atraso.
        /// </summary>
        public bool IsCurrentStatusBottleneck { get; set; }
    }
}
