namespace AgilePredict.Models.Flow
{
    /// <summary>
    /// Dados de conexão com uma organização/projeto do Azure DevOps, resolvidos por requisição.
    /// Permite que o mesmo backend atenda a qualquer organização à qual o Scrum Master tenha
    /// acesso (ex.: "contoso", "eleven11C", ou outra), em vez de travar em uma única organização
    /// fixa na configuração do servidor.
    /// </summary>
    public record AzureDevOpsConnection(string Organization, string Project, string PersonalAccessToken);
}
