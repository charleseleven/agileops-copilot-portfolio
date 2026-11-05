using System.Net;
using System.Text;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Tests.Pipeline.Fakes;

/// <summary>Azure Boards em memória: work items com relações, comentários e anexos.</summary>
internal sealed class InMemoryBoards : IAzureDevOpsClient
{
    private readonly Dictionary<int, WorkItemDetails> _items = new();
    private readonly Dictionary<int, List<WorkItemComment>> _comments = new();
    private readonly Dictionary<string, byte[]> _attachments = new(StringComparer.OrdinalIgnoreCase);

    public void Add(int id, string type, string title, string? descriptionHtml = null, params WorkItemRelation[] relations) =>
        _items[id] = new WorkItemDetails(new WorkItemInfo(id, title, type, "Active", descriptionHtml, $"https://dev.azure.com/contoso/_apis/wit/workItems/{id}"), relations);

    public void AddComment(int id, string text)
    {
        if (!_comments.TryGetValue(id, out var list))
        {
            list = [];
            _comments[id] = list;
        }

        list.Add(new WorkItemComment(list.Count + 1, text, "dev", DateTimeOffset.UnixEpoch));
    }

    public WorkItemRelation AddAttachment(string name, string content)
    {
        var url = $"https://dev.azure.com/contoso/_apis/wit/attachments/{Guid.NewGuid()}";
        _attachments[url] = Encoding.UTF8.GetBytes(content);
        return new WorkItemRelation(WorkItemDetails.AttachmentRelation, url, name);
    }

    public static WorkItemRelation Child(int id) => new(WorkItemDetails.ChildRelation, $"https://dev.azure.com/contoso/_apis/wit/workItems/{id}", null);

    public static WorkItemRelation Related(int id) => new(WorkItemDetails.RelatedRelation, $"https://dev.azure.com/contoso/_apis/wit/workItems/{id}", null);

    public static WorkItemRelation CommitLink(string repositoryId, string commitId) =>
        new(WorkItemDetails.ArtifactRelation, $"vstfs:///Git/Commit/proj%2F{repositoryId}%2F{commitId}", "Fixed in Commit");

    public Task<WorkItemInfo> GetWorkItemAsync(int workItemId, CancellationToken cancellationToken = default) =>
        _items.TryGetValue(workItemId, out var item)
            ? Task.FromResult(item.Info)
            : throw new HttpRequestException("not found", null, HttpStatusCode.NotFound);

    public Task<WorkItemDetails> GetWorkItemDetailsAsync(int workItemId, CancellationToken cancellationToken = default) =>
        _items.TryGetValue(workItemId, out var item)
            ? Task.FromResult(item)
            : throw new HttpRequestException("not found", null, HttpStatusCode.NotFound);

    public Task<IReadOnlyList<WorkItemComment>> GetWorkItemCommentsAsync(int workItemId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkItemComment>>(_comments.TryGetValue(workItemId, out var list) ? list : []);

    public Task<byte[]> DownloadAttachmentAsync(string attachmentUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(_attachments[attachmentUrl]);

    public Task<IReadOnlyList<int>> GetChildWorkItemIdsAsync(int workItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> BranchExistsAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddWorkItemCommentAsync(int workItemId, string commentText, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string> CreateBranchAsync(string repositoryName, string sourceBranchName, string newBranchName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task MergeBranchesAsync(string repositoryName, string sourceBranchName, string targetBranchName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<PullRequestInfo> CreatePullRequestAsync(string repositoryName, string sourceBranchName, string targetBranchName, string title, string description, bool isDraft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task CompletePullRequestAsync(string repositoryName, int pullRequestId, string lastMergeSourceCommitId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int?> GetLatestBuildIdForBranchAsync(string branchName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DateTimeOffset?> GetLatestBuildDateForBranchAsync(string branchName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<string> UploadAttachmentAsync(string fileName, byte[] content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AttachFileToWorkItemAsync(int workItemId, string attachmentUrl, string comment, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// Azure Repos em memória: cada commit guarda o snapshot completo dos arquivos, o que permite calcular commits à
/// frente, merge-base, diffs e um merge de 3 vias por arquivo (conflito quando os dois lados mudam o mesmo arquivo).
/// Registra toda escrita para os testes verificarem o que foi (e o que não foi) feito.
/// </summary>
internal sealed class InMemoryRepos : IAzureReposClient
{
    private sealed record StoredCommit(string Id, string Message, IReadOnlyList<string> Parents, IReadOnlyDictionary<string, string> Files);

    private sealed class Repository
    {
        public required string Id { get; init; }
        public Dictionary<string, StoredCommit> Commits { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Branches { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Dictionary<string, Repository> _repositories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BuildInfo>> _builds = new(StringComparer.OrdinalIgnoreCase);
    private int _sequence;

    public List<string> Writes { get; } = [];

    public void AddRepository(string name) => _repositories[name] = new Repository { Id = $"id-{name}" };

    public string RepositoryId(string name) => _repositories[name].Id;

    /// <summary>Cria um commit sobre <paramref name="parent"/> (ou raiz) aplicando <paramref name="changes"/> (valor null = remover).</summary>
    public string Commit(string repository, string? parent, string message, params (string Path, string? Content)[] changes)
    {
        var repo = _repositories[repository];
        var files = parent is null ? new Dictionary<string, string>() : new Dictionary<string, string>(repo.Commits[parent].Files);
        foreach (var (path, content) in changes)
        {
            if (content is null) files.Remove(path);
            else files[path] = content;
        }

        var id = NewId();
        repo.Commits[id] = new StoredCommit(id, message, parent is null ? [] : [parent], files);
        return id;
    }

    public void SetBranch(string repository, string branch, string commitId) => _repositories[repository].Branches[branch] = commitId;

    public string? BranchHead(string repository, string branch) => _repositories[repository].Branches.GetValueOrDefault(branch);

    public string? FileAt(string repository, string commitId, string path) => _repositories[repository].Commits[commitId].Files.GetValueOrDefault(path);

    public void AddBuild(string branch, BuildInfo build)
    {
        if (!_builds.TryGetValue(branch, out var list))
        {
            list = [];
            _builds[branch] = list;
        }

        list.Insert(0, build);
    }

    public Task<IReadOnlyList<GitRepositoryInfo>> ListRepositoriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitRepositoryInfo>>(_repositories.Select(r => new GitRepositoryInfo(r.Value.Id, r.Key, "main")).ToList());

    public Task<GitBranchInfo?> GetBranchAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_repositories.TryGetValue(repositoryName, out var repo) && repo.Branches.TryGetValue(branchName, out var head)
            ? new GitBranchInfo(branchName, head)
            : null);

    public Task<IReadOnlyList<GitBranchInfo>> ListBranchesAsync(string repositoryName, string prefix, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GitBranchInfo>>(!_repositories.TryGetValue(repositoryName, out var repo)
            ? []
            : repo.Branches.Where(b => b.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(b => new GitBranchInfo(b.Key, b.Value)).ToList());

    public Task CreateBranchFromCommitAsync(string repositoryName, string newBranchName, string commitId, CancellationToken cancellationToken = default)
    {
        var repo = _repositories[repositoryName];
        if (repo.Branches.ContainsKey(newBranchName)) throw new InvalidOperationException($"{newBranchName} já existe");
        if (!repo.Commits.ContainsKey(commitId)) throw new InvalidOperationException($"commit {commitId} inexistente");
        repo.Branches[newBranchName] = commitId;
        Writes.Add($"create {repositoryName} {newBranchName} {commitId}");
        return Task.CompletedTask;
    }

    public Task UpdateBranchAsync(string repositoryName, string branchName, string expectedOldCommitId, string newCommitId, CancellationToken cancellationToken = default)
    {
        var repo = _repositories[repositoryName];
        if (!string.Equals(repo.Branches[branchName], expectedOldCommitId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("stale ref");
        repo.Branches[branchName] = newCommitId;
        Writes.Add($"update {repositoryName} {branchName} {newCommitId}");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GitCommitInfo>> GetCommitsAheadAsync(string repositoryName, string itemCommitId, string compareCommitId, CancellationToken cancellationToken = default)
    {
        var repo = _repositories[repositoryName];
        var excluded = Ancestors(repo, compareCommitId);
        var result = Ancestors(repo, itemCommitId).Where(id => !excluded.Contains(id))
            .Select(id => repo.Commits[id])
            .Select(c => new GitCommitInfo(c.Id, c.Message, c.Parents))
            .Reverse()
            .ToList();
        return Task.FromResult<IReadOnlyList<GitCommitInfo>>(result);
    }

    public Task<IReadOnlyList<GitFileChange>> GetCommitChangesAsync(string repositoryName, string commitId, CancellationToken cancellationToken = default)
    {
        var repo = _repositories[repositoryName];
        var commit = repo.Commits[commitId];
        var parentFiles = commit.Parents.Count == 0 ? new Dictionary<string, string>() : repo.Commits[commit.Parents[0]].Files;
        return Task.FromResult(Diff(parentFiles, commit.Files));
    }

    public Task<string?> GetMergeBaseAsync(string repositoryName, string commitId, string otherCommitId, CancellationToken cancellationToken = default)
    {
        var repo = _repositories[repositoryName];
        var other = Ancestors(repo, otherCommitId);
        return Task.FromResult(Ancestors(repo, commitId).FirstOrDefault(other.Contains));
    }

    public Task<IReadOnlyList<GitFileChange>> GetDiffAsync(string repositoryName, string baseCommitId, string targetCommitId, CancellationToken cancellationToken = default)
    {
        var repo = _repositories[repositoryName];
        return Task.FromResult(Diff(repo.Commits[baseCommitId].Files, repo.Commits[targetCommitId].Files));
    }

    public async Task<GitMergeResult> MergeCommitsAsync(string repositoryName, string targetCommitId, string sourceCommitId, string commitMessage, CancellationToken cancellationToken = default)
    {
        var repo = _repositories[repositoryName];
        var baseId = await GetMergeBaseAsync(repositoryName, targetCommitId, sourceCommitId, cancellationToken);
        var baseFiles = baseId is null ? new Dictionary<string, string>() : repo.Commits[baseId].Files;
        var target = repo.Commits[targetCommitId].Files;
        var source = repo.Commits[sourceCommitId].Files;

        var merged = new Dictionary<string, string>();
        var conflicts = new List<string>();
        foreach (var path in baseFiles.Keys.Union(target.Keys).Union(source.Keys))
        {
            var b = baseFiles.GetValueOrDefault(path);
            var t = target.GetValueOrDefault(path);
            var s = source.GetValueOrDefault(path);
            var result = s == b ? t : t == b ? s : t == s ? t : null;
            if (result is null && (t != s))
            {
                conflicts.Add(path);
                continue;
            }

            if (result is not null) merged[path] = result;
        }

        if (conflicts.Count > 0)
        {
            return new GitMergeResult(null, conflicts);
        }

        var id = NewId();
        repo.Commits[id] = new StoredCommit(id, commitMessage, [targetCommitId, sourceCommitId], merged);
        Writes.Add($"merge-object {repositoryName} {id}");
        return new GitMergeResult(id, []);
    }

    public Task<byte[]> GetFileContentAsync(string repositoryName, string path, string commitId, CancellationToken cancellationToken = default)
    {
        var content = _repositories[repositoryName].Commits[commitId].Files.GetValueOrDefault(path);
        return content is null
            ? throw new HttpRequestException("not found", null, HttpStatusCode.NotFound)
            : Task.FromResult(Encoding.UTF8.GetBytes(content));
    }

    public Task<string> PushChangesAsync(string repositoryName, string branchName, string expectedOldCommitId, string commitMessage, IReadOnlyList<GitPushChange> changes, CancellationToken cancellationToken = default)
    {
        var repo = _repositories[repositoryName];
        if (!string.Equals(repo.Branches[branchName], expectedOldCommitId, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("stale ref");

        var id = Commit(repositoryName, expectedOldCommitId, commitMessage,
            changes.Select(c => (c.Path, c.ChangeType == "delete" ? null : Encoding.UTF8.GetString(c.Content!))).ToArray());
        repo.Branches[branchName] = id;
        Writes.Add($"push {repositoryName} {branchName} {string.Join(",", changes.Select(c => c.ChangeType + ":" + c.Path))}");
        return Task.FromResult(id);
    }

    public Task<IReadOnlyList<BuildInfo>> ListBuildsForBranchAsync(string branchName, int top = 10, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BuildInfo>>(_builds.TryGetValue(branchName, out var list) ? list.Take(top).ToList() : []);

    private static List<string> Ancestors(Repository repo, string commitId)
    {
        // Busca em largura a partir do commit: a ordem dá o ancestral comum mais próximo primeiro.
        var order = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>([commitId]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id)) continue;
            order.Add(id);
            foreach (var parent in repo.Commits[id].Parents) queue.Enqueue(parent);
        }

        return order;
    }

    private static IReadOnlyList<GitFileChange> Diff(IReadOnlyDictionary<string, string> from, IReadOnlyDictionary<string, string> to)
    {
        var changes = new List<GitFileChange>();
        foreach (var path in from.Keys.Union(to.Keys).Order(StringComparer.Ordinal))
        {
            var had = from.TryGetValue(path, out var before);
            var has = to.TryGetValue(path, out var after);
            if (had && has && before == after) continue;
            changes.Add(new GitFileChange(path, !had ? "add" : !has ? "delete" : "edit"));
        }

        return changes;
    }

    private string NewId() => (++_sequence).ToString("x40");
}

/// <summary>App Services em memória: site → imagem configurada.</summary>
internal sealed class InMemoryAppServices : IAppServiceClient
{
    public Dictionary<string, string> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<string> GetCurrentTagAsync(string siteName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<string?> GetContainerImageAsync(string siteName, CancellationToken cancellationToken = default) =>
        Task.FromResult(Images.GetValueOrDefault(siteName));

    public Task<IReadOnlyList<string>> FindSitesAsync(string nameFragment, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Images.Keys.Where(k => k.Contains(nameFragment, StringComparison.OrdinalIgnoreCase)).Order().ToList());
}
