using System.Globalization;
using System.Text;
using BacklogAutomation.Core.Ai;
using BacklogAutomation.Core.Documents;
using BacklogAutomation.Core.Formatting;
using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Parsing;
using BacklogAutomation.Core.Providers;
using BacklogAutomation.Core.Providers.AzureDevOps;
using BacklogAutomation.Core.Providers.Jira;
using BacklogAutomation.Core.Providers.Trello;
using BacklogAutomation.Core.Services;
using BacklogAutomation.Core.Validation;

Console.OutputEncoding = Encoding.UTF8;

// Segredos (PAT, API tokens, chave da LLM) vêm SEMPRE de variáveis de ambiente — opcionalmente de um
// arquivo .env local (fora do controle de versão), procurado a partir do diretório atual para cima.
DotEnv.Load();

var options = CliOptions.Parse(args);
if (options is null)
{
    PrintUsage();
    return 1;
}

if (options.Command == "template")
{
    Console.WriteLine(Templates.Markdown);
    return 0;
}

if (options.Command is not ("plan" or "create"))
{
    PrintUsage();
    return 1;
}

var estimateParser = new EstimateParser(options.HoursPerDay);
using var llmHttpClient = new HttpClient();
var aiGenerator = CreateAiGenerator(llmHttpClient);
var planBuilder = new BacklogPlanBuilder(new DocumentTextExtractor(), new MarkdownBacklogParser(estimateParser), aiGenerator);
var validator = new BacklogPlanValidator(options.HoursPerDay);
var formatter = new BacklogPlanFormatter();

try
{
    var plan = await LoadPlanAsync(options, planBuilder);
    Console.WriteLine();
    Console.WriteLine(formatter.FormatTree(plan));

    var report = validator.Validate(plan);
    Console.WriteLine(formatter.FormatValidation(report));

    if (options.OutputPath is not null)
    {
        await File.WriteAllTextAsync(options.OutputPath, BacklogJson.Serialize(plan));
        Console.WriteLine($"Backlog salvo em \"{options.OutputPath}\" — revise/ajuste e crie com: create --plan \"{options.OutputPath}\" ...");
    }

    if (options.Command == "plan")
    {
        Console.WriteLine("(somente leitura — nada foi criado; use o comando 'create' com --confirm-write para criar os cards)");
        return report.HasErrors ? 2 : 0;
    }

    if (report.HasErrors)
    {
        Console.Error.WriteLine("O backlog tem erros de validação — corrija-os antes de criar os cards.");
        return 2;
    }

    if (!options.ConfirmedWrite)
    {
        Console.Error.WriteLine("Esse comando CRIA cards na ferramenta de destino. Revise a prévia acima e informe --confirm-write para prosseguir.");
        return 1;
    }

    using var trackerHttpClient = new HttpClient();
    var provider = CreateProvider(options, trackerHttpClient);
    if (provider is null)
    {
        return 1;
    }

    Console.WriteLine($"Criando {plan.EnumerateAll().Count()} item(ns) no {provider.Name}...");
    var service = new BacklogCreationService(provider);
    var progress = new SynchronousProgress<CreatedWorkItem>(PrintCreated);

    try
    {
        var result = await service.CreateAsync(plan, progress);
        Console.WriteLine();
        Console.WriteLine($"Concluído: {result.CreatedItems.Count} item(ns) criado(s) no {provider.Name}, com a tag \"{BacklogCreationService.AutomationTag}\".");
        return 0;
    }
    catch (BacklogCreationException ex)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(ex.Message);
        Console.Error.WriteLine(
            $"{ex.CreatedItems.Count} item(ns) já haviam sido criados (listados acima, tag \"{BacklogCreationService.AutomationTag}\"). "
            + "Remova-os ou ajuste o plano (--plan) para conter só o que falta antes de executar novamente.");
        return 1;
    }
}
catch (Exception ex) when (ex is BacklogParseException or LlmException or InvalidOperationException or ArgumentException
    or FileNotFoundException or NotSupportedException or System.Text.Json.JsonException or HttpRequestException)
{
    Console.Error.WriteLine($"Erro: {ex.Message}");
    return 1;
}

static async Task<BacklogPlan> LoadPlanAsync(CliOptions options, BacklogPlanBuilder planBuilder)
{
    var aiOptions = new AiGenerationOptions
    {
        HoursPerDay = options.HoursPerDay,
        SuggestMissingEstimates = options.SuggestEstimates,
        AdditionalInstructions = options.Instructions
    };

    BacklogPlanBuildResult result;
    if (options.PlanPath is not null)
    {
        result = await planBuilder.FromFileAsync(options.PlanPath, ParserMode.Json, aiOptions);
    }
    else if (options.FilePath is not null)
    {
        Console.WriteLine($"Lendo \"{options.FilePath}\"...");
        result = await planBuilder.FromFileAsync(options.FilePath, options.Parser, aiOptions);
    }
    else
    {
        result = await planBuilder.FromPromptAsync(options.Prompt!, options.Parser, aiOptions);
    }

    Console.WriteLine(result.StrategyUsed switch
    {
        ParserMode.Json => "Backlog carregado do JSON revisado.",
        ParserMode.Markdown => "Documento estruturado detectado — backlog extraído pelo parser determinístico (sem IA).",
        _ => "Backlog gerado por IA a partir do conteúdo — REVISE antes de criar (use --out para salvar e ajustar)."
    });

    return result.Plan;
}

static IAiBacklogGenerator? CreateAiGenerator(HttpClient httpClient)
{
    var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY") ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return null;
    }

    var maxTokens = int.TryParse(Environment.GetEnvironmentVariable("LLM_MAX_TOKENS"), out var parsed) ? parsed : 16384;
    return new AiBacklogGenerator(new OpenAiCompatibleLlmClient(httpClient, new LlmClientOptions
    {
        ApiKey = apiKey,
        ApiUrl = Environment.GetEnvironmentVariable("LLM_API_URL") ?? LlmClientOptions.DefaultApiUrl,
        Model = Environment.GetEnvironmentVariable("LLM_MODEL") ?? LlmClientOptions.DefaultModel,
        MaxTokens = maxTokens
    }));
}

static IWorkItemTrackerProvider? CreateProvider(CliOptions options, HttpClient httpClient)
{
    switch (options.Target)
    {
        case "azure":
        {
            var organization = options.Organization ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_DEFAULT_ORG");
            var project = options.Project ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_DEFAULT_PROJECT");
            if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(project))
            {
                Console.Error.WriteLine("Informe --org e --project (ou --url https://dev.azure.com/<org>/<projeto>), ou defina AZURE_DEVOPS_DEFAULT_ORG/AZURE_DEVOPS_DEFAULT_PROJECT.");
                return null;
            }

            var patVariable = $"AZURE_DEVOPS_PAT_{Sanitize(organization)}";
            var pat = Environment.GetEnvironmentVariable(patVariable) ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT");
            if (string.IsNullOrWhiteSpace(pat))
            {
                Console.Error.WriteLine($"PAT não configurado. Defina {patVariable} ou AZURE_DEVOPS_EXT_PAT (escopo Work Items: Read & Write).");
                return null;
            }

            return new AzureDevOpsBoardsProvider(httpClient, new AzureDevOpsBoardsOptions
            {
                Organization = organization,
                Project = project,
                PersonalAccessToken = pat,
                Process = options.Process,
                AreaPath = options.AreaPath,
                IterationPath = options.IterationPath,
                CustomFieldMap = options.FieldMap
            });
        }

        case "jira":
        {
            var baseUrl = Environment.GetEnvironmentVariable("JIRA_BASE_URL");
            var email = Environment.GetEnvironmentVariable("JIRA_EMAIL");
            var token = Environment.GetEnvironmentVariable("JIRA_API_TOKEN");
            var projectKey = options.Project ?? Environment.GetEnvironmentVariable("JIRA_PROJECT_KEY");
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(projectKey))
            {
                Console.Error.WriteLine("Defina JIRA_BASE_URL, JIRA_EMAIL e JIRA_API_TOKEN, e informe --project <CHAVE> (ou JIRA_PROJECT_KEY).");
                return null;
            }

            return new JiraProvider(httpClient, new JiraOptions
            {
                BaseUrl = baseUrl,
                Email = email,
                ApiToken = token,
                ProjectKey = projectKey,
                StoryPointsField = Environment.GetEnvironmentVariable("JIRA_STORY_POINTS_FIELD") ?? "customfield_10016",
                CustomFieldMap = options.FieldMap
            });
        }

        case "trello":
        {
            var apiKey = Environment.GetEnvironmentVariable("TRELLO_API_KEY");
            var token = Environment.GetEnvironmentVariable("TRELLO_TOKEN");
            var boardId = options.Board ?? Environment.GetEnvironmentVariable("TRELLO_BOARD_ID");
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(boardId))
            {
                Console.Error.WriteLine("Defina TRELLO_API_KEY e TRELLO_TOKEN, e informe --board <id> (ou TRELLO_BOARD_ID).");
                return null;
            }

            return new TrelloProvider(httpClient, new TrelloOptions
            {
                ApiKey = apiKey,
                Token = token,
                BoardId = boardId,
                ListName = options.ListName ?? "Backlog"
            });
        }

        default:
            Console.Error.WriteLine("Informe o destino com --target azure|jira|trello.");
            return null;
    }
}

static void PrintCreated(CreatedWorkItem item)
{
    var indent = item.Kind switch
    {
        WorkItemKind.Epic => string.Empty,
        WorkItemKind.Feature => "  ",
        WorkItemKind.UserStory => "    ",
        _ => "      "
    };

    var reference = item.IsVirtual ? $"({item.ProviderType})" : $"#{item.Id} [{item.ProviderType}] {item.Url}";
    Console.WriteLine($"{indent}{item.Kind.DisplayName()} \"{item.Title}\" → {reference}");
    foreach (var warning in item.Warnings)
    {
        Console.WriteLine($"{indent}  aviso: {warning}");
    }
}

static string Sanitize(string value) =>
    new(value.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

static void PrintUsage()
{
    Console.WriteLine(
        """
        Uso: dotnet run --project BacklogAutomation.Cli -- <comando> <entrada> [opções]

        Comandos:
          plan       Gera e valida o backlog e mostra a prévia (somente leitura). Use --out para salvar o JSON.
          create     Gera/carrega o backlog, valida e CRIA os cards na hierarquia Epic → Feature → US → Task/Bug.
          template   Imprime um modelo de documento estruturado (Markdown) que dispensa IA.

        Entrada (uma delas):
          --file <arquivo>       Documento anexado: .md, .txt, .docx, .pdf ou .json
          --prompt "<texto>"     Solicitação em linguagem natural (gerada por IA)
          --plan <backlog.json>  Backlog já revisado (saída de 'plan --out')

        Geração:
          --parser auto|markdown|ai|json   Padrão auto: JSON → direto; Markdown/Word estruturado → sem IA; texto livre → IA
          --suggest-estimates              Permite que a IA SUGIRA estimativas ausentes (marcadas com a tag estimativa-sugerida-ia)
          --instructions "<texto>"         Instruções adicionais para a IA (ex: "personas: lojista e cliente final")
          --hours-per-day <n>              Horas por dia ao converter estimativas em dias (padrão: 8)
          --out <backlog.json>             Salva o backlog gerado para revisão

        Destino (create):
          --target azure|jira|trello
          --confirm-write                  Obrigatório para criar os cards
          --field-map "<medida>=<campo>"   Mapeia uma medida livre do documento para um campo (repetível)
                                           ex: --field-map "Pontos de função=Custom.PontosDeFuncao"

          Azure DevOps:  --org <org> --project <projeto> | --url https://dev.azure.com/<org>/<projeto>
                         [--process agile|scrum|cmmi|basic] [--area-path <caminho>] [--iteration-path <caminho>]
                         PAT: AZURE_DEVOPS_PAT_<ORG> ou AZURE_DEVOPS_EXT_PAT
          Jira:          --project <CHAVE>   (JIRA_BASE_URL, JIRA_EMAIL, JIRA_API_TOKEN, JIRA_STORY_POINTS_FIELD)
          Trello:        --board <id> [--list <nome>, padrão "Backlog"]   (TRELLO_API_KEY, TRELLO_TOKEN)

        IA: LLM_API_KEY (ou GROQ_API_KEY), LLM_API_URL (padrão Groq), LLM_MODEL (padrão openai/gpt-oss-120b)
        """);
}

internal static class Templates
{
    public const string Markdown =
        """
        # Epic: <objetivo de negócio>
        Descrição do valor de negócio do Epic.
        Tags: <tag1>, <tag2>
        Prioridade: Alta

        ## Feature: <capacidade entregável>
        Descrição da Feature.

        ### User Story: <título curto> (5 SP)
        Como <persona>, quero <ação>, para que <benefício>.

        **Critérios de aceite:**
        - Dado <contexto>, quando <ação>, então <resultado>
        - Dado <contexto>, quando <ação>, então <resultado>

        **Estimativas:**
        - Story Points: 5
        - Pontos de função: 12

        - Task: <atividade técnica> (4h)
        - Task: <atividade técnica> (1 dia)
        - Bug: <defeito> (2h)

        #### Bug: <defeito detalhado> (3h)
        Passos para reproduzir:
        1. <passo>
        2. <passo>
        Resultado esperado: <...>
        Resultado atual: <...>
        """;
}

internal sealed class CliOptions
{
    public required string Command { get; init; }
    public string? FilePath { get; init; }
    public string? Prompt { get; init; }
    public string? PlanPath { get; init; }
    public string? OutputPath { get; init; }
    public ParserMode Parser { get; init; }
    public bool SuggestEstimates { get; init; }
    public string? Instructions { get; init; }
    public decimal HoursPerDay { get; init; } = 8m;
    public string? Target { get; init; }
    public bool ConfirmedWrite { get; init; }
    public string? Organization { get; init; }
    public string? Project { get; init; }
    public AzureDevOpsProcess? Process { get; init; }
    public string? AreaPath { get; init; }
    public string? IterationPath { get; init; }
    public string? Board { get; init; }
    public string? ListName { get; init; }
    public Dictionary<string, string> FieldMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static CliOptions? Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var command = args[0].ToLowerInvariant();
        string? file = null, prompt = null, plan = null, output = null, instructions = null, target = null;
        string? organization = null, project = null, areaPath = null, iterationPath = null, board = null, listName = null;
        var parser = ParserMode.Auto;
        AzureDevOpsProcess? process = null;
        var suggest = false;
        var confirmed = false;
        var hoursPerDay = 8m;
        var fieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--file" when i + 1 < args.Length:
                    file = args[++i];
                    break;
                case "--prompt" when i + 1 < args.Length:
                    prompt = args[++i];
                    break;
                case "--plan" when i + 1 < args.Length:
                    plan = args[++i];
                    break;
                case "--out" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--parser" when i + 1 < args.Length && Enum.TryParse<ParserMode>(args[i + 1], ignoreCase: true, out var mode):
                    parser = mode;
                    i++;
                    break;
                case "--suggest-estimates":
                    suggest = true;
                    break;
                case "--instructions" when i + 1 < args.Length:
                    instructions = args[++i];
                    break;
                case "--hours-per-day" when i + 1 < args.Length
                    && decimal.TryParse(args[i + 1].Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) && hours > 0:
                    hoursPerDay = hours;
                    i++;
                    break;
                case "--target" when i + 1 < args.Length:
                    target = args[++i].ToLowerInvariant() switch
                    {
                        "azure" or "azure-devops" or "ado" or "azuredevops" => "azure",
                        var other => other
                    };
                    break;
                case "--confirm-write":
                    confirmed = true;
                    break;
                case "--org" when i + 1 < args.Length:
                    organization = args[++i];
                    break;
                case "--project" when i + 1 < args.Length:
                    project = args[++i];
                    break;
                case "--url" when i + 1 < args.Length:
                    if (TryParseAzureProjectUrl(args[++i], out var urlOrganization, out var urlProject))
                    {
                        organization = urlOrganization;
                        project = urlProject;
                        target ??= "azure";
                    }

                    break;
                case "--process" when i + 1 < args.Length && Enum.TryParse<AzureDevOpsProcess>(args[i + 1], ignoreCase: true, out var parsedProcess):
                    process = parsedProcess;
                    i++;
                    break;
                case "--area-path" when i + 1 < args.Length:
                    areaPath = args[++i];
                    break;
                case "--iteration-path" when i + 1 < args.Length:
                    iterationPath = args[++i];
                    break;
                case "--board" when i + 1 < args.Length:
                    board = args[++i];
                    break;
                case "--list" when i + 1 < args.Length:
                    listName = args[++i];
                    break;
                case "--field-map" when i + 1 < args.Length:
                    var mapping = args[++i].Split('=', 2, StringSplitOptions.TrimEntries);
                    if (mapping.Length == 2 && mapping[0].Length > 0 && mapping[1].Length > 0)
                    {
                        fieldMap[mapping[0]] = mapping[1];
                    }

                    break;
            }
        }

        var inputs = new[] { file, prompt, plan }.Count(value => !string.IsNullOrWhiteSpace(value));
        if (command != "template" && inputs != 1)
        {
            return null;
        }

        return new CliOptions
        {
            Command = command,
            FilePath = file,
            Prompt = prompt,
            PlanPath = plan,
            OutputPath = output,
            Parser = parser,
            SuggestEstimates = suggest,
            Instructions = instructions,
            HoursPerDay = hoursPerDay,
            Target = target,
            ConfirmedWrite = confirmed,
            Organization = organization,
            Project = project,
            Process = process,
            AreaPath = areaPath,
            IterationPath = iterationPath,
            Board = board,
            ListName = listName,
            FieldMap = fieldMap
        };
    }

    /// <summary>Aceita https://dev.azure.com/{org}/{projeto}[/...] e https://{org}.visualstudio.com/{projeto}[/...].</summary>
    private static bool TryParseAzureProjectUrl(string url, out string organization, out string project)
    {
        organization = project = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 2)
        {
            (organization, project) = (segments[0], segments[1]);
            return true;
        }

        if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase) && segments.Length >= 1)
        {
            (organization, project) = (uri.Host.Split('.')[0], segments[0]);
            return true;
        }

        return false;
    }
}

/// <summary>IProgress que reporta na mesma thread (o Progress&lt;T&gt; padrão posta no thread pool e embaralha a saída do console).</summary>
internal sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

/// <summary>Carrega variáveis de um arquivo .env (sem sobrescrever as já definidas no ambiente).</summary>
internal static class DotEnv
{
    public static void Load()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, ".env");
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#') || !trimmed.Contains('='))
                    {
                        continue;
                    }

                    var (key, value) = (trimmed[..trimmed.IndexOf('=')].Trim(), trimmed[(trimmed.IndexOf('=') + 1)..].Trim().Trim('"', '\''));
                    if (key.Length > 0 && value.Length > 0 && Environment.GetEnvironmentVariable(key) is null)
                    {
                        Environment.SetEnvironmentVariable(key, value);
                    }
                }

                return;
            }

            directory = directory.Parent;
        }
    }
}
