using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>
/// Fecha o ciclo da US 1.2: roda a identificação de impacto técnico (projetos + validação de branch)
/// e grava o resultado como comentário no card do work item, atualizando-o com as informações apuradas.
/// </summary>
public sealed class GmudTechnicalImpactPublisher : IGmudTechnicalImpactPublisher
{
    private readonly ITechnicalImpactService _technicalImpactService;
    private readonly ITechnicalImpactReportFormatter _formatter;
    private readonly IAzureDevOpsClient _client;

    public GmudTechnicalImpactPublisher(
        ITechnicalImpactService technicalImpactService,
        ITechnicalImpactReportFormatter formatter,
        IAzureDevOpsClient client)
    {
        _technicalImpactService = technicalImpactService ?? throw new ArgumentNullException(nameof(technicalImpactService));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<IReadOnlyList<TechnicalImpact>> AnalyzeAndPublishAsync(int workItemId, CancellationToken cancellationToken = default)
    {
        var impacts = await _technicalImpactService.IdentifyTechnicalImpactAsync(workItemId, cancellationToken);
        var comment = _formatter.FormatComment(impacts);
        await _client.AddWorkItemCommentAsync(workItemId, comment, cancellationToken);

        return impacts;
    }
}
