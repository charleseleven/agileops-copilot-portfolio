using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;
using GmudAutomation.Core.Pipeline.Reporting;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 11 — acompanha o build disparado pelo push de cada release (premissa 6: trigger em <c>release/*</c>;
/// o fluxo não dispara builds), grava buildId, status e a nova tag <c>&lt;buildId&gt;-&lt;release&gt;-&lt;commitSha&gt;</c>
/// e gera <c>gmuds/&lt;id&gt;/infra.md</c>.
/// </summary>
public sealed class TrackBuildsStep : IGmudStep
{
    private readonly IAzureReposClient _repos;
    private readonly GmudConfig _config;
    private readonly GmudNamingConventions _naming;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public TrackBuildsStep(IAzureReposClient repos, GmudConfig config, GmudNamingConventions naming, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _delay = delay ?? Task.Delay;
    }

    public string Key => GmudStepKeys.TrackBuilds;
    public string Name => "Acompanhar o build e coletar o buildId";
    public bool WritesRemote => false;
    public bool SupportsDryRun => false;

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        var tracked = new List<(string Repository, string Environment, RepositoryEntry Entry, string Release)>();
        foreach (var (repositoryName, entry) in manifest.Repositories)
        {
            if (entry.Releases.Count == 0)
            {
                throw new InvalidOperationException($"[{repositoryName}] sem branches de release no manifesto — rode a etapa 10.");
            }

            if (!GmudStepSupport.GetRepository(_config, repositoryName).EffectiveHasBuild)
            {
                continue;
            }

            tracked.AddRange(entry.Releases.Select(r => (repositoryName, r.Key, entry, r.Value)));
        }

        var deadline = context.Clock.GetUtcNow().AddMinutes(_config.Build.TimeoutMinutes);
        while (true)
        {
            foreach (var (repositoryName, environment, entry, release) in tracked)
            {
                if (entry.Builds.TryGetValue(environment, out var known) && known.Status == "completed")
                {
                    continue;
                }

                var builds = await _repos.ListBuildsForBranchAsync(release, cancellationToken: cancellationToken);
                var build = builds.FirstOrDefault(b => string.Equals(b.SourceVersion, entry.GmudCommit, StringComparison.OrdinalIgnoreCase));
                if (build is not null)
                {
                    entry.Builds[environment] = ToEntry(manifest, repositoryName, release, entry.GmudCommit!, build);
                }
            }

            var pending = tracked.Where(t => !t.Entry.Builds.TryGetValue(t.Environment, out var b) || b.Status != "completed").ToList();
            if (pending.Count == 0 || context.Clock.GetUtcNow() >= deadline)
            {
                break;
            }

            context.Logger.Info($"Aguardando {pending.Count} build(s): {string.Join(", ", pending.Select(p => p.Release + " em " + p.Repository))}.", context.StepNumber);
            await _delay(TimeSpan.FromSeconds(_config.Build.PollIntervalSeconds), cancellationToken);
        }

        var reportPath = Path.Combine(context.GmudDirectory, InfraReportFormatter.FileName);
        await File.WriteAllTextAsync(reportPath, InfraReportFormatter.Format(manifest, _config, _naming), cancellationToken);
        context.Logger.Info($"Resumo para a infra gerado em {context.GmudDirectoryDisplay}/{InfraReportFormatter.FileName}.", context.StepNumber);

        var failed = tracked.Where(t => t.Entry.Builds.TryGetValue(t.Environment, out var b) && b.Status == "completed" && b.Result != "succeeded").ToList();
        if (failed.Count > 0)
        {
            throw new InvalidOperationException(
                "Build(s) sem sucesso: " + string.Join(", ", failed.Select(f => $"{f.Release} em {f.Repository} ({f.Entry.Builds[f.Environment].Result})")) + ".");
        }

        var stillPending = tracked.Where(t => !t.Entry.Builds.TryGetValue(t.Environment, out var b) || b.Status != "completed").ToList();
        if (stillPending.Count > 0)
        {
            throw new GmudBlockedException(
                $"Build(s) ainda não concluído(s) em {_config.Build.TimeoutMinutes} min: {string.Join(", ", stillPending.Select(p => p.Release + " em " + p.Repository))}. " +
                $"Rode --from-step {context.StepNumber} para continuar acompanhando.");
        }

        return manifest;
    }

    private BuildEntry ToEntry(GmudManifest manifest, string repositoryName, string release, string commit, BuildInfo build)
    {
        var tag = GmudNamingConventions.ContainerTag(build.Id, _naming.ReleaseNameFromBranch(release), commit);
        var repository = GmudStepSupport.GetRepository(_config, repositoryName);
        var registryImage = manifest.Environments.TryGetValue(_naming.ProductionEnvironment, out var prd)
            && prd.TryGetValue(repository.EffectiveKey, out var app)
            && GmudNamingConventions.TryParseContainerImage(app.Image, out var current)
                ? current.Repository
                : null;

        return new BuildEntry
        {
            BuildId = build.Id,
            Status = build.Status,
            Result = build.Result,
            Pipeline = build.DefinitionName,
            Branch = release,
            CommitSha = commit,
            NewTag = tag,
            Image = registryImage is null ? null : $"{registryImage}:{tag}"
        };
    }
}
