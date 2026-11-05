using System.Text;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 10 — a partir de <c>feature/us_&lt;GMUD&gt;</c> conciliada, cria <c>release/GMUD_PROD_&lt;CLIENTE&gt;_&lt;DATA&gt;</c> e
/// <c>release/GMUD_GMUD_&lt;CLIENTE&gt;_&lt;DATA&gt;</c> (premissa 4: mesmo conteúdo). Release já existente nunca é
/// sobrescrita: se aponta para o mesmo commit, é reaproveitada; se não, o fluxo para.
/// </summary>
public sealed class CreateReleaseBranchesStep : IGmudStep, IGmudConfirmationCheckpoint
{
    private readonly IAzureReposClient _repos;
    private readonly GmudNamingConventions _naming;

    public CreateReleaseBranchesStep(IAzureReposClient repos, GmudNamingConventions naming)
    {
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
    }

    public string Key => GmudStepKeys.CreateReleaseBranches;
    public string Name => "Criar as branches de release";
    public bool WritesRemote => true;
    public bool SupportsDryRun => false;

    public string DescribePendingWrites(GmudManifest manifest)
    {
        var summary = new StringBuilder();
        foreach (var (repository, entry) in manifest.Repositories)
        {
            var (production, gmud) = ReleaseNames(manifest);
            summary.AppendLine(
                $"[{repository}] {entry.GmudBranch} @ {GmudStepSupport.Short(entry.GmudCommit)} → {production} e {gmud} " +
                $"({entry.ChangedFiles?.Count.ToString() ?? "?"} arquivo(s) diferentes da produção)");
            foreach (var file in entry.ChangedFiles ?? [])
            {
                summary.AppendLine($"    {file}");
            }
        }

        return summary.ToString();
    }

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        var notReady = manifest.Repositories
            .Where(r => r.Value.Blocker is not null || r.Value.Merges.Any(m => !MergeStatus.IsFinal(m.Status)))
            .Select(r => $"[{r.Key}] {r.Value.Blocker ?? "há merges não concluídos: " + string.Join(", ", r.Value.Merges.Where(m => !MergeStatus.IsFinal(m.Status)).Select(m => $"{m.Source} ({m.Status})"))}")
            .ToList();
        if (notReady.Count > 0)
        {
            throw new GmudBlockedException("Releases não criadas — conclua a conciliação antes:" + Environment.NewLine + string.Join(Environment.NewLine, notReady));
        }

        var (productionRelease, gmudRelease) = ReleaseNames(manifest);
        var toCreate = new List<(string Repository, string Branch, string Commit)>();
        var conflicts = new List<string>();

        // Primeiro confere tudo, depois cria: uma release recusada não deixa a GMUD com metade das releases publicadas.
        foreach (var (repositoryName, entry) in manifest.Repositories)
        {
            var head = await _repos.GetBranchAsync(repositoryName, entry.GmudBranch!, cancellationToken)
                ?? throw new InvalidOperationException($"[{repositoryName}] {entry.GmudBranch} não existe — rode as etapas 7 a 9.");
            entry.GmudCommit = head.ObjectId;

            foreach (var (environment, releaseName) in new[] { (_naming.ProductionEnvironment, productionRelease), (_naming.GmudEnvironment, gmudRelease) })
            {
                var existing = await _repos.GetBranchAsync(repositoryName, releaseName, cancellationToken);
                if (existing is null)
                {
                    toCreate.Add((repositoryName, releaseName, head.ObjectId));
                }
                else if (!string.Equals(existing.ObjectId, head.ObjectId, StringComparison.OrdinalIgnoreCase))
                {
                    conflicts.Add(
                        $"[{repositoryName}] {releaseName} já existe em {GmudStepSupport.Short(existing.ObjectId)}, diferente de {entry.GmudBranch} " +
                        $"({GmudStepSupport.Short(head.ObjectId)}) — não é sobrescrita.");
                }
                else
                {
                    context.Logger.Info($"[{repositoryName}] {releaseName} já existe no mesmo commit — reaproveitada.", context.StepNumber);
                }

                entry.Releases[environment] = releaseName;
            }
        }

        if (conflicts.Count > 0)
        {
            throw new GmudBlockedException(string.Join(Environment.NewLine, conflicts));
        }

        foreach (var (repository, branch, commit) in toCreate)
        {
            await _repos.CreateBranchFromCommitAsync(repository, branch, commit, cancellationToken);
            context.Logger.Info($"[{repository}] {branch} criada em {GmudStepSupport.Short(commit)}.", context.StepNumber);
        }

        return manifest;
    }

    private (string Production, string Gmud) ReleaseNames(GmudManifest manifest)
    {
        var date = GmudStepSupport.GetDate(manifest);
        return (_naming.ProductionRelease(manifest.Gmud.Client, date), _naming.GmudRelease(manifest.Gmud.Client, date));
    }
}
