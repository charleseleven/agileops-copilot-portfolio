namespace BacklogAutomation.Core.Models;

/// <summary>Nível do item na hierarquia Epic → Feature → User Story → Task/Bug.</summary>
public enum WorkItemKind
{
    Epic,
    Feature,
    UserStory,
    Task,
    Bug
}

public static class WorkItemKindExtensions
{
    public static string DisplayName(this WorkItemKind kind) => kind switch
    {
        WorkItemKind.Epic => "Epic",
        WorkItemKind.Feature => "Feature",
        WorkItemKind.UserStory => "User Story",
        WorkItemKind.Task => "Task",
        WorkItemKind.Bug => "Bug",
        _ => kind.ToString()
    };
}
