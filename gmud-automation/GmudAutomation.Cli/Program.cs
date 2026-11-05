using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Services;

// Fluxo automatizado de preparação de GMUD (gmud run/status/report): configurado por gmud.config.json,
// com manifesto próprio — não depende das opções e variáveis dos comandos da Feature 1 a 3 abaixo.
if (args.Length > 0 && GmudPipelineCommands.Handles(args[0]))
{
    return await GmudPipelineCommands.RunAsync(args);
}

// Organização "conhecida" (aquela para a qual este processo foi desenhado) e o Team Project de
// Repos/Pipelines correspondente (quando Boards e Repos vivem em Team Projects diferentes dentro da
// mesma organização) NUNCA ficam hardcoded no código-fonte — vêm de configuração local (variáveis de
// ambiente), para que o repositório publicado seja genérico e reutilizável para qualquer organização
// do Azure DevOps. Configure GMUD_KNOWN_ORGANIZATION / GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT localmente
// (ex: em .mcp/.env) para o uso real contra uma organização específica.
var knownOrganization = Environment.GetEnvironmentVariable("GMUD_KNOWN_ORGANIZATION");
var knownOrganizationReposProject = Environment.GetEnvironmentVariable("GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT");

// A branch de release SEMPRE parte do que está publicado em produção (baseline estável e já validada),
// independentemente do ambiente de destino (--ambiente gmud|prd) — nunca do que já está no ambiente-alvo,
// que pode estar desatualizado ou conter uma versão intermediária ainda não promovida.
const string ProductionSiteSuffix = "prd";
const string GmudSiteSuffix = "gmud";

// Se a última build da branch atualmente publicada no ambiente GMUD do cliente tiver menos de 14 dias,
// ela é mesclada na branch de release recém-criada também — para não perder, na nova release, o que já
// foi validado recentemente em GMUD e ainda não foi promovido para produção.
const int GmudCatchUpWindowDays = 14;

var urlParser = new AzureBoardsUrlParser();
var options = CliOptions.Parse(args, urlParser);
if (options is null)
{
    PrintUsage();
    return 1;
}

var isKnownOrganization = knownOrganization is not null
    && string.Equals(options.Organization, knownOrganization, StringComparison.OrdinalIgnoreCase);

var reposProject = options.ReposProject
    ?? (isKnownOrganization && knownOrganizationReposProject is not null ? knownOrganizationReposProject : options.BoardsProject);

if (isKnownOrganization)
{
    Console.WriteLine(
        $"Organização \"{options.Organization}\" reconhecida (configuração local) — executando o processo de Automação e Orquestração do Processo de GMUD.");
}
else
{
    Console.WriteLine($"Executando para a organização informada: \"{options.Organization}\" (projeto \"{options.BoardsProject}\").");
}

if (!string.Equals(reposProject, options.BoardsProject, StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine($"Boards: \"{options.BoardsProject}\" | Repos/Pipelines: \"{reposProject}\".");
}

var patEnvironmentVariable = ResolvePatEnvironmentVariableName(options.Organization);
var personalAccessToken = Environment.GetEnvironmentVariable(patEnvironmentVariable);
if (string.IsNullOrWhiteSpace(personalAccessToken))
{
    Console.Error.WriteLine($"PAT não configurado. Defina a variável de ambiente {patEnvironmentVariable}.");
    return 1;
}

using var httpClient = new HttpClient();
var client = new AzureDevOpsClient(httpClient, new AzureDevOpsClientOptions
{
    Organization = options.Organization,
    BoardsProject = options.BoardsProject,
    ReposProject = reposProject,
    PersonalAccessToken = personalAccessToken
});

// O catálogo de repositórios reais (nomes, categorias, padrões de App Service) vem de um arquivo local
// apontado por GMUD_REPOSITORY_CATALOG_PATH (nunca commitado com dados reais de cliente). Sem essa
// variável, cai no catálogo de exemplo (dados fictícios) — a ferramenta continua funcional para
// demonstração, só não reconhece repositórios reais.
var repositoryCatalogPath = Environment.GetEnvironmentVariable("GMUD_REPOSITORY_CATALOG_PATH");
var repositoryCatalog = string.IsNullOrWhiteSpace(repositoryCatalogPath)
    ? RepositoryCatalog.CreateExample()
    : RepositoryCatalog.LoadFromFile(repositoryCatalogPath);

var linkParser = new WorkItemLinkParser();
var hierarchyService = new GmudHierarchyService(client, linkParser);
var tagExtractor = new ProjectTagExtractor(repositoryCatalog);
var technicalImpactService = new TechnicalImpactService(client, tagExtractor);
var formatter = new TechnicalImpactReportFormatter();
var publisher = new GmudTechnicalImpactPublisher(technicalImpactService, formatter, client);
var orchestrator = new GmudAutomationOrchestrator(hierarchyService, publisher);
var releaseBranchNameBuilder = new ReleaseBranchNameBuilder();
using var appServiceHttpClient = new HttpClient();
var appServiceClient = new AppServiceClient(
    appServiceHttpClient,
    new AppServiceClientOptions { SubscriptionId = options.SubscriptionId },
    new AzureCliAccessTokenProvider());
var integrationBranchService = new IntegrationBranchService(appServiceClient, client, releaseBranchNameBuilder);
var pullRequestOrchestrationService = new PullRequestOrchestrationService(client);

try
{
    switch (options.Command)
    {
        case "hierarchy":
        {
            var gmudRequest = await hierarchyService.BuildHierarchyAsync(options.WorkItemId);
            PrintHierarchy(gmudRequest);
            return 0;
        }

        case "impact":
        {
            var impacts = await technicalImpactService.IdentifyTechnicalImpactAsync(options.WorkItemId);
            Console.WriteLine(formatter.FormatComment(impacts));
            Console.WriteLine();
            Console.WriteLine("(somente leitura — nenhum comentário foi publicado no card; use 'publish-impact' para isso)");
            return 0;
        }

        case "add-comment":
        {
            if (!options.ConfirmedWrite)
            {
                return RequireConfirmWrite();
            }

            if (string.IsNullOrWhiteSpace(options.CommentText))
            {
                Console.Error.WriteLine("Informe o texto do comentário com --text \"...\".");
                return 1;
            }

            await client.AddWorkItemCommentAsync(options.WorkItemId, options.CommentText);
            Console.WriteLine($"Comentário de teste publicado no work item #{options.WorkItemId}.");
            return 0;
        }

        case "publish-impact":
        {
            if (!options.ConfirmedWrite)
            {
                return RequireConfirmWrite();
            }

            var impacts = await publisher.AnalyzeAndPublishAsync(options.WorkItemId);
            Console.WriteLine($"Comentário publicado no work item #{options.WorkItemId} com {impacts.Count} impacto(s) identificado(s).");
            return 0;
        }

        // Era "run" antes do fluxo automatizado de GMUD assumir esse nome (gmud run <link|id>).
        case "run-impact":
        {
            if (!options.ConfirmedWrite)
            {
                return RequireConfirmWrite();
            }

            var gmudRequest = await orchestrator.RunAsync(options.WorkItemId);
            PrintHierarchy(gmudRequest);
            Console.WriteLine();
            Console.WriteLine(
                $"Processo completo: impacto técnico identificado e publicado em {gmudRequest.ChildWorkItems.Count} work item(ns) filho(s).");
            return 0;
        }

        case "release-branch":
        {
            if (!options.ConfirmedWrite)
            {
                return RequireConfirmWrite();
            }

            var environment = ResolveEnvironment(options.Ambiente);
            if (environment is null || string.IsNullOrWhiteSpace(options.Cliente))
            {
                Console.Error.WriteLine("Informe --ambiente <gmud|prd> e --cliente <sufixo> para este comando.");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(options.SubscriptionId))
            {
                Console.Error.WriteLine("Subscription do Azure não configurada. Informe --subscription-id <id> ou defina a variável de ambiente AZURE_SUBSCRIPTION_ID.");
                return 1;
            }

            var branchesByRepository = await CollectRepositoryBranchesAsync(hierarchyService, technicalImpactService, options.WorkItemId);
            if (branchesByRepository.Count == 0)
            {
                Console.WriteLine("Nenhum projeto/branch identificado nos comentários dos work items filhos. Nada para fazer.");
                return 0;
            }

            foreach (var (repositoryName, featureBranches) in branchesByRepository)
            {
                // A tag de origem vem sempre do site de PRODUÇÃO — o ambiente-alvo (environment.Value)
                // só é usado para nomear a branch de release, nunca para escolher de onde ela parte.
                if (!repositoryCatalog.TryGetSiteName(repositoryName, ProductionSiteSuffix, options.Cliente, out var siteName))
                {
                    Console.WriteLine($"[{repositoryName}] Sem Azure App Service correspondente (ex: Database) — trate a branch de release manualmente.");
                    continue;
                }

                var branchesToMerge = new List<string>(featureBranches);
                var catchUpBranch = await TryGetRecentGmudCatchUpBranchAsync(
                    repositoryCatalog, appServiceClient, client, repositoryName, options.Cliente, siteName, GmudCatchUpWindowDays);
                if (catchUpBranch is not null && !branchesToMerge.Contains(catchUpBranch, StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine(
                        $"[{repositoryName}] Release de GMUD em execução (\"{catchUpBranch}\") tem build com menos de {GmudCatchUpWindowDays} dias — será mesclada também.");
                    branchesToMerge.Add(catchUpBranch);
                }

                Console.WriteLine($"[{repositoryName}] Criando branch de release a partir da tag em produção em \"{siteName}\"...");
                try
                {
                    var releaseBranch = await integrationBranchService.CreateReleaseBranchAsync(new ReleaseBranchRequest(
                        Environment: environment.Value.BranchLabel,
                        ClientName: options.Cliente,
                        ReleaseDate: DateOnly.FromDateTime(DateTime.Today),
                        RepositoryName: repositoryName,
                        SiteName: siteName,
                        FeatureBranchNames: branchesToMerge));

                    Console.WriteLine($"[{repositoryName}] Branch \"{releaseBranch}\" criada — {branchesToMerge.Count} feature branch(es) mesclada(s).");
                }
                catch (MergeConflictException ex)
                {
                    Console.Error.WriteLine($"[{repositoryName}] CONFLITO DE MERGE — resolução manual necessária:{Environment.NewLine}{ex.Message}");
                    return 1;
                }
            }

            return 0;
        }

        case "pull-requests":
        {
            if (!options.ConfirmedWrite)
            {
                return RequireConfirmWrite();
            }

            var environment = ResolveEnvironment(options.Ambiente);
            if (environment is null || string.IsNullOrWhiteSpace(options.Cliente))
            {
                Console.Error.WriteLine("Informe --ambiente <gmud|prd> e --cliente <sufixo> para este comando.");
                return 1;
            }

            var targetBranch = string.IsNullOrWhiteSpace(options.TargetBranch) ? "main" : options.TargetBranch;
            var branchesByRepository = await CollectRepositoryBranchesAsync(hierarchyService, technicalImpactService, options.WorkItemId);
            if (branchesByRepository.Count == 0)
            {
                Console.WriteLine("Nenhum projeto/branch identificado nos comentários dos work items filhos. Nada para fazer.");
                return 0;
            }

            var releaseBranchName = releaseBranchNameBuilder.Build(environment.Value.BranchLabel, options.Cliente, DateOnly.FromDateTime(DateTime.Today));
            var requests = new List<RepositoryPullRequestRequest>();

            foreach (var (repositoryName, featureBranches) in branchesByRepository)
            {
                if (!repositoryCatalog.TryGetKind(repositoryName, out var kind))
                {
                    Console.WriteLine($"[{repositoryName}] Repositório não catalogado — pulei a criação de PR.");
                    continue;
                }

                // Database não roda em App Service (sem branch de release) — a PR sai direto da feature branch identificada.
                var sourceBranch = kind == RepositoryKind.Database ? featureBranches[0] : releaseBranchName;
                requests.Add(new RepositoryPullRequestRequest(
                    repositoryName, kind, sourceBranch, targetBranch,
                    Title: $"GMUD #{options.WorkItemId} — {repositoryName}",
                    Description: $"PR gerada automaticamente pela automação de GMUD para o work item #{options.WorkItemId}."));
            }

            var outcomes = await pullRequestOrchestrationService.OrchestrateAsync(requests);
            foreach (var outcome in outcomes)
            {
                var status = outcome.Completed
                    ? $"completada (buildId: {(outcome.BuildId?.ToString() ?? "não capturado")})"
                    : "criada como Draft/Ativa (aguardando aprovação manual do DBA)";
                Console.WriteLine($"[{outcome.RepositoryName}] PR #{outcome.PullRequestId} {status} — {outcome.Url}");
            }

            return 0;
        }

        default:
            PrintUsage();
            return 1;
    }
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Falha ao comunicar com o Azure DevOps: {ex.Message}");
    return 1;
}

/// <summary>
/// Se o repositório tiver um Azure App Service de GMUD distinto do de produção, e a última build da
/// branch atualmente publicada lá tiver menos de <paramref name="windowDays"/> dias, retorna o nome
/// dessa branch (para ser mesclada também na nova branch de release). Retorna null quando não há
/// ambiente de GMUD aplicável ou a última build é mais antiga que a janela — nesse caso não há nada
/// recente para "arrastar" para a nova release.
/// </summary>
static async Task<string?> TryGetRecentGmudCatchUpBranchAsync(
    IRepositoryCatalog repositoryCatalog,
    IAppServiceClient appServiceClient,
    IAzureDevOpsClient azureDevOpsClient,
    string repositoryName,
    string clientSuffix,
    string productionSiteName,
    int windowDays)
{
    if (!repositoryCatalog.TryGetSiteName(repositoryName, GmudSiteSuffix, clientSuffix, out var gmudSiteName)
        || string.Equals(gmudSiteName, productionSiteName, StringComparison.OrdinalIgnoreCase))
    {
        // Sem site de GMUD distinto do de produção para este repositório (ex: GerenciadorJobs só tem
        // instância de produção conhecida) — nada para verificar.
        return null;
    }

    // A tag é "<buildId>-<branch>-<commitSha>"; a branch em GMUD é o segmento central (mesma regra da release de origem).
    var gmudTag = await appServiceClient.GetCurrentTagAsync(gmudSiteName);
    if (!GmudNamingConventions.TryParseContainerTag(gmudTag, out var parsedTag))
    {
        Console.WriteLine($"[{repositoryName}] Tag \"{gmudTag}\" do site de GMUD fora do padrão <buildId>-<branch>-<commitSha> — catch-up ignorado.");
        return null;
    }

    var gmudBranch = new GmudNamingConventions(new NamingConfig()).SourceBranchFromRelease(parsedTag.ReleaseName);
    var buildDate = await azureDevOpsClient.GetLatestBuildDateForBranchAsync(gmudBranch);

    if (buildDate is null || DateTimeOffset.UtcNow - buildDate.Value > TimeSpan.FromDays(windowDays))
    {
        return null;
    }

    return gmudBranch;
}

static async Task<Dictionary<string, List<string>>> CollectRepositoryBranchesAsync(
    IGmudHierarchyService hierarchyService, ITechnicalImpactService technicalImpactService, int gmudWorkItemId)
{
    var hierarchy = await hierarchyService.BuildHierarchyAsync(gmudWorkItemId);
    var branchesByRepository = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    foreach (var child in hierarchy.ChildWorkItems)
    {
        var impacts = await technicalImpactService.IdentifyTechnicalImpactAsync(child.Id);
        foreach (var impact in impacts)
        {
            if (!branchesByRepository.TryGetValue(impact.ProjectName, out var branches))
            {
                branches = [];
                branchesByRepository[impact.ProjectName] = branches;
            }

            if (!branches.Contains(impact.BranchName, StringComparer.OrdinalIgnoreCase))
            {
                branches.Add(impact.BranchName);
            }
        }
    }

    return branchesByRepository;
}

static (string SiteSuffix, string BranchLabel)? ResolveEnvironment(string? ambiente) => ambiente?.Trim().ToLowerInvariant() switch
{
    "gmud" => ("gmud", "GMUD"),
    "prd" or "prod" => ("prd", "PROD"),
    _ => null
};

static int RequireConfirmWrite()
{
    Console.Error.WriteLine("Esse comando ESCREVE no Azure Boards. Informe também --confirm-write para prosseguir.");
    return 1;
}

static void PrintHierarchy(GmudAutomation.Core.Models.GmudRequest gmudRequest)
{
    Console.WriteLine($"GMUD #{gmudRequest.Id}: {gmudRequest.Title}");
    Console.WriteLine($"Work items filhos ({gmudRequest.ChildWorkItems.Count}):");
    foreach (var child in gmudRequest.ChildWorkItems)
    {
        Console.WriteLine($"  - #{child.Id} [{child.State}] {child.Title}");
    }
}

static string ResolvePatEnvironmentVariableName(string organization) =>
    string.Equals(organization, CliOptions.DefaultOrganization, StringComparison.OrdinalIgnoreCase)
        ? "AZURE_DEVOPS_EXT_PAT"
        : $"AZURE_DEVOPS_PAT_{Sanitize(organization)}";

static string Sanitize(string value)
{
    var chars = value.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
    return new string(chars);
}

static void PrintUsage()
{
    Console.WriteLine(
        $"""
        Uso: dotnet run -- <comando> (--url <url-do-work-item> | --work-item <id> [--org <organização>] [--project <projeto>]) [opções]

        A organização/projeto/ID podem ser informados de duas formas:
          --url <url>                    URL do work item no Azure Boards (ex: https://dev.azure.com/sua-org/SeuProjeto/_workitems/edit/123)
          --work-item/--org/--project    informados manualmente (padrão: {CliOptions.DefaultOrganization} / {CliOptions.DefaultProject})

        Se a organização resolvida bater com GMUD_KNOWN_ORGANIZATION (variável de ambiente, config local),
        o processo é reconhecido como "conhecido" e usa GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT como --repos-project
        padrão. Para qualquer outra organização, o mesmo processo roda normalmente para a organização informada.

        Opções:
          --repos-project <projeto>   Team Project dos repositórios/pipelines, se diferente do de Boards
                                       (padrão: GMUD_KNOWN_ORGANIZATION_REPOS_PROJECT, quando aplicável)
          --ambiente <gmud|prd>        Ambiente da GMUD (obrigatório para 'release-branch'/'pull-requests')
          --cliente <sufixo>           Sufixo do cliente no nome do App Service, ex: "cli1", "cli2" (idem)
          --target <branch>            Branch de destino da PR em 'pull-requests' (padrão: "main")
          --subscription-id <id>       Subscription do Azure App Service (padrão: variável de ambiente AZURE_SUBSCRIPTION_ID)
          --text <texto>               Texto do comentário (obrigatório para 'add-comment')
          --confirm-write              Necessário para comandos que escrevem no Azure Boards/Repos

        Comandos:
          hierarchy        US 1.1 — mapeia a hierarquia da GMUD (somente leitura)
          impact           US 1.2 — identifica impacto técnico e valida branches (somente leitura)
          add-comment      Publica um comentário arbitrário no card (uso: dados de teste)
          publish-impact   US 1.2 — roda 'impact' e publica o resultado como comentário no card
          run-impact       Processo completo Feature 1: hierarquia + impacto técnico publicado em cada filho
          release-branch   US 2.1 — cria a branch de release e mescla as features identificadas (--ambiente/--cliente)
          pull-requests    US 3.1 — abre as PRs (API/WEB/Backend completadas; Database só criada) (--ambiente/--cliente)

        """);
    GmudPipelineCommands.PrintUsage();
}

internal sealed class CliOptions
{
    // Sandbox padrão para uso sem --url (ex: validação em ambiente que não é o do cliente).
    public const string DefaultOrganization = "eleven11C";
    public const string DefaultProject = "Applied AI Engineering";

    // Subscription do Azure onde vivem os App Services da organização-alvo, usada como padrão para
    // 'release-branch'/'pull-requests' — nunca hardcoded no código-fonte; vem de configuração local
    // (ex: em .mcp/.env) para não expor o ID real da subscription no repositório publicado.
    public static string? DefaultSubscriptionId => Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");

    public required string Command { get; init; }
    public required int WorkItemId { get; init; }
    public required string Organization { get; init; }
    public required string BoardsProject { get; init; }
    public string? ReposProject { get; init; }
    public bool ConfirmedWrite { get; init; }
    public string? CommentText { get; init; }
    public string? Ambiente { get; init; }
    public string? Cliente { get; init; }
    public string? TargetBranch { get; init; }
    public required string SubscriptionId { get; init; }

    public static CliOptions? Parse(string[] args, IAzureBoardsUrlParser urlParser)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var command = args[0];
        int? workItemId = null;
        var organization = DefaultOrganization;
        var project = DefaultProject;
        string? reposProject = null;
        var confirmedWrite = false;
        string? commentText = null;
        string? url = null;
        string? ambiente = null;
        string? cliente = null;
        string? targetBranch = null;
        var subscriptionId = DefaultSubscriptionId ?? string.Empty;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--work-item" when i + 1 < args.Length && int.TryParse(args[++i], out var id):
                    workItemId = id;
                    break;
                case "--org" when i + 1 < args.Length:
                    organization = args[++i];
                    break;
                case "--project" when i + 1 < args.Length:
                    project = args[++i];
                    break;
                case "--repos-project" when i + 1 < args.Length:
                    reposProject = args[++i];
                    break;
                case "--url" when i + 1 < args.Length:
                    url = args[++i];
                    break;
                case "--text" when i + 1 < args.Length:
                    commentText = args[++i];
                    break;
                case "--ambiente" when i + 1 < args.Length:
                    ambiente = args[++i];
                    break;
                case "--cliente" when i + 1 < args.Length:
                    cliente = args[++i];
                    break;
                case "--target" when i + 1 < args.Length:
                    targetBranch = args[++i];
                    break;
                case "--subscription-id" when i + 1 < args.Length:
                    subscriptionId = args[++i];
                    break;
                case "--confirm-write":
                    confirmedWrite = true;
                    break;
            }
        }

        if (url is not null)
        {
            if (!urlParser.TryParse(url, out var reference))
            {
                return null;
            }

            organization = reference.Organization;
            project = reference.Project;
            workItemId = reference.WorkItemId;
        }

        if (workItemId is null)
        {
            return null;
        }

        return new CliOptions
        {
            Command = command,
            WorkItemId = workItemId.Value,
            Organization = organization,
            BoardsProject = project,
            ReposProject = reposProject,
            ConfirmedWrite = confirmedWrite,
            CommentText = commentText,
            Ambiente = ambiente,
            Cliente = cliente,
            TargetBranch = targetBranch,
            SubscriptionId = subscriptionId
        };
    }
}
