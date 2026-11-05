namespace GmudAutomation.Core.Services;

/// <summary>Extrai IDs de work items filhos a partir dos links presentes na descrição HTML de uma User Story.</summary>
public interface IWorkItemLinkParser
{
    IReadOnlyList<int> ExtractChildWorkItemIds(string? descriptionHtml);
}
