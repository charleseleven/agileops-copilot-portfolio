using System.Text.RegularExpressions;

namespace GmudAutomation.Core.Services;

/// <summary>
/// US 1.2 (Task 3): reconhece as tags padrão do time (API, WEB, Database) em nomes de projeto
/// mencionados no texto de um comentário, seja no formato qualificado (ex: "Contoso.PortalCliente.API")
/// seja na forma curta usada informalmente pelos desenvolvedores (ex: "Alterações (WEB)", "Alterações WEB:"),
/// que é resolvida para o nome canônico do repositório via <see cref="IRepositoryCatalog"/>.
/// </summary>
public sealed partial class ProjectTagExtractor : IProjectTagExtractor
{
    [GeneratedRegex(@"\b[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*\.(?:API|WEB|Database)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FullyQualifiedProjectNameRegex();

    // Não deve casar o sufixo de um nome já qualificado (ex: o "WEB" dentro de "Contoso.PortalCliente.WEB",
    // já coberto pelo regex acima) — por isso a negative lookbehind para o ponto que precede o sufixo.
    [GeneratedRegex(@"(?<!\.)\b(API|WEB|Database)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BareProjectTagRegex();

    private readonly IRepositoryCatalog _repositoryCatalog;

    public ProjectTagExtractor() : this(RepositoryCatalog.CreateExample())
    {
    }

    public ProjectTagExtractor(IRepositoryCatalog repositoryCatalog)
    {
        _repositoryCatalog = repositoryCatalog ?? throw new ArgumentNullException(nameof(repositoryCatalog));
    }

    public IReadOnlyList<string> ExtractProjectTags(string? commentText)
    {
        if (string.IsNullOrWhiteSpace(commentText))
        {
            return Array.Empty<string>();
        }

        var tags = new List<string>();
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in FullyQualifiedProjectNameRegex().Matches(commentText))
        {
            if (seenProjects.Add(match.Value))
            {
                tags.Add(match.Value);
            }
        }

        foreach (Match match in BareProjectTagRegex().Matches(commentText))
        {
            if (!_repositoryCatalog.TryGetRepositoryNameByTag(match.Value, out var repositoryName))
            {
                continue;
            }

            if (seenProjects.Add(repositoryName))
            {
                tags.Add(repositoryName);
            }
        }

        return tags;
    }
}
