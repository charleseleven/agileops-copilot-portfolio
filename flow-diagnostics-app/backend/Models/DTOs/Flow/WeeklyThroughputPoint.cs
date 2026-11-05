namespace AgilePredict.Models.DTOs.Flow
{
    /// <summary>
    /// Vazão (throughput) semanal: quantidade de cards concluídos na semana iniciada em <see cref="WeekStartUtc"/>.
    /// </summary>
    public class WeeklyThroughputPoint
    {
        public DateTime WeekStartUtc { get; set; }

        public int CompletedCount { get; set; }
    }
}
