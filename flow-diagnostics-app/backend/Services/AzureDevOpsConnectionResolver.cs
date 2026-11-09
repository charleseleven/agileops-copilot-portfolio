using AgilePredict.Models.Configuration;
using AgilePredict.Models.Flow;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AgilePredict.Services
{
    /// <summary>
    /// Resolve organização, projeto e PAT de uma requisição: URL do Azure Boards colada, ou campos
    /// explícitos, com fallback para a configuração do servidor. O PAT padrão de uma organização
    /// nunca é reaproveitado para outra.
    /// </summary>
    public interface IAzureDevOpsConnectionResolver
    {
        /// <exception cref="ArgumentException">Dados insuficientes ou URL que não é do Azure DevOps.</exception>
        AzureDevOpsConnection Resolve(string? boardsUrl, string? organization, string? project, string? personalAccessToken);

        /// <summary>
        /// Aceita o Iteration Path ou a URL da sprint (extrai o caminho). Vazio = nulo (sprint atual).
        /// </summary>
        /// <exception cref="ArgumentException">URL da qual não foi possível extrair a sprint.</exception>
        string? NormalizeIterationPath(string? iterationPath);

        /// <summary>
        /// Time informado; se vazio, o time da URL colada (link de sprint/backlog/board do time).
        /// Nulo quando nenhum dos dois traz o time (o chamador usa o time padrão do projeto).
        /// </summary>
        string? ResolveTeam(string? boardsUrl, string? team);

        /// <summary>
        /// Sprint informada (Iteration Path ou URL); se vazia, a sprint da URL colada no campo do
        /// Azure Boards. Nulo = sprint atual do time.
        /// </summary>
        /// <exception cref="ArgumentException">URL no campo da sprint da qual não foi possível extrair a sprint.</exception>
        string? ResolveIterationPath(string? boardsUrl, string? iterationPath);
    }

    public sealed class AzureDevOpsConnectionResolver : IAzureDevOpsConnectionResolver
    {
        private readonly IAzureDevOpsBoardsUrlParser _urlParser;
        private readonly AzureDevOpsConfiguration _defaults;

        public AzureDevOpsConnectionResolver(IAzureDevOpsBoardsUrlParser urlParser, IOptions<AzureDevOpsConfiguration> defaults)
        {
            _urlParser = urlParser ?? throw new ArgumentNullException(nameof(urlParser));
            _defaults = defaults?.Value ?? throw new ArgumentNullException(nameof(defaults));
        }

        public AzureDevOpsConnection Resolve(string? boardsUrl, string? organization, string? project, string? personalAccessToken)
        {
            AzureDevOpsProjectReference reference;
            if (!string.IsNullOrWhiteSpace(boardsUrl))
            {
                reference = _urlParser.Parse(boardsUrl);
            }
            else
            {
                var org = string.IsNullOrWhiteSpace(organization) ? _defaults.Organization : organization;
                var proj = string.IsNullOrWhiteSpace(project) ? _defaults.Project : project;
                if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(proj))
                {
                    throw new ArgumentException(
                        "Informe a URL do Azure Boards (campo boardsUrl) ou a organização e o projeto (campos organization e project).");
                }
                reference = new AzureDevOpsProjectReference(org, proj);
            }

            var pat = string.IsNullOrWhiteSpace(personalAccessToken) ? DefaultToken(reference.Organization) : personalAccessToken;
            if (string.IsNullOrWhiteSpace(pat))
            {
                throw new ArgumentException("Informe um Personal Access Token com acesso a essa organização (campo personalAccessToken).");
            }

            return new AzureDevOpsConnection(reference.Organization, reference.Project, pat);
        }

        public string? NormalizeIterationPath(string? iterationPath)
        {
            if (string.IsNullOrWhiteSpace(iterationPath)) return null;
            var value = iterationPath.Trim();
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return _urlParser.TryExtractIterationPath(value) ?? throw new ArgumentException(
                "O campo da sprint recebeu uma URL, mas não foi possível extrair a sprint dela. Informe o Iteration Path (ex.: 'Projeto\\Sprint 1').");
        }

        public string? ResolveTeam(string? boardsUrl, string? team)
        {
            if (!string.IsNullOrWhiteSpace(team)) return team.Trim();
            return string.IsNullOrWhiteSpace(boardsUrl) ? null : _urlParser.TryExtractTeam(boardsUrl);
        }

        public string? ResolveIterationPath(string? boardsUrl, string? iterationPath)
        {
            var explicitPath = NormalizeIterationPath(iterationPath);
            if (explicitPath is not null) return explicitPath;
            return string.IsNullOrWhiteSpace(boardsUrl) ? null : _urlParser.TryExtractIterationPath(boardsUrl);
        }

        private string? DefaultToken(string organization)
        {
            if (_defaults.OrganizationTokens.TryGetValue(organization, out var token) && !string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            return string.Equals(_defaults.Organization, organization, StringComparison.OrdinalIgnoreCase)
                ? _defaults.PersonalAccessToken
                : null;
        }
    }
}
