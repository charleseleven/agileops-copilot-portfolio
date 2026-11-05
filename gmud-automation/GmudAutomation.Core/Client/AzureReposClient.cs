using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Client;

/// <summary>Cliente HTTP concreto para Git e Pipelines do Azure DevOps, autenticado via PAT (Basic Auth).</summary>
public sealed class AzureReposClient : IAzureReposClient
{
    private const string EmptyObjectId = "0000000000000000000000000000000000000000";

    private readonly HttpClient _httpClient;
    private readonly AzureDevOpsClientOptions _options;
    private readonly TimeSpan _mergePollInterval;
    private readonly int _mergePollAttempts;

    public AzureReposClient(HttpClient httpClient, AzureDevOpsClientOptions options, TimeSpan? mergePollInterval = null, int mergePollAttempts = 60)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _mergePollInterval = mergePollInterval ?? TimeSpan.FromSeconds(1);
        _mergePollAttempts = mergePollAttempts;

        _httpClient.BaseAddress ??= new Uri($"https://dev.azure.com/{_options.Organization}/");

        var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_options.PersonalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
    }

    private string ReposPath(string relativePath) => $"{Uri.EscapeDataString(_options.EffectiveReposProject)}/_apis/{relativePath}";

    private string RepositoryPath(string repositoryName, string relativePath) =>
        ReposPath($"git/repositories/{Uri.EscapeDataString(repositoryName)}/{relativePath}");

    public async Task<IReadOnlyList<GitRepositoryInfo>> ListRepositoriesAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(ReposPath($"git/repositories?api-version={_options.ApiVersion}"), cancellationToken);

        return document.RootElement.GetProperty("value").EnumerateArray()
            .Select(r => new GitRepositoryInfo(
                r.GetProperty("id").GetString() ?? string.Empty,
                r.GetProperty("name").GetString() ?? string.Empty,
                r.TryGetProperty("defaultBranch", out var defaultBranch) ? defaultBranch.GetString()?.Replace("refs/heads/", string.Empty, StringComparison.Ordinal) : null))
            .ToList();
    }

    public async Task<GitBranchInfo?> GetBranchAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default)
    {
        // O filtro de refs da API é por PREFIXO ("feature/us_1" também traria "feature/us_10") — por isso
        // a comparação exata do nome é feita aqui.
        var branches = await ListBranchesAsync(repositoryName, branchName, cancellationToken);
        return branches.FirstOrDefault(b => string.Equals(b.Name, branchName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<GitBranchInfo>> ListBranchesAsync(string repositoryName, string prefix, CancellationToken cancellationToken = default)
    {
        var filter = Uri.EscapeDataString($"heads/{prefix}");
        using var response = await _httpClient.GetAsync(
            RepositoryPath(repositoryName, $"refs?filter={filter}&api-version={_options.ApiVersion}"), cancellationToken);

        // Repositório inexistente = nenhuma branch (resultado de negócio, não falha de comunicação).
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<GitBranchInfo>();
        }

        response.EnsureSuccessStatusCode();
        using var document = await ParseAsync(response, cancellationToken);

        return document.RootElement.GetProperty("value").EnumerateArray()
            .Select(r => new GitBranchInfo(
                (r.GetProperty("name").GetString() ?? string.Empty).Replace("refs/heads/", string.Empty, StringComparison.Ordinal),
                r.GetProperty("objectId").GetString() ?? string.Empty))
            .ToList();
    }

    public Task CreateBranchFromCommitAsync(string repositoryName, string newBranchName, string commitId, CancellationToken cancellationToken = default) =>
        UpdateRefAsync(repositoryName, newBranchName, EmptyObjectId, commitId, cancellationToken);

    public Task UpdateBranchAsync(string repositoryName, string branchName, string expectedOldCommitId, string newCommitId, CancellationToken cancellationToken = default) =>
        UpdateRefAsync(repositoryName, branchName, expectedOldCommitId, newCommitId, cancellationToken);

    private async Task UpdateRefAsync(string repositoryName, string branchName, string oldObjectId, string newObjectId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new[] { new { name = $"refs/heads/{branchName}", oldObjectId, newObjectId } });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(RepositoryPath(repositoryName, $"refs?api-version={_options.ApiVersion}"), content, cancellationToken);
        response.EnsureSuccessStatusCode();

        // A API responde 200 mesmo quando a atualização é recusada (ex: oldObjectId divergente, branch
        // já existente) — o resultado real vem em value[].success/updateStatus.
        using var document = await ParseAsync(response, cancellationToken);
        var result = document.RootElement.GetProperty("value")[0];
        if (!result.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            var status = result.TryGetProperty("updateStatus", out var updateStatus) ? updateStatus.GetString() : "desconhecido";
            throw new InvalidOperationException(
                $"Atualização da branch \"{branchName}\" recusada pelo Azure Repos em \"{repositoryName}\" (status: {status}).");
        }
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetCommitsAheadAsync(
        string repositoryName, string itemCommitId, string compareCommitId, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            itemVersion = new { version = itemCommitId, versionType = "commit" },
            compareVersion = new { version = compareCommitId, versionType = "commit" }
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(
            RepositoryPath(repositoryName, $"commitsbatch?$top=1000&api-version={_options.ApiVersion}"), content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await ParseAsync(response, cancellationToken);

        return document.RootElement.GetProperty("value").EnumerateArray()
            .Select(c => new GitCommitInfo(
                c.GetProperty("commitId").GetString() ?? string.Empty,
                c.TryGetProperty("comment", out var comment) ? comment.GetString() ?? string.Empty : string.Empty,
                c.TryGetProperty("parents", out var parents)
                    ? parents.EnumerateArray().Select(p => p.GetString() ?? string.Empty).ToList()
                    : Array.Empty<string>()))
            .ToList();
    }

    public async Task<IReadOnlyList<GitFileChange>> GetCommitChangesAsync(string repositoryName, string commitId, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            RepositoryPath(repositoryName, $"commits/{commitId}/changes?api-version={_options.ApiVersion}"), cancellationToken);
        return ParseChanges(document.RootElement);
    }

    public async Task<string?> GetMergeBaseAsync(string repositoryName, string commitId, string otherCommitId, CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync(
            RepositoryPath(repositoryName, $"commits/{commitId}/mergebases?otherCommitId={otherCommitId}&api-version={_options.ApiVersion}-preview.1"),
            cancellationToken);

        var values = document.RootElement.GetProperty("value");
        return values.GetArrayLength() > 0 ? values[0].GetProperty("commitId").GetString() : null;
    }

    public async Task<IReadOnlyList<GitFileChange>> GetDiffAsync(
        string repositoryName, string baseCommitId, string targetCommitId, CancellationToken cancellationToken = default)
    {
        var changes = new List<GitFileChange>();
        var skip = 0;
        const int pageSize = 2000;

        while (true)
        {
            using var document = await GetJsonAsync(RepositoryPath(repositoryName,
                $"diffs/commits?baseVersion={baseCommitId}&baseVersionType=commit&targetVersion={targetCommitId}&targetVersionType=commit" +
                $"&diffCommonCommit=false&$top={pageSize}&$skip={skip}&api-version={_options.ApiVersion}"), cancellationToken);

            var page = ParseChanges(document.RootElement);
            changes.AddRange(page);

            var allChangesIncluded = !document.RootElement.TryGetProperty("allChangesIncluded", out var all) || all.GetBoolean();
            if (allChangesIncluded || page.Count == 0)
            {
                return changes;
            }

            skip += pageSize;
        }
    }

    public async Task<GitMergeResult> MergeCommitsAsync(
        string repositoryName, string targetCommitId, string sourceCommitId, string commitMessage, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { parents = new[] { targetCommitId, sourceCommitId }, comment = commitMessage });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(
            RepositoryPath(repositoryName, $"merges?api-version={_options.ApiVersion}-preview.1"), content, cancellationToken);
        response.EnsureSuccessStatusCode();

        int operationId;
        using (var created = await ParseAsync(response, cancellationToken))
        {
            operationId = created.RootElement.GetProperty("mergeOperationId").GetInt32();
            var immediate = InterpretMergeStatus(created.RootElement);
            if (immediate is not null)
            {
                return immediate;
            }
        }

        // A operação de merge é assíncrona no servidor — consulta até sair de queued/inProgress.
        for (var attempt = 0; attempt < _mergePollAttempts; attempt++)
        {
            await Task.Delay(_mergePollInterval, cancellationToken);
            using var status = await GetJsonAsync(
                RepositoryPath(repositoryName, $"merges/{operationId}?api-version={_options.ApiVersion}-preview.1"), cancellationToken);
            var result = InterpretMergeStatus(status.RootElement);
            if (result is not null)
            {
                return result;
            }
        }

        throw new TimeoutException($"O merge {operationId} em \"{repositoryName}\" não terminou dentro do tempo esperado.");
    }

    private static GitMergeResult? InterpretMergeStatus(JsonElement merge)
    {
        var status = merge.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        var detail = merge.TryGetProperty("detailedStatus", out var detailElement) ? detailElement : default;

        switch (status)
        {
            case "completed":
                var commitId = detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("mergeCommitId", out var id) ? id.GetString() : null;
                return new GitMergeResult(commitId ?? throw new InvalidOperationException("Merge concluído sem mergeCommitId."), Array.Empty<string>());
            case "conflicts":
                var files = detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("conflicts", out var conflicts)
                    ? conflicts.EnumerateArray()
                        .Select(c => c.TryGetProperty("path", out var path) ? path.GetString() ?? string.Empty : string.Empty)
                        .Where(p => p.Length > 0)
                        .ToList()
                    : new List<string>();
                return new GitMergeResult(null, files);
            case "failure":
                var message = detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("failureMessage", out var failure) ? failure.GetString() : null;
                throw new InvalidOperationException($"O Azure Repos recusou o merge: {message ?? "(sem detalhe)"}");
            default:
                return null;
        }
    }

    public async Task<byte[]> GetFileContentAsync(string repositoryName, string path, string commitId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, RepositoryPath(repositoryName,
            $"items?path={Uri.EscapeDataString(path)}&versionDescriptor.version={commitId}&versionDescriptor.versionType=commit" +
            $"&download=true&api-version={_options.ApiVersion}"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<string> PushChangesAsync(
        string repositoryName, string branchName, string expectedOldCommitId, string commitMessage, IReadOnlyList<GitPushChange> changes,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            refUpdates = new[] { new { name = $"refs/heads/{branchName}", oldObjectId = expectedOldCommitId } },
            commits = new[]
            {
                new
                {
                    comment = commitMessage,
                    changes = changes.Select(c => new
                    {
                        changeType = c.ChangeType,
                        item = new { path = c.Path },
                        newContent = c.Content is null ? null : new { content = Convert.ToBase64String(c.Content), contentType = "base64encoded" }
                    }).ToArray()
                }
            }
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(RepositoryPath(repositoryName, $"pushes?api-version={_options.ApiVersion}"), content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await ParseAsync(response, cancellationToken);

        return document.RootElement.GetProperty("commits")[0].GetProperty("commitId").GetString()
            ?? throw new InvalidOperationException("O push não retornou o commit criado.");
    }

    public async Task<IReadOnlyList<BuildInfo>> ListBuildsForBranchAsync(string branchName, int top = 10, CancellationToken cancellationToken = default)
    {
        var branchFilter = Uri.EscapeDataString($"refs/heads/{branchName}");
        using var document = await GetJsonAsync(
            ReposPath($"build/builds?branchName={branchFilter}&$top={top}&queryOrder=queueTimeDescending&api-version={_options.ApiVersion}"),
            cancellationToken);

        return document.RootElement.GetProperty("value").EnumerateArray()
            .Select(b => new BuildInfo(
                Id: b.GetProperty("id").GetInt32(),
                BuildNumber: GetString(b, "buildNumber"),
                Status: GetString(b, "status"),
                Result: b.TryGetProperty("result", out var result) ? result.GetString() : null,
                DefinitionName: b.TryGetProperty("definition", out var definition) ? GetString(definition, "name") : string.Empty,
                SourceBranch: GetString(b, "sourceBranch"),
                SourceVersion: GetString(b, "sourceVersion"),
                QueueTime: b.TryGetProperty("queueTime", out var queueTime) ? queueTime.GetDateTimeOffset() : null))
            .ToList();
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static List<GitFileChange> ParseChanges(JsonElement root)
    {
        var changes = new List<GitFileChange>();
        if (!root.TryGetProperty("changes", out var items))
        {
            return changes;
        }

        foreach (var change in items.EnumerateArray())
        {
            if (!change.TryGetProperty("item", out var item))
            {
                continue;
            }

            // Pastas (tree) aparecem na lista de mudanças mas não são arquivos a conciliar.
            var isFolder = (item.TryGetProperty("isFolder", out var folder) && folder.GetBoolean())
                || (item.TryGetProperty("gitObjectType", out var objectType) && objectType.GetString() == "tree");
            if (isFolder)
            {
                continue;
            }

            changes.Add(new GitFileChange(GetString(item, "path"), GetString(change, "changeType")));
        }

        return changes;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ParseAsync(response, cancellationToken);
    }

    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
