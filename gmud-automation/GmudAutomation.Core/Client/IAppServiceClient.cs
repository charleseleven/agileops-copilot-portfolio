namespace GmudAutomation.Core.Client;

/// <summary>Abstrai a consulta à tag/versão em execução em um Azure App Service (Task 197).</summary>
public interface IAppServiceClient
{
    Task<string> GetCurrentTagAsync(string siteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Imagem completa configurada no site (<c>linuxFxVersion</c> sem o prefixo "DOCKER|"), ou null quando
    /// o App Service não existe na subscription — usado pela etapa 6 do fluxo de GMUD, que precisa
    /// distinguir "não encontrado" (listar candidatos) de falha de comunicação.
    /// </summary>
    Task<string?> GetContainerImageAsync(string siteName, CancellationToken cancellationToken = default);

    /// <summary>Nomes dos App Services cujo nome contém <paramref name="nameFragment"/> (sem diferenciar maiúsculas).</summary>
    Task<IReadOnlyList<string>> FindSitesAsync(string nameFragment, CancellationToken cancellationToken = default);
}
