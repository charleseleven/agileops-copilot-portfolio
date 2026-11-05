using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Client;

/// <summary>Cliente HTTP concreto para as APIs REST do Azure DevOps (Boards e Repos), autenticado via PAT (Basic Auth).</summary>
public sealed class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly HttpClient _httpClient;
    private readonly AzureDevOpsClientOptions _options;

    public AzureDevOpsClient(HttpClient httpClient, AzureDevOpsClientOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _httpClient.BaseAddress ??= new Uri($"https://dev.azure.com/{_options.Organization}/");

        var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_options.PersonalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
    }

    // Boards e Repos podem viver em Team Projects diferentes na mesma organização (ex: "contoso" separa
    // "Contoso Projetos" de "Contoso Repositorios") — por isso o projeto é escolhido por chamada.
    private string BoardsPath(string relativePath) => $"{Uri.EscapeDataString(_options.BoardsProject)}/_apis/{relativePath}";

    private string ReposPath(string relativePath) => $"{Uri.EscapeDataString(_options.EffectiveReposProject)}/_apis/{relativePath}";

    public async Task<WorkItemInfo> GetWorkItemAsync(int workItemId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            BoardsPath($"wit/workitems/{workItemId}?api-version={_options.ApiVersion}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var fields = root.GetProperty("fields");

        return new WorkItemInfo(
            Id: root.GetProperty("id").GetInt32(),
            Title: fields.GetProperty("System.Title").GetString() ?? string.Empty,
            WorkItemType: fields.GetProperty("System.WorkItemType").GetString() ?? string.Empty,
            State: fields.GetProperty("System.State").GetString() ?? string.Empty,
            DescriptionHtml: fields.TryGetProperty("System.Description", out var description) ? description.GetString() : null,
            Url: root.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty);
    }

    public async Task<WorkItemDetails> GetWorkItemDetailsAsync(int workItemId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            BoardsPath($"wit/workitems/{workItemId}?$expand=relations&api-version={_options.ApiVersion}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var fields = root.GetProperty("fields");

        var info = new WorkItemInfo(
            Id: root.GetProperty("id").GetInt32(),
            Title: fields.GetProperty("System.Title").GetString() ?? string.Empty,
            WorkItemType: fields.GetProperty("System.WorkItemType").GetString() ?? string.Empty,
            State: fields.GetProperty("System.State").GetString() ?? string.Empty,
            DescriptionHtml: fields.TryGetProperty("System.Description", out var description) ? description.GetString() : null,
            Url: root.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty);

        var relations = new List<WorkItemRelation>();
        if (root.TryGetProperty("relations", out var relationItems) && relationItems.ValueKind == JsonValueKind.Array)
        {
            foreach (var relation in relationItems.EnumerateArray())
            {
                var name = relation.TryGetProperty("attributes", out var attributes) && attributes.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;

                relations.Add(new WorkItemRelation(
                    relation.TryGetProperty("rel", out var rel) ? rel.GetString() ?? string.Empty : string.Empty,
                    relation.TryGetProperty("url", out var relationUrl) ? relationUrl.GetString() ?? string.Empty : string.Empty,
                    name));
            }
        }

        return new WorkItemDetails(info, relations);
    }

    public async Task<IReadOnlyList<WorkItemComment>> GetWorkItemCommentsAsync(int workItemId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            BoardsPath($"wit/workItems/{workItemId}/comments?api-version={_options.ApiVersion}-preview.4"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var comments = new List<WorkItemComment>();
        if (document.RootElement.TryGetProperty("comments", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var createdBy = item.TryGetProperty("createdBy", out var createdByElement)
                    && createdByElement.TryGetProperty("displayName", out var displayName)
                        ? displayName.GetString() ?? string.Empty
                        : string.Empty;

                comments.Add(new WorkItemComment(
                    Id: item.GetProperty("id").GetInt32(),
                    Text: item.GetProperty("text").GetString() ?? string.Empty,
                    CreatedBy: createdBy,
                    CreatedDate: item.TryGetProperty("createdDate", out var createdDate) ? createdDate.GetDateTimeOffset() : default));
            }
        }

        return comments;
    }

    public async Task<IReadOnlyList<int>> GetChildWorkItemIdsAsync(int workItemId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            BoardsPath($"wit/workitems/{workItemId}?$expand=relations&api-version={_options.ApiVersion}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var childIds = new List<int>();
        if (document.RootElement.TryGetProperty("relations", out var relations))
        {
            foreach (var relation in relations.EnumerateArray())
            {
                var rel = relation.TryGetProperty("rel", out var relProp) ? relProp.GetString() : null;
                if (!string.Equals(rel, "System.LinkTypes.Hierarchy-Forward", StringComparison.Ordinal))
                {
                    continue;
                }

                var url = relation.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
                var lastSegment = url?[(url.LastIndexOf('/') + 1)..];
                if (lastSegment != null && int.TryParse(lastSegment, out var childId))
                {
                    childIds.Add(childId);
                }
            }
        }

        return childIds;
    }

    public async Task<bool> BranchExistsAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default)
    {
        var filter = Uri.EscapeDataString($"heads/{branchName}");
        using var response = await _httpClient.GetAsync(
            ReposPath($"git/repositories/{Uri.EscapeDataString(repositoryName)}/refs?filter={filter}&api-version={_options.ApiVersion}"), cancellationToken);

        // Repositório inexistente (nome do projeto extraído do comentário não corresponde a um repo real) é um
        // resultado de negócio válido — "branch não encontrada" —, não uma falha de comunicação.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty("count", out var count) && count.GetInt32() > 0;
    }

    public async Task AddWorkItemCommentAsync(int workItemId, string commentText, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new { text = commentText });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            BoardsPath($"wit/workItems/{workItemId}/comments?api-version={_options.ApiVersion}-preview.4"), content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> CreateBranchAsync(string repositoryName, string sourceBranchName, string newBranchName, CancellationToken cancellationToken = default)
    {
        var sourceObjectId = await GetBranchObjectIdAsync(repositoryName, sourceBranchName, cancellationToken);
        var newRefName = $"refs/heads/{newBranchName}";

        var payload = JsonSerializer.Serialize(new[]
        {
            new { name = newRefName, oldObjectId = "0000000000000000000000000000000000000000", newObjectId = sourceObjectId }
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            ReposPath($"git/repositories/{Uri.EscapeDataString(repositoryName)}/refs?api-version={_options.ApiVersion}"), content, cancellationToken);
        response.EnsureSuccessStatusCode();

        return newRefName;
    }

    private async Task<string> GetBranchObjectIdAsync(string repositoryName, string branchName, CancellationToken cancellationToken)
    {
        var filter = Uri.EscapeDataString($"heads/{branchName}");
        using var response = await _httpClient.GetAsync(
            ReposPath($"git/repositories/{Uri.EscapeDataString(repositoryName)}/refs?filter={filter}&api-version={_options.ApiVersion}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var values = document.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"Branch \"{branchName}\" não encontrada no repositório \"{repositoryName}\".");
        }

        return values[0].GetProperty("objectId").GetString() ?? throw new InvalidOperationException(
            $"A API não retornou objectId para a branch \"{branchName}\" no repositório \"{repositoryName}\".");
    }

    public async Task MergeBranchesAsync(string repositoryName, string sourceBranchName, string targetBranchName, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            parents = new[] { targetBranchName, sourceBranchName }
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            ReposPath($"git/repositories/{Uri.EscapeDataString(repositoryName)}/merges?api-version={_options.ApiVersion}-preview.1"), content, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var conflictingFiles = await ParseConflictingFilesAsync(response, cancellationToken);
            throw new MergeConflictException(sourceBranchName, targetBranchName, conflictingFiles);
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<string>> ParseConflictingFilesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("detailedMergeStatus", out var detail)
                || !detail.TryGetProperty("conflicts", out var conflicts))
            {
                return Array.Empty<string>();
            }

            var files = new List<string>();
            foreach (var conflict in conflicts.EnumerateArray())
            {
                if (conflict.TryGetProperty("path", out var path))
                {
                    files.Add(path.GetString() ?? string.Empty);
                }
            }

            return files;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public async Task<PullRequestInfo> CreatePullRequestAsync(
        string repositoryName, string sourceBranchName, string targetBranchName, string title, string description, bool isDraft,
        CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sourceRefName = $"refs/heads/{sourceBranchName}",
            targetRefName = $"refs/heads/{targetBranchName}",
            title,
            description,
            isDraft
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync(
            ReposPath($"git/repositories/{Uri.EscapeDataString(repositoryName)}/pullrequests?api-version={_options.ApiVersion}"), content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        return new PullRequestInfo(
            Id: root.GetProperty("pullRequestId").GetInt32(),
            Url: root.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty,
            Status: root.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
            LastMergeSourceCommitId: root.TryGetProperty("lastMergeSourceCommit", out var lastMerge)
                && lastMerge.TryGetProperty("commitId", out var commitId)
                    ? commitId.GetString() ?? string.Empty
                    : string.Empty);
    }

    public async Task CompletePullRequestAsync(string repositoryName, int pullRequestId, string lastMergeSourceCommitId, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            status = "completed",
            lastMergeSourceCommit = new { commitId = lastMergeSourceCommitId }
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Patch,
            ReposPath($"git/repositories/{Uri.EscapeDataString(repositoryName)}/pullrequests/{pullRequestId}?api-version={_options.ApiVersion}"))
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<int?> GetLatestBuildIdForBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        var branchFilter = Uri.EscapeDataString($"refs/heads/{branchName}");
        using var response = await _httpClient.GetAsync(
            ReposPath($"build/builds?branchName={branchFilter}&$top=1&queryOrder=queueTimeDescending&api-version={_options.ApiVersion}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var values = document.RootElement.GetProperty("value");
        return values.GetArrayLength() > 0 ? values[0].GetProperty("id").GetInt32() : null;
    }

    public async Task<DateTimeOffset?> GetLatestBuildDateForBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        var branchFilter = Uri.EscapeDataString($"refs/heads/{branchName}");
        using var response = await _httpClient.GetAsync(
            ReposPath($"build/builds?branchName={branchFilter}&$top=1&queryOrder=queueTimeDescending&api-version={_options.ApiVersion}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var values = document.RootElement.GetProperty("value");
        if (values.GetArrayLength() == 0)
        {
            return null;
        }

        return values[0].TryGetProperty("queueTime", out var queueTime) ? queueTime.GetDateTimeOffset() : null;
    }

    public async Task<byte[]> DownloadAttachmentAsync(string attachmentUrl, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(attachmentUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<string> UploadAttachmentAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        using var byteContent = new ByteArrayContent(content);
        byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

        using var response = await _httpClient.PostAsync(
            BoardsPath($"wit/attachments?fileName={Uri.EscapeDataString(fileName)}&api-version={_options.ApiVersion}"), byteContent, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return document.RootElement.GetProperty("url").GetString() ?? string.Empty;
    }

    public async Task AttachFileToWorkItemAsync(int workItemId, string attachmentUrl, string comment, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new[]
        {
            new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "AttachedFile",
                    url = attachmentUrl,
                    attributes = new { comment }
                }
            }
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json-patch+json");

        using var request = new HttpRequestMessage(HttpMethod.Patch, BoardsPath($"wit/workitems/{workItemId}?api-version={_options.ApiVersion}"))
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
