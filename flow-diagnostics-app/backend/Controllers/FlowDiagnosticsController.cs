using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.Flow;
using AgilePredict.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgilePredict.Controllers
{
    /// <summary>
    /// Expõe o diagnóstico preditivo de gargalos de fluxo (US #211) para o painel executivo do
    /// Scrum Master (Task #215). Funciona para qualquer organização/projeto do Azure DevOps ao
    /// qual o Scrum Master tenha acesso — não fica travado em uma única organização configurada
    /// no servidor.
    /// </summary>
    [ApiController]
    [Route("api/flow-diagnostics")]
    [Produces("application/json")]
    public class FlowDiagnosticsController : ControllerBase
    {
        private readonly IFlowDiagnosticsOrchestrator _orchestrator;
        private readonly IAzureDevOpsBoardsUrlParser _urlParser;
        private readonly AzureDevOpsConfiguration _defaultConfiguration;
        private readonly ILogger<FlowDiagnosticsController> _logger;

        public FlowDiagnosticsController(
            IFlowDiagnosticsOrchestrator orchestrator,
            IAzureDevOpsBoardsUrlParser urlParser,
            IOptions<AzureDevOpsConfiguration> defaultConfiguration,
            ILogger<FlowDiagnosticsController> logger)
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _urlParser = urlParser ?? throw new ArgumentNullException(nameof(urlParser));
            _defaultConfiguration = defaultConfiguration?.Value ?? throw new ArgumentNullException(nameof(defaultConfiguration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gera o relatório de diagnóstico preditivo (Cycle Time vs. baseline, alertas de gargalo
        /// por status/responsável e sugestões de mitigação geradas por IA) para a organização,
        /// projeto e sprint informados. Aceita POST (em vez de GET) porque a requisição pode
        /// carregar um Personal Access Token, que nunca deve trafegar em query string/logs.
        /// </summary>
        /// <response code="200">Relatório gerado com sucesso.</response>
        /// <response code="400">Requisição inválida: faltou iteração, organização/projeto/PAT não puderam ser resolvidos, ou a URL informada não é do Azure DevOps.</response>
        /// <response code="502">Falha ao coletar dados na API do Azure DevOps (organização/projeto/PAT incorretos, ou item não encontrado).</response>
        [HttpPost]
        [ProducesResponseType(typeof(FlowDiagnosticReport), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<FlowDiagnosticReport>> GetDiagnostics(
            [FromBody] FlowDiagnosticsRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            AzureDevOpsProjectReference projectReference;
            try
            {
                projectReference = ResolveProjectReference(request);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            var personalAccessToken = request.PersonalAccessToken ?? ResolveDefaultPersonalAccessToken(projectReference.Organization);
            if (string.IsNullOrWhiteSpace(personalAccessToken))
            {
                return BadRequest(new
                {
                    message = "Informe um Personal Access Token com acesso a essa organização (campo personalAccessToken)."
                });
            }

            var iterationPath = ResolveIterationPath(request.IterationPath, out var iterationPathError);
            if (iterationPathError is not null)
            {
                return BadRequest(new { message = iterationPathError });
            }

            var connection = new AzureDevOpsConnection(projectReference.Organization, projectReference.Project, personalAccessToken);

            try
            {
                var report = await _orchestrator.GenerateReportAsync(connection, iterationPath!, cancellationToken);
                return Ok(report);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "Falha ao coletar dados de Work Items para {Organization}/{Project}, iteração {IterationPath}",
                    connection.Organization, connection.Project, iterationPath);

                var reason = ex.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized =>
                        "Personal Access Token inválido ou expirado.",
                    System.Net.HttpStatusCode.Forbidden =>
                        "Personal Access Token sem permissão de leitura de Work Items (escopo 'Work Items (Read)').",
                    System.Net.HttpStatusCode.NotFound =>
                        "Organização ou projeto não encontrados — confira a URL do Azure Boards (ou organização/projeto) informados.",
                    System.Net.HttpStatusCode.BadRequest =>
                        "Requisição rejeitada pelo Azure DevOps — o caminho da iteração pode estar incorreto (deve ser o valor exato do campo Iteration Path, ex.: 'Projeto\\Sprint 1', e não uma URL).",
                    _ => "Falha ao comunicar com a API do Azure DevOps."
                };

                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    message = reason,
                    detail = ex.Message
                });
            }
        }

        /// <summary>
        /// Tolera o erro comum de colar a URL da sprint (taskboard/backlog) no campo de Iteration
        /// Path: nesse caso, extrai o caminho automaticamente em vez de enviar a URL literal para
        /// o Azure DevOps (o que resultaria em uma consulta WIQL malformada).
        /// </summary>
        private string? ResolveIterationPath(string iterationPath, out string? error)
        {
            error = null;

            if (!iterationPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !iterationPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return iterationPath;
            }

            var extracted = _urlParser.TryExtractIterationPath(iterationPath);
            if (extracted is not null)
            {
                return extracted;
            }

            error = "O campo 'Caminho da iteração' recebeu uma URL, mas não foi possível extrair a sprint dela. " +
                    "Informe o valor exato do Iteration Path (ex.: 'Projeto\\Sprint 1'), sem 'https://'.";
            return null;
        }

        /// <summary>
        /// Resolve o PAT padrão do servidor para a organização informada: primeiro tenta um PAT
        /// específico daquela organização (<see cref="AzureDevOpsConfiguration.OrganizationTokens"/>),
        /// e só cai no PAT único (<see cref="AzureDevOpsConfiguration.PersonalAccessToken"/>) quando a
        /// organização resolvida é exatamente a <see cref="AzureDevOpsConfiguration.Organization"/>
        /// configurada — nunca reaproveita esse PAT para uma organização diferente da sua.
        /// </summary>
        private string? ResolveDefaultPersonalAccessToken(string organization)
        {
            if (_defaultConfiguration.OrganizationTokens.TryGetValue(organization, out var token) &&
                !string.IsNullOrWhiteSpace(token))
            {
                return token;
            }

            if (!string.IsNullOrWhiteSpace(_defaultConfiguration.Organization) &&
                string.Equals(_defaultConfiguration.Organization, organization, StringComparison.OrdinalIgnoreCase))
            {
                return _defaultConfiguration.PersonalAccessToken;
            }

            return null;
        }

        private AzureDevOpsProjectReference ResolveProjectReference(FlowDiagnosticsRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.BoardsUrl))
            {
                return _urlParser.Parse(request.BoardsUrl);
            }

            var organization = request.Organization ?? _defaultConfiguration.Organization;
            var project = request.Project ?? _defaultConfiguration.Project;

            if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(project))
            {
                throw new ArgumentException(
                    "Informe a URL do Azure Boards (campo boardsUrl) ou a organização e o projeto explicitamente " +
                    "(campos organization e project).");
            }

            return new AzureDevOpsProjectReference(organization, project);
        }
    }
}
