using System.Text.Json;
using System.Text.Json.Serialization;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IRepositoryCatalog"/>
/// <remarks>
/// Os repositórios reais (nomes, categorias e padrões de nome de App Service) NUNCA ficam hardcoded
/// nesta classe — são recebidos como uma lista de <see cref="RepositoryCatalogEntry"/>, tipicamente
/// carregada de um arquivo de configuração local via <see cref="LoadFromFile"/>. Isso mantém o
/// código-fonte genérico e reutilizável para qualquer cliente/organização. Para uso rápido/demonstração
/// sem arquivo de configuração, veja <see cref="CreateExample"/>.
/// </remarks>
public sealed class RepositoryCatalog : IRepositoryCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<string, RepositoryCatalogEntry> _entriesByRepositoryName;
    private readonly Dictionary<string, string> _repositoryNameByTag;

    public RepositoryCatalog(IReadOnlyList<RepositoryCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _entriesByRepositoryName = entries.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
        _repositoryNameByTag = entries
            .Where(e => e.Tag is not null)
            .ToDictionary(e => e.Tag!, e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetKind(string repositoryName, out RepositoryKind kind)
    {
        if (_entriesByRepositoryName.TryGetValue(repositoryName, out var entry))
        {
            kind = entry.Kind;
            return true;
        }

        kind = default;
        return false;
    }

    public bool TryGetRepositoryNameByTag(string tag, out string repositoryName) =>
        _repositoryNameByTag.TryGetValue(tag, out repositoryName!);

    public bool TryGetSiteName(string repositoryName, string environmentSuffix, string clientSuffix, out string siteName)
    {
        siteName = string.Empty;

        if (!_entriesByRepositoryName.TryGetValue(repositoryName, out var entry))
        {
            return false;
        }

        if (entry.FixedSiteName is not null)
        {
            siteName = entry.FixedSiteName;
            return true;
        }

        if (entry.SiteNameTemplate is not null)
        {
            siteName = entry.SiteNameTemplate
                .Replace("{ambiente}", environmentSuffix)
                .Replace("{cliente}", clientSuffix);
            return true;
        }

        // Sem site associado (ex: Database, que não roda em Azure App Service).
        return false;
    }

    /// <summary>
    /// Carrega o catálogo de um arquivo JSON local — a forma real de uso, apontando para um arquivo
    /// não versionado (gitignored) com os repositórios reais do cliente/organização.
    /// </summary>
    public static RepositoryCatalog LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var entries = JsonSerializer.Deserialize<List<RepositoryCatalogEntry>>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Catálogo de repositórios inválido em \"{path}\".");

        return new RepositoryCatalog(entries);
    }

    /// <summary>
    /// Catálogo de exemplo (dados fictícios) usado quando nenhum arquivo de configuração real está
    /// definido — mantém a ferramenta funcional (e os testes/demos determinísticos) sem depender de
    /// dados de um cliente real.
    /// </summary>
    public static RepositoryCatalog CreateExample() => new(
    [
        new RepositoryCatalogEntry(
            "Contoso.PortalCliente.API", RepositoryKind.Api,
            SiteNameTemplate: "contoso-portalcliente-api-{ambiente}-{cliente}", Tag: "API"),
        new RepositoryCatalogEntry(
            "Contoso.PortalCliente.WEB", RepositoryKind.Web,
            SiteNameTemplate: "contoso-portalcliente-web-{ambiente}-{cliente}", Tag: "WEB"),
        new RepositoryCatalogEntry(
            "Contoso.Integracao.GerenciadorJobs", RepositoryKind.Backend,
            FixedSiteName: "contoso-integracao-gerenciadorjobs-prd-01"),
        new RepositoryCatalogEntry(
            "Contoso.Legado.Database", RepositoryKind.Database, Tag: "Database")
    ]);
}
