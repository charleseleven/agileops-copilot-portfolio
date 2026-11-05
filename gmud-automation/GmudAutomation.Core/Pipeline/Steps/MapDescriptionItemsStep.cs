using GmudAutomation.Core.Client;
using GmudAutomation.Core.Pipeline.Analysis;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 2 — lê a Descrição da GMUD e extrai os work items citados (links e <c>#id</c>). Entram os tipos de
/// <see cref="GmudConfig.DescriptionItemTypes"/> (User Story e, pela premissa 5, Bug); os demais viram aviso.
/// </summary>
public sealed class MapDescriptionItemsStep : IGmudStep
{
    private readonly IAzureDevOpsClient _boards;
    private readonly GmudConfig _config;
    private readonly GmudNamingConventions _naming;

    public MapDescriptionItemsStep(IAzureDevOpsClient boards, GmudConfig config, GmudNamingConventions naming)
    {
        _boards = boards ?? throw new ArgumentNullException(nameof(boards));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
    }

    public string Key => GmudStepKeys.MapDescriptionItems;
    public string Name => "Mapear os itens citados na Descrição";
    public bool WritesRemote => false;
    public bool SupportsDryRun => true;

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        GmudStepSupport.RequireResolvedGmud(manifest);

        var gmud = await _boards.GetWorkItemAsync(manifest.Gmud.Id, cancellationToken);
        var referencedIds = WorkItemReferenceParser.Extract(gmud.DescriptionHtml).Where(id => id != manifest.Gmud.Id).ToList();

        if (referencedIds.Count == 0)
        {
            throw new GmudValidationException("descricao",
                $"A Descrição da GMUD #{manifest.Gmud.Id} não cita nenhum work item (links _workitems/edit/<id> ou #<id>).");
        }

        var userStories = new List<UserStoryEntry>();
        foreach (var id in referencedIds)
        {
            var item = await _boards.GetWorkItemAsync(id, cancellationToken);
            if (!_config.DescriptionItemTypes.Contains(item.WorkItemType, StringComparer.OrdinalIgnoreCase))
            {
                manifest.AddWarning(
                    $"#{id} ({item.WorkItemType}) citado na Descrição foi ignorado — só {string.Join("/", _config.DescriptionItemTypes)} entram na GMUD.");
                continue;
            }

            userStories.Add(new UserStoryEntry
            {
                Id = id,
                Type = item.WorkItemType,
                Title = item.Title,
                State = item.State,
                Branch = _naming.ItemBranch(id)
            });
        }

        if (userStories.Count == 0)
        {
            throw new GmudValidationException("descricao",
                $"Nenhum dos itens citados na Descrição ({string.Join(", ", referencedIds.Select(i => "#" + i))}) é do tipo {string.Join("/", _config.DescriptionItemTypes)}.");
        }

        manifest.UserStories = userStories;
        context.Logger.Info($"User stories da GMUD: {string.Join(", ", userStories.Select(us => $"#{us.Id}"))}.", context.StepNumber);
        return manifest;
    }
}
