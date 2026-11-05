using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>
/// US 1.1: acessa a User Story principal da GMUD, extrai os links de tasks/bugs filhos da sua descrição
/// e resolve os metadados de cada um, montando a hierarquia completa do escopo da publicação.
/// </summary>
public sealed class GmudHierarchyService : IGmudHierarchyService
{
    private readonly IAzureDevOpsClient _client;
    private readonly IWorkItemLinkParser _linkParser;

    public GmudHierarchyService(IAzureDevOpsClient client, IWorkItemLinkParser linkParser)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _linkParser = linkParser ?? throw new ArgumentNullException(nameof(linkParser));
    }

    public async Task<GmudRequest> BuildHierarchyAsync(int gmudWorkItemId, CancellationToken cancellationToken = default)
    {
        var mainWorkItem = await _client.GetWorkItemAsync(gmudWorkItemId, cancellationToken);
        var childIds = _linkParser.ExtractChildWorkItemIds(mainWorkItem.DescriptionHtml);

        var children = new List<WorkItemInfo>(childIds.Count);
        foreach (var childId in childIds)
        {
            children.Add(await _client.GetWorkItemAsync(childId, cancellationToken));
        }

        return new GmudRequest
        {
            Id = mainWorkItem.Id,
            Title = mainWorkItem.Title,
            ChildWorkItems = children
        };
    }
}
