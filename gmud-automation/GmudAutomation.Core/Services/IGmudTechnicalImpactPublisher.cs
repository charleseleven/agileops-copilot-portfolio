using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>Executa a análise de impacto técnico de um work item e publica o resultado como comentário no seu card.</summary>
public interface IGmudTechnicalImpactPublisher
{
    Task<IReadOnlyList<TechnicalImpact>> AnalyzeAndPublishAsync(int workItemId, CancellationToken cancellationToken = default);
}
