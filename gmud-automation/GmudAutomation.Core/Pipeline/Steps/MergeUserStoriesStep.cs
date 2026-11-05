using System.Text;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Analysis;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 8 — em cada repositório, mescla <c>feature/us_&lt;US&gt;</c> em <c>feature/us_&lt;GMUD&gt;</c> na ordem da Descrição.
/// O merge direto só acontece quando tudo o que a feature traz pertence à user story; se ela traz commits de
/// outra origem (ex: saiu de develop) ou o merge conflita, o item vai para a conciliação da etapa 9.
/// O Database não passa por aqui: usa só a branch do desenvolvedor (ver <see cref="GmudManifest.Database"/>).
/// </summary>
public sealed class MergeUserStoriesStep : IGmudStep
{
    private readonly IAzureReposClient _repos;
    private readonly GmudNamingConventions _naming;

    public MergeUserStoriesStep(IAzureReposClient repos, GmudNamingConventions naming)
    {
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
    }

    public string Key => GmudStepKeys.MergeUserStories;
    public string Name => "Fazer merge das branches das user stories";
    public bool WritesRemote => true;
    public bool SupportsDryRun => true;

    public string DescribePendingWrites(GmudManifest manifest)
    {
        var summary = new StringBuilder();
        foreach (var (repository, entry) in manifest.Repositories)
        {
            foreach (var userStory in GmudStepSupport.UserStoriesAffecting(manifest, repository))
            {
                summary.AppendLine($"[{repository}] merge {userStory.Branch} → {entry.GmudBranch}");
            }
        }

        return summary.ToString();
    }

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        var blockers = new List<string>();

        foreach (var (repositoryName, entry) in manifest.Repositories)
        {
            entry.Blocker = null;

            var head = await GetGmudHeadAsync(repositoryName, entry, context, cancellationToken);

            foreach (var userStory in GmudStepSupport.UserStoriesAffecting(manifest, repositoryName))
            {
                var merge = entry.Merges.FirstOrDefault(m => m.UserStoryId == userStory.Id);
                if (merge is null)
                {
                    merge = new MergeEntry { Source = userStory.Branch ?? _naming.ItemBranch(userStory.Id), UserStoryId = userStory.Id };
                    entry.Merges.Add(merge);
                }

                // Idempotência (cenário 9): merges já aplicados ou entregues à conciliação não são refeitos.
                if (!context.DryRun && (MergeStatus.IsFinal(merge.Status)
                    || merge.Status is MergeStatus.NeedsReconciliation or MergeStatus.Conflict or MergeStatus.AwaitingApproval))
                {
                    continue;
                }

                var feature = await _repos.GetBranchAsync(repositoryName, merge.Source, cancellationToken);
                if (feature is null)
                {
                    merge.Status = MergeStatus.MissingBranch;
                    entry.Blocker = $"a branch {merge.Source} não existe em {repositoryName}";
                    blockers.Add($"[{repositoryName}] {entry.Blocker} — crie/renomeie a branch da user story #{userStory.Id} e rode --from-step {context.StepNumber}.");
                    break;
                }

                merge.SourceCommit = feature.ObjectId;
                head = await MergeAsync(manifest, repositoryName, entry, userStory, merge, feature, head, context, cancellationToken);
            }

            entry.GmudCommit = head;
        }

        if (blockers.Count > 0)
        {
            throw new GmudBlockedException(string.Join(Environment.NewLine, blockers));
        }

        return manifest;
    }

    private async Task<string> GetGmudHeadAsync(string repositoryName, RepositoryEntry entry, GmudStepContext context, CancellationToken cancellationToken)
    {
        if (context.DryRun)
        {
            return entry.GmudCommit ?? throw new InvalidOperationException($"[{repositoryName}] sem commit planejado da branch da GMUD — rode a etapa 7.");
        }

        var branch = await _repos.GetBranchAsync(repositoryName, entry.GmudBranch!, cancellationToken);
        return branch?.ObjectId ?? throw new InvalidOperationException($"[{repositoryName}] {entry.GmudBranch} não existe — rode a etapa 7.");
    }

    private async Task<string> MergeAsync(
        GmudManifest manifest, string repositoryName, RepositoryEntry entry, UserStoryEntry userStory, MergeEntry merge, GitBranchInfo feature,
        string head, GmudStepContext context, CancellationToken cancellationToken)
    {
        var ahead = await _repos.GetCommitsAheadAsync(repositoryName, feature.ObjectId, head, cancellationToken);
        if (ahead.Count == 0)
        {
            merge.Status = MergeStatus.AlreadyContained;
            context.Logger.Info($"[{repositoryName}] {merge.Source} já está contida em {entry.GmudBranch}.", context.StepNumber);
            return head;
        }

        var (userStoryCommits, outOfScope) = UserStoryCommitClassifier.Split(
            ahead, GmudStepSupport.WorkItemIds(userStory), GmudStepSupport.LinkedCommits(userStory, repositoryName));

        merge.UserStoryCommits = userStoryCommits.Select(c => c.CommitId).ToList();
        merge.OutOfScopeCommits = outOfScope.Select(c => c.CommitId).ToList();
        merge.UserStoryFiles = await CollectFilesAsync(repositoryName, userStoryCommits, cancellationToken);

        if (outOfScope.Count > 0)
        {
            merge.Status = MergeStatus.NeedsReconciliation;
            var message = $"[{repositoryName}] {merge.Source} traz {outOfScope.Count} commit(s) fora da user story #{userStory.Id} — vai para a conciliação (etapa 9).";
            manifest.AddWarning(message);
            if (context.DryRun)
            {
                manifest.Plan.Add(message);
            }

            return head;
        }

        if (context.DryRun)
        {
            // Dry-run não calcula o merge no servidor: estima o risco de conflito pelos arquivos tocados dos dois lados.
            var mergeBase = await _repos.GetMergeBaseAsync(repositoryName, feature.ObjectId, head, cancellationToken);
            var changedInProduction = mergeBase is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : (await _repos.GetDiffAsync(repositoryName, mergeBase, head, cancellationToken)).Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
            var risk = merge.UserStoryFiles.Where(changedInProduction.Contains).ToList();

            merge.Status = risk.Count == 0 ? MergeStatus.SimulatedOk : MergeStatus.SimulatedConflictRisk;
            merge.Conflicts = risk.Select(p => new ConflictEntry { Path = p, Resolution = "risco", Reason = "alterado na produção e na user story" }).ToList();
            manifest.Plan.Add(
                $"[{repositoryName}] mesclaria {merge.Source} ({userStoryCommits.Count} commit(s), {merge.UserStoryFiles.Count} arquivo(s))" +
                (risk.Count == 0 ? "." : $" — risco de conflito em: {string.Join(", ", risk)}."));
            return head;
        }

        var result = await _repos.MergeCommitsAsync(
            repositoryName, head, feature.ObjectId, _naming.MergeCommitMessage(manifest.Gmud.Id, merge.Source), cancellationToken);

        if (result.HasConflicts)
        {
            merge.Status = MergeStatus.Conflict;
            merge.Conflicts = result.ConflictingFiles.Select(p => new ConflictEntry { Path = p, Resolution = "pendente", Reason = "conflito no merge" }).ToList();
            manifest.AddWarning($"[{repositoryName}] conflito ao mesclar {merge.Source} — vai para a conciliação (etapa 9).");
            return head;
        }

        await _repos.UpdateBranchAsync(repositoryName, entry.GmudBranch!, head, result.MergeCommitId!, cancellationToken);
        merge.Status = MergeStatus.Ok;
        merge.ResultCommit = result.MergeCommitId;
        context.Logger.Info($"[{repositoryName}] {merge.Source} mesclada ({GmudStepSupport.Short(result.MergeCommitId)}).", context.StepNumber);
        return result.MergeCommitId!;
    }

    private async Task<List<string>> CollectFilesAsync(string repositoryName, IReadOnlyList<GitCommitInfo> commits, CancellationToken cancellationToken)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var commit in commits)
        {
            foreach (var change in await _repos.GetCommitChangesAsync(repositoryName, commit.CommitId, cancellationToken))
            {
                files.Add(change.Path);
            }
        }

        return files.ToList();
    }
}
