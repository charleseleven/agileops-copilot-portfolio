using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>
/// US 3.1: cria os Pull Requests de release, completando automaticamente os de API/WEB e mantendo
/// os de Database apenas criados (Draft/Ativa) para aprovação manual do DBA.
/// </summary>
public interface IPullRequestOrchestrationService
{
    Task<IReadOnlyList<PullRequestOutcome>> OrchestrateAsync(
        IReadOnlyList<RepositoryPullRequestRequest> requests, CancellationToken cancellationToken = default);
}
