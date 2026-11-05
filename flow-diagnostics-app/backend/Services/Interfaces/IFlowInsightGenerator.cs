using AgilePredict.Models.DTOs.Flow;

namespace AgilePredict.Services.Interfaces
{
    /// <summary>
    /// Integração com a LLM para geração de insights preditivos (Task #214): recebe o payload
    /// estatístico de fluxo, aplica as regras de negócio ágeis via prompt de sistema e retorna
    /// um diagnóstico textual estruturado com os gargalos e sugestões de mitigação.
    /// </summary>
    public interface IFlowInsightGenerator
    {
        Task<FlowInsightResult> GenerateAsync(FlowMetricsPayload payload, CancellationToken cancellationToken = default);
    }
}
