namespace AgilePredict.Models.Flow
{
    /// <summary>
    /// Retrato normalizado de um Work Item para fins de análise de fluxo, independente da fonte
    /// de dados (Azure DevOps, Jira, etc.). É o contrato de saída da Task #212 (coleta) e de
    /// entrada da Task #213 (cálculo estatístico).
    /// </summary>
    public class WorkItemFlowSnapshot
    {
        public int ExternalId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? AssignedTo { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public string CurrentStatus { get; set; } = string.Empty;

        public bool IsDone { get; set; }

        /// <summary>
        /// Histórico de status em ordem cronológica, do primeiro ao mais recente.
        /// </summary>
        public List<WorkItemStatusPeriod> StatusHistory { get; set; } = new();
    }
}
