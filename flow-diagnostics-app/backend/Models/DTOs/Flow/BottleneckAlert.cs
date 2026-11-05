namespace AgilePredict.Models.DTOs.Flow
{
    public enum BottleneckSeverity
    {
        Warning,
        Critical
    }

    /// <summary>
    /// Alerta de gargalo (AC #2): um card ultrapassou o limite estatístico de permanência
    /// em um status crítico e deve ter o risco de atraso destacado para o responsável atual.
    /// </summary>
    public class BottleneckAlert
    {
        public int ExternalId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? AssignedTo { get; set; }

        public string Status { get; set; } = string.Empty;

        public double HoursInStatus { get; set; }

        public double ThresholdHours { get; set; }

        public BottleneckSeverity Severity { get; set; }
    }
}
