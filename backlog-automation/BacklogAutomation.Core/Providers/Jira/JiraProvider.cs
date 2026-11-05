using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Providers.Jira;

public sealed class JiraOptions
{
    /// <summary>URL do site, ex: https://sua-empresa.atlassian.net</summary>
    public required string BaseUrl { get; init; }

    public required string Email { get; init; }

    /// <summary>API token (id.atlassian.com → Security → API tokens) — vem de variável de ambiente, nunca hardcoded.</summary>
    public required string ApiToken { get; init; }

    public required string ProjectKey { get; init; }

    /// <summary>
    /// Campo de story points. Jira Cloud usa "customfield_10016" (Story point estimate) na maioria dos sites,
    /// mas o ID varia por instância — configure JIRA_STORY_POINTS_FIELD se for diferente.
    /// </summary>
    public string StoryPointsField { get; init; } = "customfield_10016";

    /// <summary>Medida livre do documento → ID do campo customizado (ex: "Pontos de função" → "customfield_10100").</summary>
    public IReadOnlyDictionary<string, string> CustomFieldMap { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Cria os itens no Jira Cloud via REST v2 (<c>POST /rest/api/2/issue</c>). Os tipos são escolhidos pelo
/// <c>hierarchyLevel</c> do projeto, não por nome fixo (funciona com Jira em português — "História",
/// "Subtarefa"): nível 2 (ex: Initiative) ← Epic, nível 1 (Epic) ← Feature, nível 0 (Story) ← User Story,
/// nível -1 (Sub-task) ← Task e Bug (Bug recebe a label "bug", pois no Jira um Bug de nível 0 não pode ser
/// filho de uma Story). Sem tipo de nível 2 (planos sem Advanced Roadmaps), o Epic do documento vira a
/// label "epic-&lt;nome&gt;" em todos os descendentes, preservando o agrupamento.
/// </summary>
public sealed partial class JiraProvider : IWorkItemTrackerProvider
{
    private readonly HttpClient _httpClient;
    private readonly JiraOptions _options;
    private readonly Dictionary<WorkItemKind, JiraIssueType?> _typesByKind = [];
    private readonly Dictionary<string, HashSet<string>?> _fieldsByIssueType = [];
    private bool _prepared;

    public JiraProvider(HttpClient httpClient, JiraOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Email}:{_options.ApiToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string Name => "Jira";

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (_prepared)
        {
            return;
        }

        using var response = await _httpClient.GetAsync($"rest/api/2/project/{Uri.EscapeDataString(_options.ProjectKey)}", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var issueTypes = document.RootElement.GetProperty("issueTypes").EnumerateArray()
            .Select(type => new JiraIssueType(
                Id: type.GetProperty("id").GetString() ?? string.Empty,
                Name: type.GetProperty("name").GetString() ?? string.Empty,
                HierarchyLevel: type.TryGetProperty("hierarchyLevel", out var level)
                    ? level.GetInt32()
                    : type.TryGetProperty("subtask", out var subtask) && subtask.GetBoolean() ? -1 : 0))
            .ToList();

        _typesByKind[WorkItemKind.Epic] = Pick(issueTypes, 2, []);
        _typesByKind[WorkItemKind.Feature] = Pick(issueTypes, 1, ["Epic", "Épico"]);
        _typesByKind[WorkItemKind.UserStory] = Pick(issueTypes, 0, ["Story", "História", "Historia", "User Story"]);
        _typesByKind[WorkItemKind.Task] = Pick(issueTypes, -1, ["Sub-task", "Subtask", "Subtarefa", "Sub-tarefa"]);
        _typesByKind[WorkItemKind.Bug] = _typesByKind[WorkItemKind.Task];

        foreach (var kind in new[] { WorkItemKind.Feature, WorkItemKind.UserStory, WorkItemKind.Task })
        {
            if (_typesByKind[kind] is null)
            {
                throw new InvalidOperationException(
                    $"O projeto Jira \"{_options.ProjectKey}\" não tem tipo de item para {kind.DisplayName()} (tipos: {string.Join(", ", issueTypes.Select(t => $"{t.Name}[{t.HierarchyLevel}]"))}).");
            }
        }

        _prepared = true;
    }

    public async Task<CreatedWorkItem> CreateAsync(WorkItemCreationRequest request, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        var warnings = new List<string>();

        var issueType = _typesByKind[request.Kind];
        if (issueType is null)
        {
            // Só acontece com Epic: o projeto não tem nível acima de Epic.
            return new CreatedWorkItem(request.Kind, request.Title, null, null, $"label {EpicLabel(request.Title)}",
                [$"Projeto sem tipo acima de Epic — o Epic foi registrado como a label \"{EpicLabel(request.Title)}\" nos itens filhos."]);
        }

        var availableFields = await GetAvailableFieldsAsync(issueType, cancellationToken);
        bool Has(string field) => availableFields is null || availableFields.Contains(field);

        var fields = new JsonObject
        {
            ["project"] = new JsonObject { ["key"] = _options.ProjectKey },
            ["issuetype"] = new JsonObject { ["id"] = issueType.Id },
            ["summary"] = request.Title
        };

        if (request.Parent is { IsVirtual: false } parent)
        {
            fields["parent"] = new JsonObject { ["key"] = parent.Id };
        }

        var unmapped = DescriptionComposer.DescribeEstimate(request.Estimate);
        var estimate = request.Estimate;

        if (estimate.StoryPoints is { } points && request.Kind is not WorkItemKind.Task && Has(_options.StoryPointsField))
        {
            fields[_options.StoryPointsField] = points;
            unmapped.Remove(MeasureNames.StoryPoints);
        }

        if (request.Kind is WorkItemKind.Task or WorkItemKind.Bug && Has("timetracking")
            && (estimate.OriginalEstimateHours is not null || estimate.RemainingHours is not null))
        {
            var timeTracking = new JsonObject();
            if (estimate.OriginalEstimateHours is { } original)
            {
                timeTracking["originalEstimate"] = ToJiraDuration(original);
                unmapped.Remove(MeasureNames.OriginalEstimate);
            }

            if (estimate.RemainingHours is { } remaining)
            {
                timeTracking["remainingEstimate"] = ToJiraDuration(remaining);
                unmapped.Remove(MeasureNames.Remaining);
            }

            fields["timetracking"] = timeTracking;
        }

        foreach (var (measure, value) in estimate.OtherMeasures)
        {
            if (_options.CustomFieldMap.TryGetValue(measure, out var customField) && Has(customField))
            {
                fields[customField] = decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? JsonValue.Create(number)
                    : JsonValue.Create(value);
                unmapped.Remove(measure);
            }
        }

        if (unmapped.Count > 0)
        {
            warnings.Add($"Sem campo disponível em \"{issueType.Name}\" para: {string.Join(", ", unmapped.Keys)} — registrado(s) na descrição.");
        }

        var description = DescriptionComposer.ToJiraWiki(
            DescriptionComposer.Compose(request, includeAcceptanceCriteria: true, includeReproSteps: true, unmapped));
        if (description.Length > 0)
        {
            fields["description"] = description;
        }

        var labels = BuildLabels(request);
        if (labels.Count > 0 && Has("labels"))
        {
            fields["labels"] = new JsonArray(labels.Select(label => (JsonNode)label).ToArray());
        }

        if (request.Priority is { } priority && Has("priority"))
        {
            fields["priority"] = new JsonObject { ["name"] = ToJiraPriority(priority) };
        }

        var payload = new JsonObject { ["fields"] = fields }.ToJsonString();
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("rest/api/2/issue", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} ao criar \"{issueType.Name}\": {error}", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var key = document.RootElement.GetProperty("key").GetString()!;

        return new CreatedWorkItem(request.Kind, request.Title, key, $"{_options.BaseUrl.TrimEnd('/')}/browse/{key}", issueType.Name, warnings);
    }

    private List<string> BuildLabels(WorkItemCreationRequest request)
    {
        var labels = request.Tags.Select(ToLabel).ToList();

        if (request.Kind == WorkItemKind.Bug)
        {
            labels.Add("bug");
        }

        if (_typesByKind[WorkItemKind.Epic] is null && request.EpicTitle is not null)
        {
            labels.Add(EpicLabel(request.EpicTitle));
        }

        return labels.Where(label => label.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<HashSet<string>?> GetAvailableFieldsAsync(JiraIssueType issueType, CancellationToken cancellationToken)
    {
        if (_fieldsByIssueType.TryGetValue(issueType.Id, out var cached))
        {
            return cached;
        }

        HashSet<string>? fields = null;
        using var response = await _httpClient.GetAsync(
            $"rest/api/2/issue/createmeta/{Uri.EscapeDataString(_options.ProjectKey)}/issuetypes/{issueType.Id}?maxResults=200", cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            var values = root.TryGetProperty("fields", out var fieldsElement) ? fieldsElement : root.GetProperty("values");
            fields = values.EnumerateArray()
                .Select(field => field.GetProperty("fieldId").GetString() ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        _fieldsByIssueType[issueType.Id] = fields;
        return fields;
    }

    private static JiraIssueType? Pick(List<JiraIssueType> issueTypes, int level, string[] preferredNames)
    {
        var candidates = issueTypes.Where(type => type.HierarchyLevel == level).ToList();
        return candidates.FirstOrDefault(type => preferredNames.Contains(type.Name, StringComparer.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault(type => level != 0 || !Regex.IsMatch(type.Name, "^(bug|task|tarefa)$", RegexOptions.IgnoreCase))
            ?? candidates.FirstOrDefault();
    }

    /// <summary>Converte horas decimais para o formato do Jira ("1.5" → "1h 30m").</summary>
    internal static string ToJiraDuration(decimal hours)
    {
        var totalMinutes = (int)Math.Round(hours * 60m, MidpointRounding.AwayFromZero);
        var (h, m) = (totalMinutes / 60, totalMinutes % 60);
        return (h, m) switch
        {
            (0, 0) => "0m",
            (_, 0) => $"{h}h",
            (0, _) => $"{m}m",
            _ => $"{h}h {m}m"
        };
    }

    private static string ToJiraPriority(int priority) => priority switch
    {
        1 => "Highest",
        2 => "High",
        3 => "Medium",
        _ => "Low"
    };

    internal static string EpicLabel(string epicTitle) => $"epic-{ToLabel(epicTitle)}";

    /// <summary>Labels do Jira não aceitam espaços.</summary>
    internal static string ToLabel(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var slug = NonLabelCharsRegex().Replace(plain, "-").Trim('-');
        return slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    [GeneratedRegex(@"[^a-z0-9_\-]+")]
    private static partial Regex NonLabelCharsRegex();

    private sealed record JiraIssueType(string Id, string Name, int HierarchyLevel);
}
