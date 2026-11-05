namespace AgilePredict.Models.Flow
{
    /// <summary>
    /// Organização e projeto do Azure DevOps extraídos de uma URL de Boards colada pelo usuário.
    /// </summary>
    public record AzureDevOpsProjectReference(string Organization, string Project);
}
