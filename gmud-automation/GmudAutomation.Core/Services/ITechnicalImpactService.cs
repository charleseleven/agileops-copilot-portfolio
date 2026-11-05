using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>Identifica o impacto técnico (projetos alterados e branches de origem) de um work item filho (US 1.2).</summary>
public interface ITechnicalImpactService
{
    Task<IReadOnlyList<TechnicalImpact>> IdentifyTechnicalImpactAsync(int workItemId, CancellationToken cancellationToken = default);
}
