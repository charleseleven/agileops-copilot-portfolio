using System.Text.RegularExpressions;
using AgilePredict.Models.Flow;
using AgilePredict.Services.Interfaces;

namespace AgilePredict.Services
{
    /// <summary>
    /// Reconhece as duas formas de URL usadas pelo Azure DevOps para identificar organização e
    /// projeto: o domínio moderno ("dev.azure.com/{org}/{project}/...") e o legado por subdomínio
    /// ("{org}.visualstudio.com/{project}/..."), este último ainda em uso por organizações antigas.
    /// </summary>
    public class AzureDevOpsBoardsUrlParser : IAzureDevOpsBoardsUrlParser
    {
        private static readonly Regex VisualStudioHostPattern = new(
            @"^(?<org>[^.]+)\.visualstudio\.com$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public AzureDevOpsProjectReference Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("A URL do Azure Boards é obrigatória", nameof(url));
            }

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"URL inválida: '{url}'", nameof(url));
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString)
                .ToArray();

            var visualStudioMatch = VisualStudioHostPattern.Match(uri.Host);
            if (visualStudioMatch.Success)
            {
                // https://{org}.visualstudio.com/{project}/...
                if (segments.Length < 1)
                {
                    throw new ArgumentException($"Não foi possível extrair o projeto da URL: '{url}'", nameof(url));
                }

                return new AzureDevOpsProjectReference(visualStudioMatch.Groups["org"].Value, segments[0]);
            }

            if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                // https://dev.azure.com/{org}/{project}/...
                if (segments.Length < 2)
                {
                    throw new ArgumentException($"Não foi possível extrair organização e projeto da URL: '{url}'", nameof(url));
                }

                return new AzureDevOpsProjectReference(segments[0], segments[1]);
            }

            throw new ArgumentException(
                $"Host não reconhecido como Azure DevOps: '{uri.Host}'. Use uma URL no formato " +
                "'https://dev.azure.com/{organizacao}/{projeto}/...' ou 'https://{organizacao}.visualstudio.com/{projeto}/...'.",
                nameof(url));
        }

        public string? TryExtractIterationPath(string url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString)
                .ToArray();

            var sprintsIndex = Array.FindIndex(segments, s => s.Equals("_sprints", StringComparison.OrdinalIgnoreCase));
            // Layout: .../_sprints/{taskboard|backlog}/{time}/{...partes do iteration path}
            if (sprintsIndex < 0 || sprintsIndex + 3 >= segments.Length)
            {
                return null;
            }

            var view = segments[sprintsIndex + 1];
            if (!view.Equals("taskboard", StringComparison.OrdinalIgnoreCase) &&
                !view.Equals("backlog", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var iterationParts = segments[(sprintsIndex + 3)..];
            return iterationParts.Length > 0 ? string.Join('\\', iterationParts) : null;
        }
    }
}
