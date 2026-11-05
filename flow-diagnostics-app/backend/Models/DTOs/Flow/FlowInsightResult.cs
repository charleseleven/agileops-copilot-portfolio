namespace AgilePredict.Models.DTOs.Flow
{
    /// <summary>
    /// Resultado da geração de insights por IA (Task #214): diagnóstico textual dos gargalos
    /// e sugestões de mitigação, a partir do payload estatístico de fluxo.
    /// </summary>
    public class FlowInsightResult
    {
        public bool Success { get; set; }

        public string Summary { get; set; } = string.Empty;

        public List<string> Recommendations { get; set; } = new();

        public string? ErrorMessage { get; set; }
    }
}
