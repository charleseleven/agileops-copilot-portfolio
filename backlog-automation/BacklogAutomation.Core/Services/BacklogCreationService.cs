using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Providers;

namespace BacklogAutomation.Core.Services;

/// <summary>
/// Percorre o backlog em profundidade (Epic → Features → User Stories → Tasks/Bugs) e cria cada card
/// somente depois do pai, vinculando-o ao pai já criado — assim a hierarquia nasce correta na ferramenta,
/// sem etapa posterior de "religar" itens. Se uma criação falhar, interrompe e informa o que JÁ foi criado
/// (<see cref="BacklogCreationException.CreatedItems"/>), para que o PO saiba exatamente o estado parcial.
/// </summary>
public sealed class BacklogCreationService
{
    // Tag aplicada a todo card criado por esta automação — facilita filtrar/auditar (ou desfazer) um lote.
    public const string AutomationTag = "backlog-automation";
    public const string AiSuggestedEstimateTag = "estimativa-sugerida-ia";

    private readonly IWorkItemTrackerProvider _provider;

    public BacklogCreationService(IWorkItemTrackerProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<BacklogCreationResult> CreateAsync(
        BacklogPlan plan,
        IProgress<CreatedWorkItem>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var created = new List<CreatedWorkItem>();
        await _provider.PrepareAsync(cancellationToken);

        async Task<CreatedWorkItem> CreateOneAsync(WorkItemDraft draft, CreatedWorkItem? parent, string? epicTitle, string? featureTitle)
        {
            var request = BuildRequest(draft, parent, epicTitle, featureTitle);
            try
            {
                var item = await _provider.CreateAsync(request, cancellationToken);
                created.Add(item);
                progress?.Report(item);
                return item;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new BacklogCreationException(
                    $"Falha ao criar {draft.Kind.DisplayName()} \"{draft.Title}\" no {_provider.Name}: {ex.Message}", created, ex);
            }
        }

        foreach (var epic in plan.Epics)
        {
            var createdEpic = await CreateOneAsync(epic, null, null, null);
            foreach (var feature in epic.Features)
            {
                var createdFeature = await CreateOneAsync(feature, createdEpic, epic.Title, null);
                foreach (var story in feature.UserStories)
                {
                    var createdStory = await CreateOneAsync(story, createdFeature, epic.Title, feature.Title);
                    foreach (var task in story.Tasks)
                    {
                        await CreateOneAsync(task, createdStory, epic.Title, feature.Title);
                    }

                    foreach (var bug in story.Bugs)
                    {
                        await CreateOneAsync(bug, createdStory, epic.Title, feature.Title);
                    }
                }
            }
        }

        return new BacklogCreationResult(created);
    }

    internal static WorkItemCreationRequest BuildRequest(WorkItemDraft draft, CreatedWorkItem? parent, string? epicTitle, string? featureTitle)
    {
        var tags = new List<string>(draft.Tags) { AutomationTag };
        if (draft.Estimate.IsAiSuggested)
        {
            tags.Add(AiSuggestedEstimateTag);
        }

        var estimate = new Estimate();
        estimate.MergeFrom(draft.Estimate);

        // Boa prática de Azure DevOps/Jira: uma task recém-criada tem todo o trabalho estimado ainda por fazer.
        if (draft.Kind is WorkItemKind.Task or WorkItemKind.Bug
            && estimate.OriginalEstimateHours is { } original && estimate.RemainingHours is null)
        {
            estimate.RemainingHours = Math.Max(0, original - (estimate.CompletedHours ?? 0));
        }

        return new WorkItemCreationRequest
        {
            Kind = draft.Kind,
            Title = draft.Title.Trim(),
            Description = draft.Description,
            Statement = (draft as UserStoryItem)?.Statement,
            AcceptanceCriteria = draft switch
            {
                UserStoryItem story => story.AcceptanceCriteria,
                FeatureItem feature => feature.AcceptanceCriteria,
                _ => []
            },
            ReproSteps = (draft as BugItem)?.ReproSteps ?? [],
            ExpectedResult = (draft as BugItem)?.ExpectedResult,
            ActualResult = (draft as BugItem)?.ActualResult,
            Activity = (draft as TaskItem)?.Activity,
            Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Priority = draft.Priority,
            Estimate = estimate,
            Parent = parent,
            EpicTitle = epicTitle,
            FeatureTitle = featureTitle
        };
    }
}

public sealed class BacklogCreationException(string message, IReadOnlyList<CreatedWorkItem> createdItems, Exception innerException)
    : Exception(message, innerException)
{
    public IReadOnlyList<CreatedWorkItem> CreatedItems { get; } = createdItems.ToList();
}
