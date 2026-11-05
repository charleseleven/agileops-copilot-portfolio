namespace GmudAutomation.Core.Services;

/// <summary>Extrai organização, projeto e ID do work item a partir de uma URL de item do Azure Boards.</summary>
public interface IAzureBoardsUrlParser
{
    bool TryParse(string url, out AzureBoardsWorkItemReference reference);
}

/// <summary>Referência resolvida de um work item a partir de sua URL no Azure Boards.</summary>
public sealed record AzureBoardsWorkItemReference(string Organization, string Project, int WorkItemId);
