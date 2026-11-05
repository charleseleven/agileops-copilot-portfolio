using System.Text.RegularExpressions;
using GmudAutomation.Core.Pipeline.Configuration;

namespace GmudAutomation.Core.Pipeline.Analysis;

/// <summary>
/// Etapa 4: reconhece no texto dos cards os repositórios configurados, pelo nome completo
/// (ex: "Contoso.PortalCliente.WEB") ou por um alias (ex: "WEB", "Portal Web").
/// </summary>
public sealed class ProjectDetector
{
    private readonly IReadOnlyList<(RepositoryConfig Repository, Regex Pattern)> _patterns;

    public ProjectDetector(IReadOnlyList<RepositoryConfig> repositories)
    {
        ArgumentNullException.ThrowIfNull(repositories);

        _patterns = repositories
            .Select(r => (r, BuildPattern(r)))
            .ToList();
    }

    public IReadOnlyList<string> Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return _patterns
            .Where(p => p.Pattern.IsMatch(text))
            .Select(p => p.Repository.Name)
            .ToList();
    }

    private static Regex BuildPattern(RepositoryConfig repository)
    {
        var terms = new[] { repository.Name }
            .Concat(repository.Aliases ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => Regex.Escape(t.Trim()).Replace(@"\ ", @"\s+"));

        // O alias não pode ser o sufixo de outro nome qualificado (o "WEB" de "Outro.Produto.WEB") nem parte
        // de uma palavra ("WEBSITE") — mesma regra do ProjectTagExtractor (RN-GMUD-002).
        return new Regex($@"(?<![\w.])(?:{string.Join("|", terms)})(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
