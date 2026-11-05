using System.Text;
using System.Text.Json;
using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Providers.Trello;

public sealed class TrelloOptions
{
    public required string ApiKey { get; init; }

    /// <summary>Token de usuário (trello.com/app-key) — vem de variável de ambiente, nunca hardcoded.</summary>
    public required string Token { get; init; }

    /// <summary>ID (ou shortLink) do board, ex: o trecho "AbCd1234" de https://trello.com/b/AbCd1234/meu-board.</summary>
    public required string BoardId { get; init; }

    /// <summary>Lista onde os cards são criados; é criada no board se ainda não existir.</summary>
    public string ListName { get; init; } = "Backlog";
}

/// <summary>
/// O Trello não tem hierarquia nativa, então ela é representada com os recursos do próprio Trello:
/// Epic, Feature e User Story viram cards (com labels "Epic"/"Feature"/"User Story" e labels do Epic/Feature
/// de origem), cada card filho recebe um anexo-link para o card pai e o pai ganha um checklist apontando para
/// os filhos; Tasks e Bugs viram itens dos checklists "Tasks"/"Bugs" do card da User Story, com as horas no
/// nome do item. Os story points seguem a convenção do Scrum for Trello/Agile Tools: "(5) Título do card".
/// </summary>
public sealed class TrelloProvider : IWorkItemTrackerProvider
{
    private static readonly Dictionary<string, string> KindLabelColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Epic"] = "purple",
        ["Feature"] = "blue",
        ["User Story"] = "green"
    };

    private readonly HttpClient _httpClient;
    private readonly TrelloOptions _options;
    private readonly Dictionary<string, string> _labelIdsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string CardId, string Checklist), string> _checklistIds = [];
    private string? _listId;
    private string? _boardId;

    public TrelloProvider(HttpClient httpClient, TrelloOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _httpClient.BaseAddress ??= new Uri("https://api.trello.com/1/");
        // Autenticação via header (em vez de ?key=&token= na URL) para que as credenciais não apareçam em logs de URL.
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Authorization", $"OAuth oauth_consumer_key=\"{_options.ApiKey}\", oauth_token=\"{_options.Token}\"");
    }

    public string Name => "Trello";

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (_listId is not null)
        {
            return;
        }

        using (var board = await GetJsonAsync($"boards/{Uri.EscapeDataString(_options.BoardId)}?fields=id,name", cancellationToken))
        {
            _boardId = board.RootElement.GetProperty("id").GetString();
        }

        using (var lists = await GetJsonAsync($"boards/{_boardId}/lists?filter=open&fields=id,name", cancellationToken))
        {
            _listId = lists.RootElement.EnumerateArray()
                .Where(list => string.Equals(list.GetProperty("name").GetString(), _options.ListName, StringComparison.OrdinalIgnoreCase))
                .Select(list => list.GetProperty("id").GetString())
                .FirstOrDefault();
        }

        if (_listId is null)
        {
            using var createdList = await PostJsonAsync("lists", new { name = _options.ListName, idBoard = _boardId, pos = "top" }, cancellationToken);
            _listId = createdList.RootElement.GetProperty("id").GetString();
        }

        using var labels = await GetJsonAsync($"boards/{_boardId}/labels?fields=id,name&limit=1000", cancellationToken);
        foreach (var label in labels.RootElement.EnumerateArray())
        {
            var name = label.GetProperty("name").GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                _labelIdsByName.TryAdd(name, label.GetProperty("id").GetString()!);
            }
        }
    }

    public async Task<CreatedWorkItem> CreateAsync(WorkItemCreationRequest request, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);

        return request.Kind is WorkItemKind.Task or WorkItemKind.Bug
            ? await CreateChecklistItemAsync(request, cancellationToken)
            : await CreateCardAsync(request, cancellationToken);
    }

    private async Task<CreatedWorkItem> CreateCardAsync(WorkItemCreationRequest request, CancellationToken cancellationToken)
    {
        var name = request.Estimate.StoryPoints is { } points
            ? $"({DescriptionComposer.FormatNumber(points)}) {request.Title}"
            : request.Title;

        var measures = DescriptionComposer.DescribeEstimate(request.Estimate);
        var description = DescriptionComposer.ToMarkdown(
            DescriptionComposer.Compose(request, includeAcceptanceCriteria: true, includeReproSteps: true, measures));

        var labelNames = new List<string> { request.Kind.DisplayName() };
        labelNames.AddRange(request.Tags);
        if (request.EpicTitle is not null)
        {
            labelNames.Add($"Epic: {request.EpicTitle}");
        }

        if (request.FeatureTitle is not null)
        {
            labelNames.Add($"Feature: {request.FeatureTitle}");
        }

        var labelIds = new List<string>();
        foreach (var labelName in labelNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            labelIds.Add(await EnsureLabelAsync(labelName, cancellationToken));
        }

        using var card = await PostJsonAsync("cards", new
        {
            idList = _listId,
            name,
            desc = description,
            pos = "bottom",
            idLabels = string.Join(',', labelIds)
        }, cancellationToken);

        var cardId = card.RootElement.GetProperty("id").GetString()!;
        var cardUrl = card.RootElement.TryGetProperty("shortUrl", out var shortUrl) ? shortUrl.GetString() : card.RootElement.GetProperty("url").GetString();

        if (request.Parent is { IsVirtual: false, Url: not null } parent)
        {
            using (await PostJsonAsync($"cards/{cardId}/attachments", new { url = parent.Url, name = $"{parent.Kind.DisplayName()}: {parent.Title}" }, cancellationToken))
            {
            }

            var checklistName = request.Kind == WorkItemKind.Feature ? "Features" : "User Stories";
            await AddCheckItemAsync(parent.Id!, checklistName, cardUrl!, cancellationToken);
        }

        return new CreatedWorkItem(request.Kind, request.Title, cardId, cardUrl, "card", []);
    }

    private async Task<CreatedWorkItem> CreateChecklistItemAsync(WorkItemCreationRequest request, CancellationToken cancellationToken)
    {
        if (request.Parent is not { IsVirtual: false } story)
        {
            throw new InvalidOperationException($"{request.Kind.DisplayName()} \"{request.Title}\" precisa de uma User Story pai no Trello.");
        }

        var measures = DescriptionComposer.DescribeEstimate(request.Estimate);
        var suffix = measures.Count > 0 ? $" ({string.Join(", ", measures.Select(m => $"{m.Key}: {m.Value}"))})" : string.Empty;
        var checklistName = request.Kind == WorkItemKind.Bug ? "Bugs" : "Tasks";

        var itemId = await AddCheckItemAsync(story.Id!, checklistName, $"{request.Title}{suffix}", cancellationToken);
        var warnings = string.IsNullOrWhiteSpace(request.Description) && request.ReproSteps.Count == 0
            ? Array.Empty<string>()
            : ["Itens de checklist do Trello não têm descrição — detalhes da task/bug não foram copiados."];

        return new CreatedWorkItem(request.Kind, request.Title, itemId, story.Url, $"checklist \"{checklistName}\"", warnings);
    }

    private async Task<string> AddCheckItemAsync(string cardId, string checklistName, string itemName, CancellationToken cancellationToken)
    {
        if (!_checklistIds.TryGetValue((cardId, checklistName), out var checklistId))
        {
            using var checklist = await PostJsonAsync("checklists", new { idCard = cardId, name = checklistName }, cancellationToken);
            checklistId = checklist.RootElement.GetProperty("id").GetString()!;
            _checklistIds[(cardId, checklistName)] = checklistId;
        }

        using var item = await PostJsonAsync($"checklists/{checklistId}/checkItems", new { name = itemName, pos = "bottom" }, cancellationToken);
        return item.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<string> EnsureLabelAsync(string name, CancellationToken cancellationToken)
    {
        // Labels do Trello são limitadas a ~100 caracteres no nome.
        var trimmed = name.Length > 100 ? name[..100] : name;
        if (_labelIdsByName.TryGetValue(trimmed, out var id))
        {
            return id;
        }

        KindLabelColors.TryGetValue(trimmed, out var color);
        using var label = await PostJsonAsync("labels", new { name = trimmed, color = color ?? "sky", idBoard = _boardId }, cancellationToken);
        id = label.RootElement.GetProperty("id").GetString()!;
        _labelIdsByName[trimmed] = id;
        return id;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private async Task<JsonDocument> PostJsonAsync(string path, object body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(path, content, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} do Trello: {body}", null, response.StatusCode);
        }

        return JsonDocument.Parse(body);
    }
}
