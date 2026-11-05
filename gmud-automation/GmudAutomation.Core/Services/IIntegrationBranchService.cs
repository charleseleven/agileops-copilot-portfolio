namespace GmudAutomation.Core.Services;

/// <summary>
/// US 2.1: cria a branch de integração a partir da versão em execução no Azure App Service e mescla
/// sequencialmente as branches de feature identificadas.
/// </summary>
public interface IIntegrationBranchService
{
    Task<string> CreateReleaseBranchAsync(Models.ReleaseBranchRequest request, CancellationToken cancellationToken = default);
}
