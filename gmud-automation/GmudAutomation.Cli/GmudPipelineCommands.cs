using GmudAutomation.Core.Client;
using GmudAutomation.Core.Pipeline;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;
using GmudAutomation.Core.Pipeline.Reporting;
using GmudAutomation.Core.Services;

/// <summary>
/// Fluxo automatizado de preparação de GMUD (etapas 1 a 11):
/// <c>gmud run &lt;link|id&gt; [--dry-run] [--from-step N] [--confirm-write]</c>, <c>gmud status &lt;id&gt;</c> e <c>gmud report &lt;id&gt;</c>.
/// </summary>
internal static class GmudPipelineCommands
{
    public static bool Handles(string command) => command is "run" or "status" or "report";

    public static async Task<int> RunAsync(string[] args)
    {
        var options = PipelineOptions.Parse(args);
        if (options is null)
        {
            PrintUsage();
            return 1;
        }

        GmudConfig config;
        try
        {
            config = GmudConfig.LoadFromFile(options.ConfigPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(
                $"Configuração não encontrada em \"{options.ConfigPath}\". Copie gmud.config.example.json para gmud.config.json (fora do Git) " +
                "ou informe --config <caminho> / GMUD_CONFIG_PATH.");
            return 1;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var store = new FileGmudManifestStore(config.WorkspaceDirectory);
        var urlParser = new AzureBoardsUrlParser();
        if (!GmudPipelineRunner.TryParseInput(options.Target, urlParser, out var gmudId, out _))
        {
            Console.Error.WriteLine($"\"{options.Target}\" não é um ID nem um link de work item.");
            return 1;
        }

        return options.Command switch
        {
            "status" => PrintStatus(store, config, gmudId),
            "report" => WriteReport(store, config, gmudId),
            _ => await RunPipelineAsync(options, config, store, gmudId)
        };
    }

    private static async Task<int> RunPipelineAsync(PipelineOptions options, GmudConfig config, FileGmudManifestStore store, int gmudId)
    {
        // PAT por organização (mesma convenção do mcp-tooling); o valor nunca é exibido nem gravado.
        var patVariable = $"AZURE_DEVOPS_PAT_{Sanitize(config.Organization)}";
        var pat = Environment.GetEnvironmentVariable(patVariable) ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_EXT_PAT");
        if (string.IsNullOrWhiteSpace(pat))
        {
            Console.Error.WriteLine($"PAT não configurado. Defina {patVariable} (ou AZURE_DEVOPS_EXT_PAT) com escopo Work Items (leitura), Code (leitura/escrita) e Build (leitura).");
            return 1;
        }

        var subscriptionId = config.SubscriptionId ?? Environment.GetEnvironmentVariable("AZURE_SUBSCRIPTION_ID");
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            Console.Error.WriteLine("Subscription dos App Services não configurada. Defina AZURE_SUBSCRIPTION_ID (e faça az login).");
            return 1;
        }

        var devOpsOptions = new AzureDevOpsClientOptions
        {
            Organization = config.Organization,
            BoardsProject = config.BoardsProject,
            ReposProject = config.ReposProject,
            PersonalAccessToken = pat
        };

        using var boardsHttp = new HttpClient();
        using var reposHttp = new HttpClient();
        using var appServiceHttp = new HttpClient();
        var boards = new AzureDevOpsClient(boardsHttp, devOpsOptions);
        var repos = new AzureReposClient(reposHttp, devOpsOptions);
        var appServices = new AppServiceClient(appServiceHttp, new AppServiceClientOptions { SubscriptionId = subscriptionId }, new AzureCliAccessTokenProvider());

        IReadOnlyList<IGmudStep> steps;
        try
        {
            steps = GmudPipelineFactory.CreateSteps(config, boards, repos, appServices);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        IOperatorConfirmation confirmation = Console.IsInputRedirected ? new FlagOnlyOperatorConfirmation() : new ConsoleOperatorConfirmation();
        var runner = new GmudPipelineRunner(steps, store, confirmation, config.WorkspaceDirectory);
        using var logger = new ConsoleJsonLinesGmudLogger(Console.Out, Console.Error, Path.Combine(store.GetGmudDirectory(gmudId), "execucao.log.jsonl"));

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        GmudRunResult result;
        try
        {
            result = await runner.RunAsync(new GmudRunRequest(options.Target, gmudId, options.DryRun, options.FromStep, options.ConfirmWrite), logger, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Execução interrompida. O manifesto guarda o progresso até a última etapa concluída.");
            return 1;
        }

        PrintRunSummary(result, store, gmudId);
        return result.ExitCode;
    }

    private static void PrintRunSummary(GmudRunResult result, FileGmudManifestStore store, int gmudId)
    {
        var manifest = result.Manifest;
        Console.WriteLine();

        if (manifest.Plan.Count > 0 && result.Outcome == GmudRunOutcome.DryRunCompleted)
        {
            Console.WriteLine("PLANO (dry-run — nada foi escrito no Azure):");
            foreach (var line in manifest.Plan)
            {
                Console.WriteLine($"  {line}");
            }

            Console.WriteLine();
        }

        if (PrintDatabaseBranches(manifest))
        {
            Console.WriteLine();
        }

        if (manifest.Warnings.Count > 0)
        {
            Console.WriteLine("Avisos:");
            foreach (var warning in manifest.Warnings)
            {
                Console.WriteLine($"  - {warning}");
            }

            Console.WriteLine();
        }

        var manifestPath = Path.Combine(store.GetGmudDirectory(gmudId), FileGmudManifestStore.ManifestFileName);
        var outcome = result.Outcome switch
        {
            GmudRunOutcome.Completed => "Concluído.",
            GmudRunOutcome.DryRunCompleted => $"Dry-run concluído. Revise o plano e rode: gmud run {gmudId} --confirm-write",
            GmudRunOutcome.ConfirmationRequired => $"Parado antes da etapa {result.StoppedAtStep}: {result.Message}",
            GmudRunOutcome.Cancelled => $"Cancelado na etapa {result.StoppedAtStep}: {result.Message}",
            GmudRunOutcome.Blocked => $"AGUARDANDO DECISÃO na etapa {result.StoppedAtStep}:{Environment.NewLine}{result.Message}",
            _ => $"FALHOU na etapa {result.StoppedAtStep}: {result.Message}"
        };

        (result.ExitCode == 0 ? Console.Out : Console.Error).WriteLine(outcome);
        Console.WriteLine($"Manifesto: {manifestPath}");
    }

    /// <summary>Branches do desenvolvedor no Database, com link — só as user stories que alteram o banco.</summary>
    private static bool PrintDatabaseBranches(GmudManifest manifest)
    {
        var branches = manifest.Database?.Branches.Where(b => DatabaseBranchStatus.AffectsDatabase(b.Status)).ToList() ?? [];
        if (branches.Count == 0)
        {
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"{manifest.Database!.Repository} (branches do desenvolvedor):");
        foreach (var branch in branches)
        {
            var commits = branch.CommitsAhead is null ? string.Empty : $", {branch.CommitsAhead} commit(s) à frente de {branch.BaseBranch}";
            Console.WriteLine($"  #{branch.UserStoryId} {branch.Branch}: {branch.Status}{(branch.Commit is null ? string.Empty : $" @ {Short(branch.Commit)}")}{commits}");
            if (branch.Url is not null)
            {
                Console.WriteLine($"    {branch.Url}");
            }
        }

        return true;
    }

    private static int PrintStatus(FileGmudManifestStore store, GmudConfig config, int gmudId)
    {
        var manifest = store.Load(gmudId);
        if (manifest is null)
        {
            Console.Error.WriteLine($"Nenhum manifesto para a GMUD #{gmudId} em {config.WorkspaceDirectory}/. Rode: gmud run {gmudId} --dry-run");
            return 1;
        }

        Console.WriteLine($"GMUD #{manifest.Gmud.Id}: {manifest.Gmud.Title}");
        Console.WriteLine($"Cliente {manifest.Gmud.Client} · data {manifest.Gmud.Date}");
        Console.WriteLine();
        Console.WriteLine("Etapas:");
        for (var i = 0; i < config.Steps.Count; i++)
        {
            Console.WriteLine($"  {i + 1,2}. {config.Steps[i],-26} {manifest.GetStepStatus(i + 1)}");
        }

        Console.WriteLine();
        Console.WriteLine("User stories:");
        foreach (var userStory in manifest.UserStories)
        {
            Console.WriteLine($"  #{userStory.Id} [{userStory.State}] {userStory.Title}");
            foreach (var item in userStory.Items)
            {
                Console.WriteLine($"    - {item.Type} #{item.Id}: {string.Join(", ", item.Projects)}{(item.DatabaseAttachments.Count > 0 ? $" · {item.DatabaseAttachments.Count} script(s)" : string.Empty)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Repositórios:");
        foreach (var (name, entry) in manifest.Repositories)
        {
            Console.WriteLine($"  {name} ({entry.Kind}) — base {entry.BaseBranch ?? "?"} @ {Short(entry.BaseCommit)} → {entry.GmudBranch} @ {Short(entry.GmudCommit)}");
            foreach (var merge in entry.Merges)
            {
                var pending = merge.Conflicts.Count(c => c.Resolution == ConflictResolution.Ambiguous && c.Decision is null);
                Console.WriteLine($"    merge {merge.Source}: {merge.Status}{(pending > 0 ? $" — {pending} arquivo(s) aguardando decisão" : string.Empty)}");
            }

            foreach (var (environment, release) in entry.Releases)
            {
                var build = entry.Builds.GetValueOrDefault(environment);
                Console.WriteLine($"    {environment}: {release}{(build is null ? string.Empty : $" · build {build.BuildId} {build.Result ?? build.Status} · {build.NewTag}")}");
            }

            if (entry.Blocker is not null)
            {
                Console.WriteLine($"    BLOQUEIO: {entry.Blocker}");
            }
        }

        PrintDatabaseBranches(manifest);

        if (manifest.Warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Avisos ({manifest.Warnings.Count}):");
            foreach (var warning in manifest.Warnings)
            {
                Console.WriteLine($"  - {warning}");
            }
        }

        return 0;
    }

    private static int WriteReport(FileGmudManifestStore store, GmudConfig config, int gmudId)
    {
        var manifest = store.Load(gmudId);
        if (manifest is null)
        {
            Console.Error.WriteLine($"Nenhum manifesto para a GMUD #{gmudId}. Rode: gmud run {gmudId} --dry-run");
            return 1;
        }

        var report = InfraReportFormatter.Format(manifest, config, GmudNamingConventions.FromConfig(config));
        var path = Path.Combine(store.GetGmudDirectory(gmudId), InfraReportFormatter.FileName);
        File.WriteAllText(path, report);
        Console.WriteLine(report);
        Console.WriteLine($"(gravado em {path})");
        return 0;
    }

    private static string Short(string? commit) => commit is null ? "?" : commit[..Math.Min(8, commit.Length)];

    private static string Sanitize(string value) => new(value.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            Fluxo automatizado de preparação de GMUD (11 etapas, manifesto em gmuds/<id>/manifest.json):

              run <link|id> [--dry-run] [--from-step N] [--confirm-write] [--config <arquivo>]
                  --dry-run        executa as etapas 1 a 9 sem escrever no Azure e mostra o plano
                  --from-step N    reexecuta a partir da etapa N (padrão: primeira etapa não concluída)
                  --confirm-write  autoriza criar branches, merges e releases (pede confirmação no terminal interativo)
              status <id>        mostra o manifesto resumido
              report <id>        gera gmuds/<id>/infra.md (tags atual/nova por App Service e scripts de banco)

            Configuração: gmud.config.json (ou --config / GMUD_CONFIG_PATH); formato em gmud.config.example.json.
            Credenciais: AZURE_DEVOPS_PAT_<ORG> (ou AZURE_DEVOPS_EXT_PAT), AZURE_SUBSCRIPTION_ID e az login.
            Códigos de saída: 0 ok · 1 falha · 2 aguardando decisão do operador · 3 confirmação de escrita necessária.
            """);
    }

    private sealed record PipelineOptions(string Command, string Target, bool DryRun, int? FromStep, bool ConfirmWrite, string ConfigPath)
    {
        public static PipelineOptions? Parse(string[] args)
        {
            string? target = null;
            var dryRun = false;
            var confirmWrite = false;
            int? fromStep = null;
            var configPath = Environment.GetEnvironmentVariable("GMUD_CONFIG_PATH") ?? "gmud.config.json";

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--confirm-write":
                        confirmWrite = true;
                        break;
                    case "--from-step" when i + 1 < args.Length && int.TryParse(args[i + 1], out var step):
                        fromStep = step;
                        i++;
                        break;
                    case "--config" when i + 1 < args.Length:
                        configPath = args[++i];
                        break;
                    // Compatibilidade com os demais comandos da CLI.
                    case "--url" or "--work-item" when i + 1 < args.Length:
                        target = args[++i];
                        break;
                    default:
                        if (!args[i].StartsWith("--", StringComparison.Ordinal))
                        {
                            target ??= args[i];
                        }
                        else
                        {
                            Console.Error.WriteLine($"Opção desconhecida: {args[i]}");
                            return null;
                        }

                        break;
                }
            }

            if (target is null || (dryRun && confirmWrite))
            {
                if (dryRun && confirmWrite)
                {
                    Console.Error.WriteLine("--dry-run e --confirm-write não podem ser usados juntos.");
                }

                return null;
            }

            return new PipelineOptions(args[0], target, dryRun, fromStep, confirmWrite, configPath);
        }
    }
}

/// <summary>Confirmação interativa: mostra o resumo de escrita por repositório e exige "s".</summary>
internal sealed class ConsoleOperatorConfirmation : IOperatorConfirmation
{
    public Task<bool> ConfirmAsync(int stepNumber, string stepName, string summary, CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine($"A etapa {stepNumber} ({stepName}) vai escrever no Azure DevOps:");
        Console.WriteLine(string.IsNullOrWhiteSpace(summary) ? "  (sem resumo disponível)" : summary.TrimEnd());
        Console.Write("Confirmar? (s/N) ");
        var answer = Console.ReadLine();
        return Task.FromResult(answer is not null && answer.Trim().Equals("s", StringComparison.OrdinalIgnoreCase));
    }
}
