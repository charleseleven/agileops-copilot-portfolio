namespace GmudAutomation.Core.Models;

/// <summary>Representa a hierarquia completa de uma GMUD: a User Story principal e seus work items filhos.</summary>
public sealed class GmudRequest
{
    public required int Id { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<WorkItemInfo> ChildWorkItems { get; init; } = Array.Empty<WorkItemInfo>();
}
