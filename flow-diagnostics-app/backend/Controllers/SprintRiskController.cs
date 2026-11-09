using System.Net;
using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Services;
using AgilePredict.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AgilePredict.Controllers
{
    /// <summary>
    /// Expõe o relatório formatado do Sprint Risk Bot (PBI 8.1) para o dashboard executivo (PBI 8.2, Task 2).
    /// </summary>
    [ApiController]
    [Route("api/sprint-risk")]
    [Produces("application/json")]
    public sealed class SprintRiskController : ControllerBase
    {
        private readonly ISprintRiskBot _bot;
        private readonly IAzureDevOpsConnectionResolver _resolver;
        private readonly ILogger<SprintRiskController> _logger;

        public SprintRiskController(ISprintRiskBot bot, IAzureDevOpsConnectionResolver resolver, ILogger<SprintRiskController> logger)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Prevê o risco de atraso da sprint: cruza as Horas Estimadas entregues com a capacidade diária
        /// restante do time, aponta gargalos (capacidade, ritmo, sobrecarga por membro, itens sem dono ou
        /// sem estimativa) e traz o parecer da IA. POST porque a requisição pode levar um PAT.
        /// </summary>
        /// <response code="200">Relatório gerado.</response>
        /// <response code="400">Organização/projeto/PAT não resolvidos ou sprint inválida.</response>
        /// <response code="404">Time sem sprint atual, ou sprint/time não encontrados.</response>
        /// <response code="502">Falha ao consultar o Azure DevOps.</response>
        [HttpPost]
        [ProducesResponseType(typeof(SprintRiskReport), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<SprintRiskReport>> GetReport([FromBody] SprintRiskRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            Models.Flow.AzureDevOpsConnection connection;
            string? team;
            string? iterationPath;
            try
            {
                connection = _resolver.Resolve(request.BoardsUrl, request.Organization, request.Project, request.PersonalAccessToken);
                // Link do taskboard/backlog da sprint já traz time e sprint: aproveita quando os campos estão vazios.
                team = _resolver.ResolveTeam(request.BoardsUrl, request.Team);
                iterationPath = _resolver.ResolveIterationPath(request.BoardsUrl, request.IterationPath);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            try
            {
                return Ok(await _bot.GenerateReportAsync(connection, team, iterationPath, request.IncludeAiInsight, cancellationToken));
            }
            catch (SprintNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Falha ao coletar a sprint de {Organization}/{Project}", connection.Organization, connection.Project);
                var reason = ex.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Personal Access Token inválido ou expirado.",
                    HttpStatusCode.Forbidden => "Personal Access Token sem permissão de leitura (escopos 'Work Items (Read)' e 'Project and Team (Read)').",
                    HttpStatusCode.NotFound => "Organização ou projeto não encontrados; confira a URL do Azure Boards.",
                    _ => "Falha ao comunicar com a API do Azure DevOps."
                };
                return StatusCode(StatusCodes.Status502BadGateway, new { message = reason });
            }
        }
    }
}
