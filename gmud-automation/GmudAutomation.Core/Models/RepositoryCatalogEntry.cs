namespace GmudAutomation.Core.Models;

/// <summary>
/// Uma entrada do catálogo de repositórios: mapeia um nome de repositório real do Azure Repos para sua
/// categoria (<see cref="Kind"/>) e para o padrão de nome do Azure App Service correspondente.
/// Carregada de configuração local (arquivo JSON não versionado) via <see cref="Services.RepositoryCatalog.LoadFromFile"/> —
/// nunca hardcoded no código-fonte, para manter o repositório genérico e reutilizável para qualquer cliente/organização.
/// </summary>
/// <param name="Name">Nome do repositório no Azure Repos (ex: "Cliente.PortalCliente.API").</param>
/// <param name="Kind">Categoria do repositório, usada para decidir o fluxo de PR/branch de release.</param>
/// <param name="SiteNameTemplate">
/// Padrão do nome do Azure App Service, com os placeholders literais "{ambiente}" e "{cliente}"
/// (ex: "cliente-portalcliente-api-{ambiente}-{cliente}"). Usado quando o site varia por ambiente/cliente.
/// </param>
/// <param name="FixedSiteName">
/// Nome fixo do Azure App Service, para repositórios com uma única instância conhecida independente de
/// ambiente/cliente (ex: um serviço de backend só com instância de produção). Tem prioridade sobre <see cref="SiteNameTemplate"/>.
/// </param>
/// <param name="Tag">
/// Tag curta opcional (ex: "API", "WEB", "Database") usada para reconhecer o repositório quando citado
/// de forma abreviada em comentários do Azure Boards, sem o nome completo do repositório.
/// </param>
public sealed record RepositoryCatalogEntry(
    string Name,
    RepositoryKind Kind,
    string? SiteNameTemplate = null,
    string? FixedSiteName = null,
    string? Tag = null);
