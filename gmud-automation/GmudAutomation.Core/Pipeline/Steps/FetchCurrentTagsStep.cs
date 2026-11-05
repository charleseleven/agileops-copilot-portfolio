using GmudAutomation.Core.Client;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 6 — lê a imagem configurada nos App Services do cliente (produção e GMUD) e decompõe a tag em buildId,
/// release e commitSha. A release de produção vira a base da GMUD (premissa 7: a de GMUD só é registrada).
/// No Database (sem container) a referência é a <c>feature/us_&lt;US&gt;</c> do desenvolvedor: procurada em toda user
/// story, com link e commits à frente da branch padrão; bloqueia se o card cita o banco e ela não existe.
/// </summary>
public sealed class FetchCurrentTagsStep : IGmudStep
{
    private readonly IAppServiceClient _appServices;
    private readonly IAzureReposClient _repos;
    private readonly GmudConfig _config;
    private readonly GmudNamingConventions _naming;

    public FetchCurrentTagsStep(IAppServiceClient appServices, IAzureReposClient repos, GmudConfig config, GmudNamingConventions naming)
    {
        _appServices = appServices ?? throw new ArgumentNullException(nameof(appServices));
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
    }

    public string Key => GmudStepKeys.FetchCurrentTags;
    public string Name => "Buscar a tag vigente nos App Services";
    public bool WritesRemote => false;
    public bool SupportsDryRun => true;

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        GmudStepSupport.RequireResolvedGmud(manifest);
        if (manifest.Repositories.Count == 0 && manifest.Database is null)
        {
            throw new InvalidOperationException("Nenhum repositório no manifesto — rode as etapas 4 e 5.");
        }

        var client = GmudStepSupport.GetClient(manifest);
        var production = _naming.ProductionEnvironment;
        var blockers = new List<string>();

        if (manifest.Database is not null)
        {
            await CheckDatabaseBranchesAsync(manifest, manifest.Database, context, blockers, cancellationToken);
            if (manifest.Repositories.Count == 0 && !manifest.Database.Branches.Any(b => DatabaseBranchStatus.AffectsDatabase(b.Status)))
            {
                throw new GmudBlockedException(
                    $"Nenhuma alteração encontrada: os cards não citam repositório de código e nenhuma user story tem feature no " +
                    $"{manifest.Database.Repository} nem script anexado. Confira os cards e rode --from-step 4.");
            }
        }

        foreach (var (repositoryName, entry) in manifest.Repositories)
        {
            var repository = GmudStepSupport.GetRepository(_config, repositoryName);

            if (repository.AppService is null)
            {
                blockers.Add(
                    $"[{repositoryName}] não tem \"appService\" no gmud.config.json — sem a tag do container não há base de produção. " +
                    "Só o Database pode ficar sem App Service.");
                continue;
            }

            foreach (var environment in new[] { production, _naming.GmudEnvironment })
            {
                var appService = await ReadAppServiceAsync(manifest, repository, environment, client, context, blockers, cancellationToken);
                if (!manifest.Environments.TryGetValue(environment, out var byKey))
                {
                    byKey = new Dictionary<string, AppServiceEntry>(StringComparer.OrdinalIgnoreCase);
                    manifest.Environments[environment] = byKey;
                }

                byKey[repository.EffectiveKey] = appService;
            }

            var prd = manifest.Environments[production][repository.EffectiveKey];
            if (prd.Release is null)
            {
                continue;
            }

            entry.BaseBranch = _naming.SourceBranchFromRelease(prd.Release);
            entry.BaseCommit = prd.CommitSha;

            if (!prd.Release.Contains(client.ContainerName, StringComparison.OrdinalIgnoreCase))
            {
                manifest.AddWarning(
                    $"[{repositoryName}] a release em produção \"{prd.Release}\" não contém o cliente \"{client.ContainerName}\" — confira \"clients[].containerName\".");
            }

            if (manifest.Environments[_naming.GmudEnvironment].TryGetValue(repository.EffectiveKey, out var gmud)
                && gmud.CommitSha is not null && !string.Equals(gmud.CommitSha, prd.CommitSha, StringComparison.OrdinalIgnoreCase))
            {
                manifest.AddWarning(
                    $"[{repositoryName}] o App Service de GMUD está em \"{gmud.Release}\" ({GmudStepSupport.Short(gmud.CommitSha)}), diferente da produção — " +
                    "a base é sempre a produção (premissa 7).");
            }

            context.Logger.Info($"[{repositoryName}] produção em {prd.Release} (build {prd.BuildId}, commit {GmudStepSupport.Short(prd.CommitSha)}).", context.StepNumber);
        }

        if (blockers.Count > 0)
        {
            throw new GmudBlockedException(string.Join(Environment.NewLine, blockers));
        }

        return manifest;
    }

    private async Task<AppServiceEntry> ReadAppServiceAsync(
        GmudManifest manifest, RepositoryConfig repository, string environment, GmudClient client, GmudStepContext context,
        List<string> blockers, CancellationToken cancellationToken)
    {
        var siteName = _naming.GetAppServiceOverride(client.TitleName, repository.Name, environment)
            ?? _naming.AppServiceName(repository, environment, client);
        var entry = new AppServiceEntry { AppService = siteName, Repository = repository.Name };

        var image = await _appServices.GetContainerImageAsync(siteName, cancellationToken);
        if (image is null)
        {
            // Cenário 5: nome fora do padrão — lista candidatos e espera a escolha do operador; nunca adivinha.
            entry.Candidates = (await _appServices.FindSitesAsync(client.AppServiceName, cancellationToken)).ToList();
            var message =
                $"[{repository.Name}] App Service \"{siteName}\" ({environment}) não encontrado. Candidatos com \"{client.AppServiceName}\": " +
                $"{(entry.Candidates.Count == 0 ? "nenhum" : string.Join(", ", entry.Candidates))}. Defina " +
                $"clients[\"{client.TitleName}\"].appServiceOverrides[\"{repository.Name}\"][\"{environment}\"] no gmud.config.json e rode --from-step {context.StepNumber}.";
            AddProblem(manifest, environment, message, blockers);
            return entry;
        }

        entry.Image = image;
        if (!GmudNamingConventions.TryParseContainerImage(image, out var parsed))
        {
            AddProblem(manifest, environment,
                $"[{repository.Name}] a imagem de \"{siteName}\" ({image}) não segue o padrão <buildId>-<release>-<commitSha>.", blockers);
            return entry;
        }

        entry.BuildId = parsed.BuildId;
        entry.Release = parsed.ReleaseName;
        entry.CommitSha = parsed.CommitSha;
        return entry;
    }

    /// <summary>Só a produção é bloqueante (é a base); problema no App Service de GMUD vira aviso.</summary>
    private void AddProblem(GmudManifest manifest, string environment, string message, List<string> blockers)
    {
        if (string.Equals(environment, _naming.ProductionEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            blockers.Add(message);
        }
        else
        {
            manifest.AddWarning(message);
        }
    }

    /// <summary>
    /// Para cada user story, procura a <c>feature/us_&lt;US&gt;</c> do desenvolvedor no Database e conta os commits dela que
    /// não estão na branch padrão: feature com commit = houve alteração no banco, mesmo que o card não cite o Database.
    /// </summary>
    private async Task CheckDatabaseBranchesAsync(GmudManifest manifest, DatabaseEntry database, GmudStepContext context, List<string> blockers, CancellationToken cancellationToken)
    {
        var defaultBranchName = (await _repos.ListRepositoriesAsync(cancellationToken))
            .FirstOrDefault(r => string.Equals(r.Name, database.Repository, StringComparison.OrdinalIgnoreCase))?.DefaultBranch ?? "main";
        var defaultBranch = await _repos.GetBranchAsync(database.Repository, defaultBranchName, cancellationToken);

        foreach (var entry in database.Branches)
        {
            var branch = await _repos.GetBranchAsync(database.Repository, entry.Branch, cancellationToken);
            entry.Commit = branch?.ObjectId;
            entry.Url = branch is null ? null : _config.BranchUrl(database.Repository, entry.Branch);
            entry.BaseBranch = defaultBranch?.Name;
            entry.CommitsAhead = null;

            if (branch is null)
            {
                entry.Status = entry.Required ? DatabaseBranchStatus.Missing
                    : entry.HasScripts ? DatabaseBranchStatus.ScriptsOnly
                    : DatabaseBranchStatus.NotChanged;

                if (entry.Status == DatabaseBranchStatus.Missing)
                {
                    blockers.Add(
                        $"[{database.Repository}] a user story #{entry.UserStoryId} cita o banco no card, mas {entry.Branch} não existe — " +
                        $"o desenvolvedor precisa criá-la; depois rode --from-step {context.StepNumber}.");
                }

                continue;
            }

            if (defaultBranch is not null)
            {
                entry.CommitsAhead = (await _repos.GetCommitsAheadAsync(database.Repository, branch.ObjectId, defaultBranch.ObjectId, cancellationToken))
                    .Count(c => !c.IsMergeCommit);
            }

            entry.Status = entry.CommitsAhead is null or > 0 ? DatabaseBranchStatus.Changed : DatabaseBranchStatus.NoCommits;
            if (entry.Status == DatabaseBranchStatus.NoCommits)
            {
                manifest.AddWarning(
                    $"[{database.Repository}] {entry.Branch} existe, mas não tem commit à frente de {defaultBranch!.Name} " +
                    "(criada sem alteração ou já mesclada) — confirme com o desenvolvedor.");
            }
            else if (defaultBranch is null)
            {
                manifest.AddWarning($"[{database.Repository}] branch padrão \"{defaultBranchName}\" não encontrada — não foi possível contar os commits de {entry.Branch}.");
            }

            context.Logger.Info(
                $"[{database.Repository}] user story #{entry.UserStoryId}: {entry.Branch} ({GmudStepSupport.Short(branch.ObjectId)}, " +
                $"{entry.CommitsAhead?.ToString() ?? "?"} commit(s) à frente) — {entry.Url}",
                context.StepNumber);
        }
    }
}
