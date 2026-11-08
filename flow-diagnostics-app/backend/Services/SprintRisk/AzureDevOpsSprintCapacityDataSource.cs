using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgilePredict.Models.Configuration;
using AgilePredict.Models.Flow;
using AgilePredict.Models.SprintRisk;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AgilePredict.Services
{
    /// <summary>
    /// Coleta assíncrona da Sprint no Azure Boards (PBI 8.1, AC 1): sprint atual (ou a informada),
    /// dias úteis do time, folgas, capacidade por membro e as horas dos work items. As chamadas
    /// independentes rodam em paralelo (<see cref="Task.WhenAll(Task[])"/>), e o PAT é aplicado por
    /// requisição para não vazar entre organizações (mesmo padrão de <see cref="AzureDevOpsWorkItemFlowDataSource"/>).
    /// </summary>
    public sealed class AzureDevOpsSprintCapacityDataSource : ISprintCapacityDataSource
    {
        private const string HttpClientName = "AzureDevOps";
        private const int BatchSize = 200;

        private static readonly string[] WorkItemFields =
        {
            "System.Id",
            "System.Title",
            "System.WorkItemType",
            "System.State",
            "System.AssignedTo",
            "Microsoft.VSTS.Scheduling.OriginalEstimate",
            "Microsoft.VSTS.Scheduling.RemainingWork",
            "Microsoft.VSTS.Scheduling.CompletedWork"
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AzureDevOpsConfiguration _configuration;
        private readonly ILogger<AzureDevOpsSprintCapacityDataSource> _logger;

        public AzureDevOpsSprintCapacityDataSource(
            IHttpClientFactory httpClientFactory,
            IOptions<AzureDevOpsConfiguration> configuration,
            ILogger<AzureDevOpsSprintCapacityDataSource> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Nome do time padrão que o Azure DevOps cria junto com o projeto.</summary>
        public static string DefaultTeamName(string project) => $"{project} Team";

        public async Task<SprintCapacitySnapshot> GetSnapshotAsync(
            AzureDevOpsConnection connection,
            string? team,
            string? iterationPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            var teamName = string.IsNullOrWhiteSpace(team) ? DefaultTeamName(connection.Project) : team.Trim();
            var teamBase = $"{Escape(connection.Project)}/{Escape(teamName)}/_apis/work/teamsettings";

            var iterationTask = ResolveIterationAsync(connection, teamName, teamBase, iterationPath, cancellationToken);
            var workingDaysTask = GetWorkingDaysAsync(connection, teamBase, cancellationToken);
            // WhenAll observa as duas tarefas; se ambas falharem, propaga a da iteração (a primeira da lista).
            await Task.WhenAll(iterationTask, workingDaysTask);
            var iteration = iterationTask.Result;

            var capacityTask = GetCapacitiesAsync(connection, teamBase, iteration.Id, cancellationToken);
            var teamDaysOffTask = GetTeamDaysOffAsync(connection, teamBase, iteration.Id, cancellationToken);
            var workItemsTask = GetWorkItemsAsync(connection, iteration.Path, cancellationToken);
            await Task.WhenAll(capacityTask, teamDaysOffTask, workItemsTask);

            _logger.LogInformation(
                "Sprint {Sprint} coletada para {Organization}/{Project}/{Team}: {Members} membros com capacidade, {Items} work items",
                iteration.Path, connection.Organization, connection.Project, teamName, capacityTask.Result.Count, workItemsTask.Result.Count);

            return new SprintCapacitySnapshot
            {
                Organization = connection.Organization,
                Project = connection.Project,
                Team = teamName,
                Iteration = iteration,
                WorkingDays = workingDaysTask.Result,
                TeamDaysOff = teamDaysOffTask.Result,
                Members = capacityTask.Result,
                WorkItems = workItemsTask.Result,
                CollectedAtUtc = DateTime.UtcNow
            };
        }

        private async Task<SprintIteration> ResolveIterationAsync(
            AzureDevOpsConnection connection, string teamName, string teamBase, string? iterationPath, CancellationToken ct)
        {
            var wantsCurrent = string.IsNullOrWhiteSpace(iterationPath);
            var path = $"{teamBase}/iterations?{(wantsCurrent ? "$timeframe=current&" : "")}api-version={_configuration.ApiVersion}";

            JsonDocument document;
            try
            {
                document = await GetJsonAsync(connection, HttpMethod.Get, path, null, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SprintNotFoundException(
                    $"Time '{teamName}' não encontrado no projeto '{connection.Project}'. Informe o nome exato do time (campo team).");
            }

            using (document)
            {
                var iterations = document.RootElement.TryGetProperty("value", out var values)
                    ? values.EnumerateArray().Select(ParseIteration).Where(i => i is not null).Select(i => i!).ToList()
                    : new List<SprintIteration>();

                if (wantsCurrent)
                {
                    return iterations.FirstOrDefault() ?? throw new SprintNotFoundException(
                        $"O time '{teamName}' não tem sprint atual. Informe a sprint (campo iterationPath) ou defina as datas das iterações do time.");
                }

                var wanted = iterationPath!.Trim().Trim('\\');
                return iterations.FirstOrDefault(i => string.Equals(i.Path, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? iterations.FirstOrDefault(i => string.Equals(i.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? throw new SprintNotFoundException(
                        $"A sprint '{wanted}' não está entre as iterações do time '{teamName}' (ou não tem datas de início e fim).");
            }
        }

        private static SprintIteration? ParseIteration(JsonElement element)
        {
            if (!element.TryGetProperty("attributes", out var attributes)) return null;
            var start = ParseDate(attributes, "startDate");
            var finish = ParseDate(attributes, "finishDate");
            if (start is null || finish is null) return null; // sprint sem datas não permite calcular dias úteis

            return new SprintIteration(
                element.GetProperty("id").GetString() ?? string.Empty,
                element.GetProperty("name").GetString() ?? string.Empty,
                element.GetProperty("path").GetString() ?? string.Empty,
                start.Value,
                finish.Value);
        }

        private async Task<IReadOnlyList<DayOfWeek>> GetWorkingDaysAsync(AzureDevOpsConnection connection, string teamBase, CancellationToken ct)
        {
            using var document = await GetJsonAsync(connection, HttpMethod.Get, $"{teamBase}?api-version={_configuration.ApiVersion}", null, ct);
            if (!document.RootElement.TryGetProperty("workingDays", out var days) || days.ValueKind != JsonValueKind.Array)
            {
                return SprintCapacitySnapshot.DefaultWorkingDays;
            }

            var parsed = days.EnumerateArray()
                .Select(d => Enum.TryParse<DayOfWeek>(d.GetString(), ignoreCase: true, out var day) ? day : (DayOfWeek?)null)
                .Where(d => d is not null)
                .Select(d => d!.Value)
                .ToList();
            return parsed.Count > 0 ? parsed : SprintCapacitySnapshot.DefaultWorkingDays;
        }

        private async Task<IReadOnlyList<TeamMemberCapacity>> GetCapacitiesAsync(
            AzureDevOpsConnection connection, string teamBase, string iterationId, CancellationToken ct)
        {
            using var document = await GetJsonAsync(connection, HttpMethod.Get,
                $"{teamBase}/iterations/{iterationId}/capacities?api-version={_configuration.ApiVersion}", null, ct);

            // api-version 7.x devolve { teamMembers: [...] }; versões anteriores, { value: [...] }.
            var root = document.RootElement;
            if (!root.TryGetProperty("teamMembers", out var members) && !root.TryGetProperty("value", out members))
            {
                return Array.Empty<TeamMemberCapacity>();
            }

            var result = new List<TeamMemberCapacity>();
            foreach (var member in members.EnumerateArray())
            {
                var name = member.TryGetProperty("teamMember", out var tm) && tm.TryGetProperty("displayName", out var dn)
                    ? dn.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var perDay = member.TryGetProperty("activities", out var activities)
                    ? activities.EnumerateArray().Sum(a => a.TryGetProperty("capacityPerDay", out var c) && c.TryGetDouble(out var v) ? v : 0)
                    : 0;

                result.Add(new TeamMemberCapacity(name, perDay, ParseDaysOff(member)));
            }

            return result;
        }

        private async Task<IReadOnlyList<DateRange>> GetTeamDaysOffAsync(
            AzureDevOpsConnection connection, string teamBase, string iterationId, CancellationToken ct)
        {
            using var document = await GetJsonAsync(connection, HttpMethod.Get,
                $"{teamBase}/iterations/{iterationId}/teamdaysoff?api-version={_configuration.ApiVersion}", null, ct);
            return ParseDaysOff(document.RootElement);
        }

        private async Task<IReadOnlyList<SprintWorkItem>> GetWorkItemsAsync(
            AzureDevOpsConnection connection, string iterationPath, CancellationToken ct)
        {
            var wiql = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project " +
                       $"AND [System.IterationPath] = '{iterationPath.Replace("'", "''")}'";
            var project = Escape(connection.Project);

            List<int> ids;
            using (var document = await GetJsonAsync(connection, HttpMethod.Post,
                       $"{project}/_apis/wit/wiql?api-version={_configuration.ApiVersion}", new { query = wiql }, ct))
            {
                ids = document.RootElement.TryGetProperty("workItems", out var refs)
                    ? refs.EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToList()
                    : new List<int>();
            }

            if (ids.Count == 0) return Array.Empty<SprintWorkItem>();

            var batches = await Task.WhenAll(ids.Chunk(BatchSize).Select(chunk => GetJsonAsync(connection, HttpMethod.Post,
                $"{project}/_apis/wit/workitemsbatch?api-version={_configuration.ApiVersion}",
                new { ids = chunk, fields = WorkItemFields }, ct)));

            var items = new List<SprintWorkItem>(ids.Count);
            foreach (var batch in batches)
            {
                using (batch)
                {
                    if (!batch.RootElement.TryGetProperty("value", out var values)) continue;
                    foreach (var item in values.EnumerateArray())
                    {
                        var fields = item.GetProperty("fields");
                        items.Add(new SprintWorkItem
                        {
                            Id = item.GetProperty("id").GetInt32(),
                            Title = GetString(fields, "System.Title") ?? string.Empty,
                            WorkItemType = GetString(fields, "System.WorkItemType") ?? string.Empty,
                            State = GetString(fields, "System.State") ?? string.Empty,
                            AssignedTo = GetAssignedTo(fields),
                            OriginalEstimate = GetDouble(fields, "Microsoft.VSTS.Scheduling.OriginalEstimate"),
                            RemainingWork = GetDouble(fields, "Microsoft.VSTS.Scheduling.RemainingWork"),
                            CompletedWork = GetDouble(fields, "Microsoft.VSTS.Scheduling.CompletedWork")
                        });
                    }
                }
            }

            return items;
        }

        private async Task<JsonDocument> GetJsonAsync(
            AzureDevOpsConnection connection, HttpMethod method, string relativePath, object? body, CancellationToken ct)
        {
            var baseUri = new Uri($"{_configuration.ApiUrl.TrimEnd('/')}/{Escape(connection.Organization)}/");
            using var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{connection.PersonalAccessToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }

            using var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }

        private static IReadOnlyList<DateRange> ParseDaysOff(JsonElement container)
        {
            if (!container.TryGetProperty("daysOff", out var daysOff) || daysOff.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DateRange>();
            }

            return daysOff.EnumerateArray()
                .Select(d => (Start: ParseDate(d, "start"), End: ParseDate(d, "end")))
                .Where(d => d.Start is not null && d.End is not null)
                .Select(d => new DateRange(d.Start!.Value, d.End!.Value))
                .ToList();
        }

        private static DateOnly? ParseDate(JsonElement container, string property)
        {
            if (!container.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return null;
            // As datas de sprint/folga do Azure DevOps são dias "puros" em meia-noite UTC.
            return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? DateOnly.FromDateTime(parsed.UtcDateTime)
                : null;
        }

        private static string? GetString(JsonElement fields, string name) =>
            fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private static double? GetDouble(JsonElement fields, string name) =>
            fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

        private static string? GetAssignedTo(JsonElement fields)
        {
            if (!fields.TryGetProperty("System.AssignedTo", out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Object when value.TryGetProperty("displayName", out var name) => name.GetString(),
                _ => null
            };
        }

        private static string Escape(string value) => Uri.EscapeDataString(value);
    }
}
