using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Providers.AzureDevOps;

public sealed class AzureDevOpsBoardsOptions
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    /// <summary>Personal Access Token (escopo Work Items: Read &amp; Write) — vem de variável de ambiente, nunca hardcoded.</summary>
    public required string PersonalAccessToken { get; init; }

    public string ApiVersion { get; init; } = "7.1";

    /// <summary>Processo do projeto. Null = detectar automaticamente pelos tipos de item existentes.</summary>
    public AzureDevOpsProcess? Process { get; init; }

    public string? AreaPath { get; init; }

    public string? IterationPath { get; init; }

    /// <summary>Medida livre do documento → reference name do campo (ex: "Pontos de função" → "Custom.PontosDeFuncao").</summary>
    public IReadOnlyDictionary<string, string> CustomFieldMap { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Cria os cards no Azure Boards via REST (<c>POST _apis/wit/workitems/${type}</c>, JSON Patch), já com o
/// vínculo Hierarchy-Reverse para o pai e as estimativas SOMENTE em campos do card: Story Points (Agile),
/// Effort (Scrum/Basic) ou Size (CMMI) para pontos; horas em Original Estimate quando o tipo tem, senão
/// Effort (Epic/Feature/PBI/Bug) ou Remaining Work (Task). Antes de usar um campo, confere se ele existe no
/// tipo de item do projeto (processos herdados podem removê-lo) — medidas sem campo viram aviso, nunca texto
/// na descrição.
/// </summary>
public sealed class AzureDevOpsBoardsProvider : IWorkItemTrackerProvider
{
    private const string DescriptionField = "System.Description";
    private const string AcceptanceCriteriaField = "Microsoft.VSTS.Common.AcceptanceCriteria";
    private const string ReproStepsField = "Microsoft.VSTS.TCM.ReproSteps";
    private const string PriorityField = "Microsoft.VSTS.Common.Priority";
    private const string ActivityField = "Microsoft.VSTS.Common.Activity";

    // Medidas livres que já têm campo nativo em todos os processos padrão (Epic/Feature/PBI).
    private static readonly Dictionary<string, string> BuiltInMeasureFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Business Value"] = "Microsoft.VSTS.Common.BusinessValue",
        ["Valor de negócio"] = "Microsoft.VSTS.Common.BusinessValue",
        ["Valor de negocio"] = "Microsoft.VSTS.Common.BusinessValue",
        ["Time Criticality"] = "Microsoft.VSTS.Common.TimeCriticality",
        ["Criticidade"] = "Microsoft.VSTS.Common.TimeCriticality"
    };

    private static readonly Dictionary<string, string> ActivityAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Development"] = "Development", ["Desenvolvimento"] = "Development", ["Dev"] = "Development",
        ["Backend"] = "Development", ["Frontend"] = "Development",
        ["Testing"] = "Testing", ["Teste"] = "Testing", ["Testes"] = "Testing", ["QA"] = "Testing",
        ["Design"] = "Design", ["UX"] = "Design",
        ["Documentation"] = "Documentation", ["Documentação"] = "Documentation", ["Documentacao"] = "Documentation",
        ["Deployment"] = "Deployment", ["Deploy"] = "Deployment", ["Implantação"] = "Deployment", ["Implantacao"] = "Deployment",
        ["Requirements"] = "Requirements", ["Requisitos"] = "Requirements"
    };

    private readonly HttpClient _httpClient;
    private readonly AzureDevOpsBoardsOptions _options;
    private readonly Dictionary<string, HashSet<string>?> _fieldsByType = new(StringComparer.OrdinalIgnoreCase);
    private AzureDevOpsProcessTemplate? _template;

    public AzureDevOpsBoardsProvider(HttpClient httpClient, AzureDevOpsBoardsOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _httpClient.BaseAddress ??= new Uri($"https://dev.azure.com/{Uri.EscapeDataString(_options.Organization)}/");

        var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_options.PersonalAccessToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
    }

    public string Name => "Azure DevOps";

    public AzureDevOpsProcessTemplate? Template => _template;

    private string ProjectPath(string relativePath) => $"{Uri.EscapeDataString(_options.Project)}/_apis/{relativePath}";

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (_template is not null)
        {
            return;
        }

        if (_options.Process is { } configured)
        {
            _template = AzureDevOpsProcessTemplate.For(configured);
            return;
        }

        using var response = await _httpClient.GetAsync(ProjectPath($"wit/workitemtypes?api-version={_options.ApiVersion}"), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var names = document.RootElement.GetProperty("value").EnumerateArray()
            .Select(type => type.GetProperty("name").GetString() ?? string.Empty)
            .ToList();

        var process = AzureDevOpsProcessTemplate.Detect(names) ?? throw new InvalidOperationException(
            $"Não foi possível identificar o processo do projeto \"{_options.Project}\" (tipos: {string.Join(", ", names)}). Informe --process agile|scrum|cmmi|basic.");

        _template = AzureDevOpsProcessTemplate.For(process);
    }

    public async Task<CreatedWorkItem> CreateAsync(WorkItemCreationRequest request, CancellationToken cancellationToken = default)
    {
        await PrepareAsync(cancellationToken);
        var template = _template!;
        var type = template.TypeFor(request.Kind);
        var availableFields = await GetAvailableFieldsAsync(type, cancellationToken);
        bool Has(string field) => availableFields is null || availableFields.Contains(field);

        var warnings = new List<string>();
        var fields = new List<(string Field, object Value)> { ("System.Title", request.Title) };
        var unmapped = DescriptionComposer.DescribeEstimate(request.Estimate);

        MapEstimateFields(request, template, Has, fields, unmapped);
        MapOtherMeasures(request, Has, fields, unmapped);

        // Estimativas vão SOMENTE para campos do card, nunca para a descrição — o que não tiver campo
        // correspondente no tipo é reportado como aviso (mapeie com --field-map ou ajuste o processo).
        if (unmapped.Count > 0)
        {
            warnings.Add($"Sem campo em \"{type}\" para: {string.Join(", ", unmapped.Keys)} — não registrado(s); use --field-map ou adicione o campo ao processo.");
        }

        var criteriaInField = request.AcceptanceCriteria.Count > 0 && Has(AcceptanceCriteriaField);
        if (criteriaInField)
        {
            fields.Add((AcceptanceCriteriaField, DescriptionComposer.ToHtmlList(request.AcceptanceCriteria)));
        }

        var reproInField = request.Kind == WorkItemKind.Bug && Has(ReproStepsField);
        if (reproInField)
        {
            var reproHtml = DescriptionComposer.ToHtml(DescriptionComposer.ComposeReproSteps(request));
            if (reproHtml.Length > 0)
            {
                fields.Add((ReproStepsField, reproHtml));
            }
        }

        var description = DescriptionComposer.ToHtml(
            DescriptionComposer.Compose(request, includeAcceptanceCriteria: !criteriaInField, includeReproSteps: !reproInField));
        if (description.Length > 0)
        {
            fields.Add((DescriptionField, description));
        }

        if (request.Priority is { } priority && Has(PriorityField))
        {
            fields.Add((PriorityField, priority));
        }

        if (request.Activity is not null)
        {
            if (ActivityAliases.TryGetValue(request.Activity.Trim(), out var activity) && Has(ActivityField))
            {
                fields.Add((ActivityField, activity));
            }
            else
            {
                warnings.Add($"Atividade \"{request.Activity}\" não reconhecida pelo processo — registrada como tag.");
            }
        }

        var tags = request.Tags.Concat(template.ExtraTagsFor(request.Kind));
        if (request.Activity is not null && !ActivityAliases.ContainsKey(request.Activity.Trim()))
        {
            tags = tags.Append(request.Activity.Trim());
        }

        var tagList = string.Join("; ", tags.Distinct(StringComparer.OrdinalIgnoreCase));
        if (tagList.Length > 0)
        {
            fields.Add(("System.Tags", tagList));
        }

        if (!string.IsNullOrWhiteSpace(_options.AreaPath))
        {
            fields.Add(("System.AreaPath", _options.AreaPath));
        }

        if (!string.IsNullOrWhiteSpace(_options.IterationPath))
        {
            fields.Add(("System.IterationPath", _options.IterationPath));
        }

        var operations = new List<object>(fields.Select(field => new { op = "add", path = $"/fields/{field.Field}", value = field.Value }));
        if (request.Parent is { IsVirtual: false } parent)
        {
            operations.Add(new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "System.LinkTypes.Hierarchy-Reverse",
                    url = new Uri(_httpClient.BaseAddress!, $"_apis/wit/workItems/{parent.Id}").ToString()
                }
            });
        }

        using var content = new StringContent(JsonSerializer.Serialize(operations), Encoding.UTF8, "application/json-patch+json");
        using var response = await _httpClient.PostAsync(
            ProjectPath($"wit/workitems/${Uri.EscapeDataString(type)}?api-version={_options.ApiVersion}"), content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} ao criar \"{type}\": {ExtractErrorMessage(error)}", null, response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var id = root.GetProperty("id").GetInt32().ToString(CultureInfo.InvariantCulture);
        var url = root.TryGetProperty("_links", out var links) && links.TryGetProperty("html", out var html)
            ? html.GetProperty("href").GetString()
            : $"https://dev.azure.com/{_options.Organization}/{Uri.EscapeDataString(_options.Project)}/_workitems/edit/{id}";

        return new CreatedWorkItem(request.Kind, request.Title, id, url, type, warnings);
    }

    private static void MapEstimateFields(
        WorkItemCreationRequest request, AzureDevOpsProcessTemplate template, Func<string, bool> has,
        List<(string Field, object Value)> fields, Dictionary<string, string> unmapped)
    {
        var estimate = request.Estimate;

        if (estimate.StoryPoints is { } points && template.PointsFieldFor(request.Kind) is { } pointsField && has(pointsField))
        {
            fields.Add((pointsField, points));
            unmapped.Remove(MeasureNames.StoryPoints);
        }

        bool TryMapHours(decimal? hours, string measureName, params string[] candidateFields)
        {
            if (hours is not { } value)
            {
                return false;
            }

            // Primeiro campo que existir no tipo e ainda não estiver preenchido (ex: Effort já usado por story points).
            var field = candidateFields.FirstOrDefault(candidate => has(candidate) && fields.All(existing => existing.Field != candidate));
            if (field is null)
            {
                return false;
            }

            fields.Add((field, value));
            unmapped.Remove(measureName);
            return true;
        }

        // Horas estimadas vão para o campo de estimativa do card: Original Estimate quando o tipo tem; senão Effort
        // (Epic/Feature/PBI/Bug no Scrum); numa Task sem Original Estimate, Remaining Work.
        var originalCandidates = request.Kind == WorkItemKind.Task
            ? new[] { AzureDevOpsProcessTemplate.OriginalEstimateField, AzureDevOpsProcessTemplate.RemainingWorkField }
            : new[] { AzureDevOpsProcessTemplate.OriginalEstimateField, AzureDevOpsProcessTemplate.EffortField, AzureDevOpsProcessTemplate.SizeField };
        var originalMapped = TryMapHours(estimate.OriginalEstimateHours, MeasureNames.OriginalEstimate, originalCandidates);

        if (!TryMapHours(estimate.RemainingHours, MeasureNames.Remaining, AzureDevOpsProcessTemplate.RemainingWorkField)
            && originalMapped && estimate.RemainingHours == estimate.OriginalEstimateHours)
        {
            // Remaining Work já recebeu as horas estimadas (Task sem Original Estimate) — não é uma medida perdida.
            unmapped.Remove(MeasureNames.Remaining);
        }

        TryMapHours(estimate.CompletedHours, MeasureNames.Completed, AzureDevOpsProcessTemplate.CompletedWorkField);
    }

    private void MapOtherMeasures(
        WorkItemCreationRequest request, Func<string, bool> has, List<(string Field, object Value)> fields, Dictionary<string, string> unmapped)
    {
        foreach (var (measure, value) in request.Estimate.OtherMeasures)
        {
            if (!_options.CustomFieldMap.TryGetValue(measure, out var field) && !BuiltInMeasureFields.TryGetValue(measure, out field))
            {
                continue;
            }

            if (!has(field) || fields.Any(existing => existing.Field == field))
            {
                continue;
            }

            object typedValue = decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : value;
            fields.Add((field, typedValue));
            unmapped.Remove(measure);
        }
    }

    /// <summary>Reference names dos campos do tipo, ou null se não for possível consultá-los (assume que todos existem).</summary>
    private async Task<HashSet<string>?> GetAvailableFieldsAsync(string type, CancellationToken cancellationToken)
    {
        if (_fieldsByType.TryGetValue(type, out var cached))
        {
            return cached;
        }

        HashSet<string>? fields = null;
        using var response = await _httpClient.GetAsync(
            ProjectPath($"wit/workitemtypes/{Uri.EscapeDataString(type)}/fields?api-version={_options.ApiVersion}"), cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            fields = document.RootElement.GetProperty("value").EnumerateArray()
                .Select(field => field.GetProperty("referenceName").GetString() ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        _fieldsByType[type] = fields;
        return fields;
    }

    private static string ExtractErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("message", out var message) ? message.GetString() ?? body : body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
