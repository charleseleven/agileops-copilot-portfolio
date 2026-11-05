using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>Etapa 3 — para cada user story, coleta as Tasks e Bugs ligados por relação Child ou Related.</summary>
public sealed class MapRelatedItemsStep : IGmudStep
{
    private readonly IAzureDevOpsClient _boards;
    private readonly GmudConfig _config;

    public MapRelatedItemsStep(IAzureDevOpsClient boards, GmudConfig config)
    {
        _boards = boards ?? throw new ArgumentNullException(nameof(boards));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public string Key => GmudStepKeys.MapRelatedItems;
    public string Name => "Mapear tasks e bugs relacionados";
    public bool WritesRemote => false;
    public bool SupportsDryRun => true;

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        if (manifest.UserStories.Count == 0)
        {
            throw new InvalidOperationException("O manifesto não tem user stories — rode a etapa 2.");
        }

        var descriptionIds = manifest.UserStories.Select(us => us.Id).Append(manifest.Gmud.Id).ToHashSet();

        foreach (var userStory in manifest.UserStories)
        {
            var details = await _boards.GetWorkItemDetailsAsync(userStory.Id, cancellationToken);
            var items = new List<WorkItemEntry>();

            // Premissa 5: um Bug citado direto na Descrição é ele mesmo o item a varrer (além dos seus filhos).
            if (!string.Equals(userStory.Type, "User Story", StringComparison.OrdinalIgnoreCase)
                && _config.RelatedItemTypes.Contains(userStory.Type, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(ToEntry(details.Info));
            }

            foreach (var relatedId in details.GetLinkedWorkItemIds(WorkItemDetails.ChildRelation, WorkItemDetails.RelatedRelation))
            {
                // Itens também citados na Descrição são user stories da própria GMUD, não tasks desta.
                if (descriptionIds.Contains(relatedId))
                {
                    continue;
                }

                var related = await _boards.GetWorkItemAsync(relatedId, cancellationToken);
                if (_config.RelatedItemTypes.Contains(related.WorkItemType, StringComparer.OrdinalIgnoreCase))
                {
                    items.Add(ToEntry(related));
                }
            }

            if (items.Count == 0)
            {
                // Sem tasks/bugs, a própria user story é varrida na etapa 4 — melhor que perder o escopo em silêncio.
                manifest.AddWarning($"A user story #{userStory.Id} não tem Tasks/Bugs relacionados — os projetos serão buscados nela mesma.");
                items.Add(ToEntry(details.Info));
            }

            userStory.Items = items;
            context.Logger.Info($"#{userStory.Id} → {string.Join(", ", items.Select(i => $"{i.Type} #{i.Id}"))}.", context.StepNumber);
        }

        return manifest;
    }

    private static WorkItemEntry ToEntry(WorkItemInfo info) => new()
    {
        Id = info.Id,
        Type = info.WorkItemType,
        Title = info.Title,
        State = info.State
    };
}
