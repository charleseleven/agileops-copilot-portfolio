using System.Net;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 1 — recebe o link/ID da GMUD, confirma que o item existe na organização configurada e extrai do
/// título a data (DD-MM-AAAA → DDMMAAAA) e o cliente. Título fora do padrão interrompe o fluxo aqui.
/// </summary>
public sealed class ResolveGmudStep : IGmudStep
{
    private readonly IAzureDevOpsClient _boards;
    private readonly GmudConfig _config;
    private readonly GmudNamingConventions _naming;
    private readonly IAzureBoardsUrlParser _urlParser;

    public ResolveGmudStep(IAzureDevOpsClient boards, GmudConfig config, GmudNamingConventions naming, IAzureBoardsUrlParser urlParser)
    {
        _boards = boards ?? throw new ArgumentNullException(nameof(boards));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _urlParser = urlParser ?? throw new ArgumentNullException(nameof(urlParser));
    }

    public string Key => GmudStepKeys.ResolveGmud;
    public string Name => "Solicitar o link da GMUD";
    public bool WritesRemote => false;
    public bool SupportsDryRun => true;

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        if (!GmudPipelineRunner.TryParseInput(context.Input, _urlParser, out var gmudId, out var reference))
        {
            throw new GmudValidationException("link",
                $"\"{context.Input}\" não é um ID nem um link de work item (formato: https://dev.azure.com/<org>/<projeto>/_workitems/edit/<id>).");
        }

        if (reference is not null && !string.Equals(reference.Organization, _config.Organization, StringComparison.OrdinalIgnoreCase))
        {
            throw new GmudValidationException("organizacao",
                $"O link aponta para a organização \"{reference.Organization}\", mas o gmud.config.json está configurado para \"{_config.Organization}\".");
        }

        if (manifest.Gmud.Id != 0 && manifest.Gmud.Id != gmudId)
        {
            throw new GmudValidationException("id", $"O manifesto pertence à GMUD #{manifest.Gmud.Id}, não à #{gmudId}.");
        }

        Models.WorkItemInfo workItem;
        try
        {
            workItem = await _boards.GetWorkItemAsync(gmudId, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new GmudValidationException("id", $"O work item #{gmudId} não existe na organização \"{_config.Organization}\" (projeto \"{_config.BoardsProject}\").");
        }

        var title = _naming.ParseTitle(workItem.Title);
        var client = _naming.ResolveClient(title.ClientTitleName);

        manifest.Gmud = new GmudHeader
        {
            Id = gmudId,
            Title = workItem.Title,
            Date = GmudNamingConventions.FormatDate(title.Date),
            Client = client.ContainerName,
            ClientTitleName = client.TitleName,
            ClientAppServiceName = client.AppServiceName,
            ChangeType = title.ChangeType,
            Organization = _config.Organization,
            Project = reference?.Project ?? _config.BoardsProject,
            State = workItem.State
        };

        if (!client.IsConfigured)
        {
            manifest.AddWarning(
                $"Cliente \"{client.TitleName}\" não está em \"clients\" do gmud.config.json — usando \"{client.ContainerName}\" (container) e " +
                $"\"{client.AppServiceName}\" (App Service). Confira na etapa 6.");
        }

        context.Logger.Info($"GMUD #{gmudId} \"{workItem.Title}\" — data {manifest.Gmud.Date}, cliente {manifest.Gmud.Client}.", context.StepNumber);
        return manifest;
    }
}
