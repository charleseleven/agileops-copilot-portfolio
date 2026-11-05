namespace GmudAutomation.Core.Client;

/// <summary>
/// Obtém tokens de acesso do Azure AD para um resource (ex: Azure Resource Manager), evitando que o
/// usuário precise gerar e colar um token manualmente a cada execução.
/// </summary>
public interface IAzureAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(string resource, CancellationToken cancellationToken = default);
}
