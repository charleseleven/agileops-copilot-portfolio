using System.Text;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Analysis;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 9 — concilia os merges que não puderam ser feitos direto (conflito ou commits fora da user story).
/// A base é a release de produção; da feature entram só os arquivos alterados pelos commits da user story e
/// inequívocos (<see cref="ReconciliationPlanner"/>). Cada resolução gera relatório por arquivo; conflitos
/// ambíguos param o fluxo para aprovação humana (campo "decisao" no manifesto: feature | release | manual).
/// Ao final, confere que o diff contra a produção contém apenas arquivos das user stories (cenário 7).
/// </summary>
public sealed class ReconcileConflictsStep : IGmudStep
{
    public const string DecisionFeature = "feature";
    public const string DecisionRelease = "release";
    public const string DecisionManual = "manual";

    private readonly IAzureReposClient _repos;
    private readonly GmudNamingConventions _naming;

    public ReconcileConflictsStep(IAzureReposClient repos, GmudNamingConventions naming)
    {
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
    }

    public string Key => GmudStepKeys.ReconcileConflicts;
    public string Name => "Corrigir conflitos de merge";
    public bool WritesRemote => true;
    public bool SupportsDryRun => true;

    public string DescribePendingWrites(GmudManifest manifest)
    {
        var summary = new StringBuilder();
        foreach (var (repository, entry) in manifest.Repositories)
        {
            foreach (var merge in entry.Merges.Where(NeedsReconciliation))
            {
                var take = merge.Conflicts.Count(c => c.Resolution == ConflictResolution.TakeFeature || c.Decision == DecisionFeature);
                summary.AppendLine($"[{repository}] conciliar {merge.Source}: {take} arquivo(s) com a versão da feature (commit em {entry.GmudBranch})");
            }
        }

        return summary.ToString();
    }

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        var blockers = new List<string>();

        foreach (var (repositoryName, entry) in manifest.Repositories)
        {
            if (entry.Blocker is not null)
            {
                blockers.Add($"[{repositoryName}] bloqueado na etapa anterior: {entry.Blocker}.");
                continue;
            }

            var head = context.DryRun
                ? entry.GmudCommit ?? throw new InvalidOperationException($"[{repositoryName}] sem commit planejado — rode as etapas 7 e 8.")
                : (await _repos.GetBranchAsync(repositoryName, entry.GmudBranch!, cancellationToken))?.ObjectId
                    ?? throw new InvalidOperationException($"[{repositoryName}] {entry.GmudBranch} não existe — rode a etapa 7.");

            foreach (var merge in entry.Merges.Where(NeedsReconciliation))
            {
                var userStory = manifest.UserStories.FirstOrDefault(us => us.Id == merge.UserStoryId)
                    ?? throw new InvalidOperationException($"User story #{merge.UserStoryId} não está mais no manifesto — rode a etapa 2.");

                head = await ReconcileAsync(manifest, repositoryName, entry, userStory, merge, head, context, blockers, cancellationToken);
            }

            if (!context.DryRun && entry.Merges.All(m => MergeStatus.IsFinal(m.Status)))
            {
                await VerifyScopeAsync(repositoryName, entry, head, blockers, cancellationToken);
            }

            entry.GmudCommit = head;
        }

        if (blockers.Count > 0)
        {
            throw new GmudBlockedException(string.Join(Environment.NewLine, blockers) + Environment.NewLine +
                "Nada foi publicado em release. Registre a decisão (\"decisao\": feature | release | manual) nos conflitos do manifest.json " +
                $"e rode --from-step {context.StepNumber}.");
        }

        return manifest;
    }

    private static bool NeedsReconciliation(MergeEntry merge) =>
        merge.Status is MergeStatus.NeedsReconciliation or MergeStatus.Conflict or MergeStatus.AwaitingApproval or MergeStatus.SimulatedReconcilable;

    private async Task<string> ReconcileAsync(
        GmudManifest manifest, string repositoryName, RepositoryEntry entry, UserStoryEntry userStory, MergeEntry merge, string head,
        GmudStepContext context, List<string> blockers, CancellationToken cancellationToken)
    {
        var feature = await _repos.GetBranchAsync(repositoryName, merge.Source, cancellationToken)
            ?? throw new InvalidOperationException($"[{repositoryName}] {merge.Source} não existe mais.");

        var mergeBase = await _repos.GetMergeBaseAsync(repositoryName, feature.ObjectId, head, cancellationToken) ?? entry.BaseCommit
            ?? throw new InvalidOperationException($"[{repositoryName}] não foi possível achar o ponto em que {merge.Source} saiu da base.");

        var ahead = await _repos.GetCommitsAheadAsync(repositoryName, feature.ObjectId, head, cancellationToken);
        var (userStoryCommits, outOfScope) = UserStoryCommitClassifier.Split(
            ahead, GmudStepSupport.WorkItemIds(userStory), GmudStepSupport.LinkedCommits(userStory, repositoryName));

        var userStoryFiles = await CollectFilesAsync(repositoryName, userStoryCommits, cancellationToken);
        var outOfScopeFiles = await CollectFilesAsync(repositoryName, outOfScope, cancellationToken);
        var changedInProduction = (await _repos.GetDiffAsync(repositoryName, mergeBase, head, cancellationToken))
            .Select(c => c.Path).ToHashSet(StringComparer.Ordinal);
        var featureNet = (await _repos.GetDiffAsync(repositoryName, mergeBase, feature.ObjectId, cancellationToken))
            .GroupBy(c => c.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().ChangeType, StringComparer.Ordinal);

        var previousDecisions = merge.Conflicts
            .Where(c => !string.IsNullOrWhiteSpace(c.Decision))
            .ToDictionary(c => c.Path, c => c.Decision!.Trim().ToLowerInvariant(), StringComparer.Ordinal);

        var plans = ReconciliationPlanner.Plan(userStoryFiles, outOfScopeFiles, changedInProduction, featureNet)
            .Select(plan => ApplyDecision(plan, previousDecisions, featureNet))
            .ToList();

        merge.SourceCommit = feature.ObjectId;
        merge.UserStoryCommits = userStoryCommits.Select(c => c.CommitId).ToList();
        merge.OutOfScopeCommits = outOfScope.Select(c => c.CommitId).ToList();
        merge.UserStoryFiles = userStoryFiles.Order(StringComparer.Ordinal).ToList();
        merge.Conflicts = plans.Select(p => new ConflictEntry
        {
            Path = p.Path,
            Resolution = p.Resolution,
            Reason = p.Reason,
            Decision = previousDecisions.GetValueOrDefault(p.Path)
        }).ToList();
        merge.ReportPath = await WriteReportAsync(manifest, repositoryName, merge, context, cancellationToken);

        var pendingApproval = plans.Where(p => p.Resolution == ConflictResolution.Ambiguous).ToList();
        if (pendingApproval.Count > 0)
        {
            merge.Status = MergeStatus.AwaitingApproval;
            blockers.Add(
                $"[{repositoryName}] {merge.Source}: {pendingApproval.Count} arquivo(s) ambíguo(s) aguardando aprovação — {string.Join(", ", pendingApproval.Select(p => p.Path))} " +
                $"(relatório: {merge.ReportPath}).");
            return head;
        }

        var toApply = plans.Where(p => p.PushChangeType is not null).ToList();
        if (context.DryRun)
        {
            merge.Status = MergeStatus.SimulatedReconcilable;
            manifest.Plan.Add(
                $"[{repositoryName}] conciliaria {merge.Source}: {toApply.Count} arquivo(s) da user story aplicados, " +
                $"{plans.Count(p => p.Resolution == ConflictResolution.Discarded)} fora do escopo descartados (relatório: {merge.ReportPath}).");
            return head;
        }

        if (toApply.Count > 0)
        {
            var changes = new List<GitPushChange>();
            foreach (var plan in toApply)
            {
                var content = plan.PushChangeType == "delete" ? null : await _repos.GetFileContentAsync(repositoryName, plan.Path, feature.ObjectId, cancellationToken);
                changes.Add(new GitPushChange(plan.Path, plan.PushChangeType!, content));
            }

            head = await _repos.PushChangesAsync(
                repositoryName, entry.GmudBranch!, head, _naming.ReconciliationCommitMessage(manifest.Gmud.Id, merge.Source), changes, cancellationToken);
            merge.ResultCommit = head;
        }

        merge.Status = MergeStatus.Reconciled;
        context.Logger.Info($"[{repositoryName}] {merge.Source} conciliada: {toApply.Count} arquivo(s) aplicados.", context.StepNumber);
        return head;
    }

    /// <summary>Decisão humana registrada no manifesto prevalece sobre a ambiguidade detectada.</summary>
    private static FilePlan ApplyDecision(FilePlan plan, IReadOnlyDictionary<string, string> decisions, IReadOnlyDictionary<string, string> featureNet)
    {
        if (plan.Resolution != ConflictResolution.Ambiguous || !decisions.TryGetValue(plan.Path, out var decision))
        {
            return plan;
        }

        return decision switch
        {
            DecisionFeature when featureNet.TryGetValue(plan.Path, out var change) =>
                plan with { Resolution = ConflictResolution.TakeFeature, Reason = "aprovado pelo operador: versão da feature", PushChangeType = ReconciliationPlanner.ToPushChangeType(change) },
            DecisionRelease => plan with { Resolution = ConflictResolution.KeptProduction, Reason = "aprovado pelo operador: mantida a versão de produção", PushChangeType = null },
            DecisionManual => plan with { Resolution = ConflictResolution.ResolvedManually, Reason = "resolvido pelo operador direto na branch da GMUD", PushChangeType = null },
            _ => plan
        };
    }

    private async Task<HashSet<string>> CollectFilesAsync(string repositoryName, IReadOnlyList<GitCommitInfo> commits, CancellationToken cancellationToken)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var commit in commits)
        {
            foreach (var change in await _repos.GetCommitChangesAsync(repositoryName, commit.CommitId, cancellationToken))
            {
                files.Add(change.Path);
            }
        }

        return files;
    }

    /// <summary>Cenário 7: o diff contra a produção só pode conter arquivos das user stories (e os scripts versionados).</summary>
    private async Task VerifyScopeAsync(string repositoryName, RepositoryEntry entry, string head, List<string> blockers, CancellationToken cancellationToken)
    {
        if (entry.BaseCommit is null)
        {
            return;
        }

        var changed = (await _repos.GetDiffAsync(repositoryName, entry.BaseCommit, head, cancellationToken)).Select(c => c.Path).ToList();
        entry.ChangedFiles = changed.Order(StringComparer.Ordinal).ToList();

        var allowed = entry.Merges
            .SelectMany(m => m.Status == MergeStatus.Reconciled
                ? m.Conflicts.Where(c => c.Resolution is ConflictResolution.TakeFeature or ConflictResolution.ResolvedManually).Select(c => c.Path)
                : m.UserStoryFiles)
            .ToHashSet(StringComparer.Ordinal);

        var outside = changed.Where(p => !allowed.Contains(p)).ToList();

        if (outside.Count > 0)
        {
            entry.Blocker = "diff contra a produção contém arquivos fora das user stories";
            blockers.Add($"[{repositoryName}] o diff de {entry.GmudBranch} contra a produção contém arquivo(s) fora das user stories: {string.Join(", ", outside)}.");
        }
    }

    private static async Task<string> WriteReportAsync(
        GmudManifest manifest, string repositoryName, MergeEntry merge, GmudStepContext context, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(context.GmudDirectory, "conciliacao");
        Directory.CreateDirectory(directory);
        var fileName = $"{repositoryName}_us_{merge.UserStoryId}.md";

        var report = new StringBuilder()
            .AppendLine($"# Conciliação — GMUD #{manifest.Gmud.Id} — {repositoryName} — {merge.Source}")
            .AppendLine()
            .AppendLine($"- Commits da user story: {merge.UserStoryCommits.Count} ({string.Join(", ", merge.UserStoryCommits.Select(GmudStepSupport.Short))})")
            .AppendLine($"- Commits fora do escopo (não levados): {merge.OutOfScopeCommits.Count} ({string.Join(", ", merge.OutOfScopeCommits.Select(GmudStepSupport.Short))})")
            .AppendLine($"- Modo: {(context.DryRun ? "dry-run (nada foi escrito)" : "execução real")}")
            .AppendLine()
            .AppendLine("| Arquivo | Resolução | Motivo | Decisão do operador |")
            .AppendLine("|---|---|---|---|");

        foreach (var conflict in merge.Conflicts)
        {
            report.AppendLine($"| `{conflict.Path}` | {conflict.Resolution} | {conflict.Reason} | {conflict.Decision ?? "—"} |");
        }

        await File.WriteAllTextAsync(Path.Combine(directory, fileName), report.ToString(), cancellationToken);
        return $"{context.GmudDirectoryDisplay}/conciliacao/{fileName}";
    }
}
