namespace GmudAutomation.Core.Client;

/// <summary>
/// Configuração de acesso à API REST do Azure DevOps para uma organização. Boards e Repos podem viver em
/// Team Projects diferentes dentro da mesma organização (ex: "contoso" separa "Contoso Projetos" de
/// "Contoso Repositorios") — por isso os dois projetos são configuráveis independentemente.
/// </summary>
public sealed class AzureDevOpsClientOptions
{
    public required string Organization { get; init; }

    /// <summary>Team Project onde vivem os Work Items (Boards).</summary>
    public required string BoardsProject { get; init; }

    /// <summary>Team Project onde vivem os repositórios Git e os pipelines. Se omitido, usa <see cref="BoardsProject"/>.</summary>
    public string? ReposProject { get; init; }

    /// <summary>Personal Access Token — deve vir de Secret Manager (dev) ou variável de ambiente (produção), nunca hardcoded.</summary>
    public required string PersonalAccessToken { get; init; }

    public string ApiVersion { get; init; } = "7.1";

    public string EffectiveReposProject => ReposProject ?? BoardsProject;
}
