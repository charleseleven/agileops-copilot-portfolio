namespace AgilePredict.Models.Flow
{
    /// <summary>
    /// Representa um período contínuo em que um Work Item permaneceu em um determinado status,
    /// derivado do histórico de transições (System.State) retornado pela API de Work Item Updates.
    /// </summary>
    public class WorkItemStatusPeriod
    {
        public string Status { get; set; } = string.Empty;

        public DateTime EnteredAtUtc { get; set; }

        /// <summary>
        /// Nulo quando o status ainda é o status atual do card (período em andamento).
        /// </summary>
        public DateTime? ExitedAtUtc { get; set; }

        /// <summary>
        /// Duração do período. Quando o status ainda está aberto, calcula até o instante informado
        /// (normalmente "agora"), permitindo medir há quanto tempo o card está parado no status atual.
        /// </summary>
        public double DurationInHours(DateTime asOfUtc)
        {
            var end = ExitedAtUtc ?? asOfUtc;
            var elapsed = end - EnteredAtUtc;
            return elapsed.TotalHours < 0 ? 0 : elapsed.TotalHours;
        }
    }
}
