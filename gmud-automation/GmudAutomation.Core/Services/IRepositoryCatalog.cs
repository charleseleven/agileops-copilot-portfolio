using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>
/// Mapeia os repositórios conhecidos de um cliente/organização para sua categoria (US 3.1) e para o nome
/// do Azure App Service correspondente (US 2.1). Os dados reais (nomes de repositório, padrões de nome
/// de site) nunca ficam no código-fonte — vêm de configuração local carregada em tempo de execução;
/// veja <see cref="RepositoryCatalog.LoadFromFile"/> e <see cref="RepositoryCatalog.CreateExample"/>.
/// </summary>
public interface IRepositoryCatalog
{
    bool TryGetKind(string repositoryName, out RepositoryKind kind);

    /// <summary>Retorna false para repositórios sem Azure App Service correspondente (ex: Database).</summary>
    bool TryGetSiteName(string repositoryName, string environmentSuffix, string clientSuffix, out string siteName);

    /// <summary>
    /// Resolve a tag curta usada pelo time nos comentários (ex: "API", "WEB", "Database", sem o prefixo
    /// do nome completo do repositório) para o nome canônico do repositório correspondente.
    /// </summary>
    bool TryGetRepositoryNameByTag(string tag, out string repositoryName);
}
