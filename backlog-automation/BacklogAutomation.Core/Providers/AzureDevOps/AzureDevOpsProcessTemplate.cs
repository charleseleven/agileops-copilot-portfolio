using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Providers.AzureDevOps;

public enum AzureDevOpsProcess
{
    Agile,
    Scrum,
    Cmmi,
    Basic
}

/// <summary>
/// Tipos de item e campos de estimativa de cada processo padrão do Azure Boards (e dos processos herdados
/// deles). O processo Basic só tem 3 níveis (Epic → Issue → Task) e nenhum tipo Bug: a Feature vira um
/// Epic filho com a tag "Feature" e o Bug vira uma Task com a tag "Bug", preservando a hierarquia.
/// </summary>
public sealed record AzureDevOpsProcessTemplate(
    AzureDevOpsProcess Process,
    string EpicType,
    string FeatureType,
    string StoryType,
    string TaskType,
    string BugType,
    string StoryPointsField,
    string BugPointsField)
{
    public const string EffortField = "Microsoft.VSTS.Scheduling.Effort";
    public const string StoryPointsReferenceName = "Microsoft.VSTS.Scheduling.StoryPoints";
    public const string SizeField = "Microsoft.VSTS.Scheduling.Size";
    public const string OriginalEstimateField = "Microsoft.VSTS.Scheduling.OriginalEstimate";
    public const string RemainingWorkField = "Microsoft.VSTS.Scheduling.RemainingWork";
    public const string CompletedWorkField = "Microsoft.VSTS.Scheduling.CompletedWork";

    public static AzureDevOpsProcessTemplate For(AzureDevOpsProcess process) => process switch
    {
        AzureDevOpsProcess.Agile => new(process, "Epic", "Feature", "User Story", "Task", "Bug", StoryPointsReferenceName, StoryPointsReferenceName),
        AzureDevOpsProcess.Scrum => new(process, "Epic", "Feature", "Product Backlog Item", "Task", "Bug", EffortField, EffortField),
        AzureDevOpsProcess.Cmmi => new(process, "Epic", "Feature", "Requirement", "Task", "Bug", SizeField, SizeField),
        AzureDevOpsProcess.Basic => new(process, "Epic", "Epic", "Issue", "Task", "Task", EffortField, EffortField),
        _ => throw new ArgumentOutOfRangeException(nameof(process), process, null)
    };

    /// <summary>Identifica o processo pelos tipos de item existentes no projeto (funciona também para processos herdados/customizados).</summary>
    public static AzureDevOpsProcess? Detect(IReadOnlyCollection<string> workItemTypeNames)
    {
        bool Has(string name) => workItemTypeNames.Contains(name, StringComparer.OrdinalIgnoreCase);

        if (Has("Product Backlog Item"))
        {
            return AzureDevOpsProcess.Scrum;
        }

        if (Has("Requirement"))
        {
            return AzureDevOpsProcess.Cmmi;
        }

        if (Has("User Story"))
        {
            return AzureDevOpsProcess.Agile;
        }

        if (Has("Issue"))
        {
            return AzureDevOpsProcess.Basic;
        }

        return null;
    }

    public string TypeFor(WorkItemKind kind) => kind switch
    {
        WorkItemKind.Epic => EpicType,
        WorkItemKind.Feature => FeatureType,
        WorkItemKind.UserStory => StoryType,
        WorkItemKind.Task => TaskType,
        _ => BugType
    };

    /// <summary>Tags extras quando o nível não tem tipo próprio no processo (Basic).</summary>
    public IReadOnlyList<string> ExtraTagsFor(WorkItemKind kind) => kind switch
    {
        WorkItemKind.Feature when FeatureType == EpicType => ["Feature"],
        WorkItemKind.Bug when BugType == TaskType => ["Bug"],
        _ => []
    };

    /// <summary>Campo de story points/esforço para o nível (null = o nível não recebe pontos, ex: Task).</summary>
    public string? PointsFieldFor(WorkItemKind kind) => kind switch
    {
        WorkItemKind.Epic or WorkItemKind.Feature => EffortField,
        WorkItemKind.UserStory => StoryPointsField,
        WorkItemKind.Bug => BugPointsField,
        _ => null
    };
}
