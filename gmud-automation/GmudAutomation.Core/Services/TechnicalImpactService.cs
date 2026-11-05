using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>
/// US 1.2: varre os comentários de um work item filho (Task 1), identifica os projetos alterados
/// via as tags padrão do time (Task 2) e valida a existência da branch feature/us_&lt;numero&gt;
/// no repositório correspondente a cada projeto (Task 3).
/// Quando a User Story em si não tem nenhuma tag de projeto em seus comentários, a busca também é
/// feita nos comentários de seus work items filhos (Tasks/Bugs) — é comum o desenvolvedor registrar
/// o projeto alterado no comentário da Task, não da User Story. A branch usada continua sempre sendo
/// feature/us_&lt;numero da User Story&gt;, independentemente de onde a tag foi encontrada.
/// </summary>
public sealed class TechnicalImpactService : ITechnicalImpactService
{
    private readonly IAzureDevOpsClient _client;
    private readonly IProjectTagExtractor _tagExtractor;

    public TechnicalImpactService(IAzureDevOpsClient client, IProjectTagExtractor tagExtractor)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _tagExtractor = tagExtractor ?? throw new ArgumentNullException(nameof(tagExtractor));
    }

    public async Task<IReadOnlyList<TechnicalImpact>> IdentifyTechnicalImpactAsync(int workItemId, CancellationToken cancellationToken = default)
    {
        var branchName = $"feature/us_{workItemId}";
        var results = new List<TechnicalImpact>();
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await CollectImpactFromCommentsAsync(workItemId, branchName, results, seenProjects, cancellationToken);

        if (results.Count == 0)
        {
            var childIds = await _client.GetChildWorkItemIdsAsync(workItemId, cancellationToken);
            foreach (var childId in childIds)
            {
                await CollectImpactFromCommentsAsync(childId, branchName, results, seenProjects, cancellationToken);
            }
        }

        return results;
    }

    private async Task CollectImpactFromCommentsAsync(
        int workItemIdForComments,
        string branchName,
        List<TechnicalImpact> results,
        HashSet<string> seenProjects,
        CancellationToken cancellationToken)
    {
        var comments = await _client.GetWorkItemCommentsAsync(workItemIdForComments, cancellationToken);

        foreach (var comment in comments)
        {
            foreach (var projectName in _tagExtractor.ExtractProjectTags(comment.Text))
            {
                if (!seenProjects.Add(projectName))
                {
                    continue;
                }

                var branchExists = await _client.BranchExistsAsync(projectName, branchName, cancellationToken);
                results.Add(new TechnicalImpact(projectName, branchName, branchExists));
            }
        }
    }
}
