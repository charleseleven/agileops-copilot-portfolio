using System.Text;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 7 — cria <c>feature/us_&lt;idDaGMUD&gt;</c> a partir da release vigente em produção, só nos repositórios
/// afetados. Se o HEAD da release divergir do commit do container, parte do commit do container (o que está
/// de fato em produção) e registra aviso. Branch já existente é reaproveitada, nunca sobrescrita.
/// </summary>
public sealed class CreateGmudBranchStep : IGmudStep
{
    private readonly IAzureReposClient _repos;

    public CreateGmudBranchStep(IAzureReposClient repos)
    {
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
    }

    public string Key => GmudStepKeys.CreateGmudBranch;
    public string Name => "Criar a branch da GMUD em cada repositório afetado";
    public bool WritesRemote => true;
    public bool SupportsDryRun => true;

    public string DescribePendingWrites(GmudManifest manifest)
    {
        var summary = new StringBuilder();
        foreach (var (repository, entry) in manifest.Repositories)
        {
            summary.AppendLine($"[{repository}] criar {entry.GmudBranch} a partir de {entry.BaseBranch} @ {GmudStepSupport.Short(entry.BaseCommit)}");
            foreach (var merge in entry.Merges)
            {
                summary.AppendLine($"    depois: merge {merge.Source} ({merge.UserStoryFiles.Count} arquivo(s) da user story, plano: {merge.Status})");
            }
        }

        return summary.ToString();
    }

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        foreach (var (repositoryName, entry) in manifest.Repositories)
        {
            if (entry.GmudBranch is null || (entry.BaseBranch is null && entry.BaseCommit is null))
            {
                throw new InvalidOperationException($"[{repositoryName}] sem branch de origem no manifesto — rode a etapa 6.");
            }

            var existing = await _repos.GetBranchAsync(repositoryName, entry.GmudBranch, cancellationToken);
            if (existing is not null)
            {
                // Reexecução (cenário 9): a branch já existe — reaproveita, sem recriar nem mover.
                entry.GmudCommit = existing.ObjectId;
                context.Logger.Info($"[{repositoryName}] {entry.GmudBranch} já existe ({GmudStepSupport.Short(existing.ObjectId)}) — reaproveitada.", context.StepNumber);
                continue;
            }

            var startCommit = await ResolveStartCommitAsync(manifest, repositoryName, entry, cancellationToken);

            if (context.DryRun)
            {
                manifest.Plan.Add($"[{repositoryName}] criaria {entry.GmudBranch} a partir de {entry.BaseBranch} @ {GmudStepSupport.Short(startCommit)}.");
            }
            else
            {
                await _repos.CreateBranchFromCommitAsync(repositoryName, entry.GmudBranch, startCommit, cancellationToken);
                context.Logger.Info($"[{repositoryName}] {entry.GmudBranch} criada em {GmudStepSupport.Short(startCommit)}.", context.StepNumber);
            }

            entry.GmudCommit = startCommit;
        }

        return manifest;
    }

    private async Task<string> ResolveStartCommitAsync(GmudManifest manifest, string repositoryName, RepositoryEntry entry, CancellationToken cancellationToken)
    {
        var baseBranch = entry.BaseBranch is null ? null : await _repos.GetBranchAsync(repositoryName, entry.BaseBranch, cancellationToken);

        if (entry.BaseCommit is null)
        {
            return baseBranch?.ObjectId
                ?? throw new InvalidOperationException($"[{repositoryName}] a branch de origem \"{entry.BaseBranch}\" não existe.");
        }

        if (baseBranch is null)
        {
            manifest.AddWarning($"[{repositoryName}] a branch \"{entry.BaseBranch}\" não existe — {entry.GmudBranch} parte do commit do container {GmudStepSupport.Short(entry.BaseCommit)}.");
        }
        else if (!string.Equals(baseBranch.ObjectId, entry.BaseCommit, StringComparison.OrdinalIgnoreCase))
        {
            manifest.AddWarning(
                $"[{repositoryName}] o HEAD de \"{entry.BaseBranch}\" ({GmudStepSupport.Short(baseBranch.ObjectId)}) diverge do commit do container " +
                $"({GmudStepSupport.Short(entry.BaseCommit)}) — {entry.GmudBranch} parte do commit do container (o que está em produção).");
        }

        return entry.BaseCommit;
    }
}
