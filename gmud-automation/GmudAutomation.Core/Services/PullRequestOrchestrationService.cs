using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IPullRequestOrchestrationService"/>
public sealed class PullRequestOrchestrationService : IPullRequestOrchestrationService
{
    private readonly IAzureDevOpsClient _client;

    public PullRequestOrchestrationService(IAzureDevOpsClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<PullRequestOutcome>> OrchestrateAsync(
        IReadOnlyList<RepositoryPullRequestRequest> requests, CancellationToken cancellationToken = default)
    {
        var outcomes = new List<PullRequestOutcome>(requests.Count);

        foreach (var request in requests)
        {
            // PRs de Database ficam apenas criadas (Draft/Ativa) — aprovação e merge exigem revisão manual do DBA.
            var isDraft = request.Kind == RepositoryKind.Database;
            var pullRequest = await _client.CreatePullRequestAsync(
                request.RepositoryName, request.SourceBranch, request.TargetBranch, request.Title, request.Description, isDraft, cancellationToken);

            int? buildId = null;
            var completed = false;

            if (request.Kind != RepositoryKind.Database)
            {
                await _client.CompletePullRequestAsync(request.RepositoryName, pullRequest.Id, pullRequest.LastMergeSourceCommitId, cancellationToken);
                completed = true;
                buildId = await _client.GetLatestBuildIdForBranchAsync(request.TargetBranch, cancellationToken);
            }

            outcomes.Add(new PullRequestOutcome(request.RepositoryName, request.Kind, pullRequest.Id, pullRequest.Url, completed, buildId));
        }

        return outcomes;
    }
}
