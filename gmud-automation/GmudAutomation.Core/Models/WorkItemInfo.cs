namespace GmudAutomation.Core.Models;

/// <summary>Metadados de um Work Item (User Story, Task ou Bug) do Azure Boards.</summary>
public sealed record WorkItemInfo(
    int Id,
    string Title,
    string WorkItemType,
    string State,
    string? DescriptionHtml,
    string Url);
