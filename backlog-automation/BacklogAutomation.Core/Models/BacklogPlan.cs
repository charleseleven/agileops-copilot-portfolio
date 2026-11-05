using System.Text.Json.Serialization;

namespace BacklogAutomation.Core.Models;

/// <summary>
/// Backlog completo extraído de um documento ou de um prompt, já na hierarquia
/// Epic → Feature → User Story → Task/Bug. É o contrato único entre os parsers (Markdown determinístico,
/// IA ou JSON revisado pelo PO) e os providers que criam os cards (Azure DevOps, Jira, Trello) — por isso
/// pode ser salvo em JSON (comando <c>plan --out</c>), revisado e reutilizado em <c>create --plan</c>.
/// </summary>
public sealed class BacklogPlan
{
    public List<EpicItem> Epics { get; set; } = [];

    public IEnumerable<WorkItemDraft> EnumerateAll()
    {
        foreach (var epic in Epics)
        {
            yield return epic;
            foreach (var feature in epic.Features)
            {
                yield return feature;
                foreach (var story in feature.UserStories)
                {
                    yield return story;
                    foreach (var task in story.Tasks)
                    {
                        yield return task;
                    }

                    foreach (var bug in story.Bugs)
                    {
                        yield return bug;
                    }
                }
            }
        }
    }
}

/// <summary>Campos comuns a todo item de trabalho, independentemente do nível na hierarquia.</summary>
public abstract class WorkItemDraft
{
    [JsonPropertyOrder(-9)]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyOrder(-8)]
    public string? Description { get; set; }

    public List<string> Tags { get; set; } = [];

    /// <summary>Prioridade de 1 (mais alta) a 4 (mais baixa), na escala do Azure DevOps.</summary>
    public int? Priority { get; set; }

    public Estimate Estimate { get; set; } = new();

    [JsonPropertyOrder(-10)]
    public abstract WorkItemKind Kind { get; }
}

public sealed class EpicItem : WorkItemDraft
{
    public override WorkItemKind Kind => WorkItemKind.Epic;

    public List<FeatureItem> Features { get; set; } = [];
}

public sealed class FeatureItem : WorkItemDraft
{
    public override WorkItemKind Kind => WorkItemKind.Feature;

    public List<string> AcceptanceCriteria { get; set; } = [];

    public List<UserStoryItem> UserStories { get; set; } = [];
}

public sealed class UserStoryItem : WorkItemDraft
{
    public override WorkItemKind Kind => WorkItemKind.UserStory;

    /// <summary>Narrativa no formato "Como &lt;persona&gt;, quero &lt;ação&gt;, para que &lt;benefício&gt;".</summary>
    public string? Statement { get; set; }

    public List<string> AcceptanceCriteria { get; set; } = [];

    public List<TaskItem> Tasks { get; set; } = [];

    public List<BugItem> Bugs { get; set; } = [];
}

public sealed class TaskItem : WorkItemDraft
{
    public override WorkItemKind Kind => WorkItemKind.Task;

    /// <summary>Disciplina da task (ex: Development, Testing, Design, Documentation, Deployment, Requirements).</summary>
    public string? Activity { get; set; }
}

public sealed class BugItem : WorkItemDraft
{
    public override WorkItemKind Kind => WorkItemKind.Bug;

    public List<string> ReproSteps { get; set; } = [];

    public string? ExpectedResult { get; set; }

    public string? ActualResult { get; set; }
}
