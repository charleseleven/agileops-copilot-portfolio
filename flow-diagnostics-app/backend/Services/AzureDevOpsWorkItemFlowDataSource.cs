using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgilePredict.Models.Configuration;
using AgilePredict.Models.Flow;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AgilePredict.Services
{
    /// <summary>
    /// Coleta Work Items e seu histórico de transição de status via API REST do Azure DevOps
    /// (Task #212). A organização/projeto/PAT vêm por requisição via <see cref="AzureDevOpsConnection"/>
    /// (o Scrum Master pode consultar qualquer organização à qual tenha acesso), por isso o
    /// Authorization header é montado por chamada em vez de fixado no HttpClient compartilhado —
    /// evita que um PAT de uma organização vaze para requisições concorrentes de outra.
    /// </summary>
    public class AzureDevOpsWorkItemFlowDataSource : IWorkItemFlowDataSource
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private const string HttpClientName = "AzureDevOps";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AzureDevOpsConfiguration _configuration;
        private readonly FlowAnalyticsConfiguration _flowConfiguration;
        private readonly ILogger<AzureDevOpsWorkItemFlowDataSource> _logger;

        private static readonly string[] TrackedFields =
        {
            "System.Id",
            "System.Title",
            "System.State",
            "System.AssignedTo",
            "System.CreatedDate",
            "System.IterationPath"
        };

        public AzureDevOpsWorkItemFlowDataSource(
            IHttpClientFactory httpClientFactory,
            IOptions<AzureDevOpsConfiguration> configuration,
            IOptions<FlowAnalyticsConfiguration> flowConfiguration,
            ILogger<AzureDevOpsWorkItemFlowDataSource> logger)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
            _flowConfiguration = flowConfiguration?.Value ?? throw new ArgumentNullException(nameof(flowConfiguration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<IReadOnlyList<WorkItemFlowSnapshot>> GetActiveWorkItemsAsync(
            AzureDevOpsConnection connection,
            string iterationPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);
            if (string.IsNullOrWhiteSpace(iterationPath))
            {
                throw new ArgumentException("O caminho da iteração (sprint) é obrigatório", nameof(iterationPath));
            }

            var doneStatuses = string.Join(", ", _flowConfiguration.DoneStatuses.Select(Quote));
            var wiql = $@"SELECT [System.Id] FROM WorkItems
                          WHERE [System.TeamProject] = @project
                          AND [System.IterationPath] = '{Escape(iterationPath)}'
                          AND [System.State] NOT IN ({doneStatuses})
                          AND [System.State] <> 'Removed'";

            var ids = await RunWiqlAsync(connection, wiql, cancellationToken);
            return await LoadSnapshotsAsync(connection, ids, cancellationToken);
        }

        public async Task<IReadOnlyList<WorkItemFlowSnapshot>> GetCompletedWorkItemsSinceAsync(
            AzureDevOpsConnection connection,
            DateTime sinceUtc,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(connection);

            var doneStatuses = string.Join(", ", _flowConfiguration.DoneStatuses.Select(Quote));
            var wiql = $@"SELECT [System.Id] FROM WorkItems
                          WHERE [System.TeamProject] = @project
                          AND [System.State] IN ({doneStatuses})
                          AND [System.ChangedDate] >= '{sinceUtc:yyyy-MM-dd}'";

            var ids = await RunWiqlAsync(connection, wiql, cancellationToken);
            return await LoadSnapshotsAsync(connection, ids, cancellationToken);
        }

        private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

        private HttpRequestMessage CreateRequest(HttpMethod method, AzureDevOpsConnection connection, string relativePath)
        {
            var baseUri = new Uri($"{_configuration.ApiUrl.TrimEnd('/')}/{connection.Organization}/");
            var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));

            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{connection.PersonalAccessToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return request;
        }

        private async Task<List<int>> RunWiqlAsync(AzureDevOpsConnection connection, string wiql, CancellationToken cancellationToken)
        {
            var relativePath = $"{Uri.EscapeDataString(connection.Project)}/_apis/wit/wiql?api-version={_configuration.ApiVersion}";
            using var request = CreateRequest(HttpMethod.Post, connection, relativePath);
            request.Content = new StringContent(JsonSerializer.Serialize(new { query = wiql }), Encoding.UTF8, "application/json");

            using var client = CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<WiqlResult>(body, JsonOptions);

            return result?.WorkItems?.Select(w => w.Id).ToList() ?? new List<int>();
        }

        private async Task<IReadOnlyList<WorkItemFlowSnapshot>> LoadSnapshotsAsync(
            AzureDevOpsConnection connection,
            IReadOnlyList<int> ids,
            CancellationToken cancellationToken)
        {
            if (ids.Count == 0)
            {
                return Array.Empty<WorkItemFlowSnapshot>();
            }

            var details = await GetWorkItemDetailsAsync(connection, ids, cancellationToken);
            var snapshots = new List<WorkItemFlowSnapshot>(details.Count);

            foreach (var detail in details)
            {
                var history = await GetStatusHistoryAsync(connection, detail.Id, detail.CreatedAtUtc, detail.CurrentStatus, cancellationToken);
                snapshots.Add(new WorkItemFlowSnapshot
                {
                    ExternalId = detail.Id,
                    Title = detail.Title,
                    AssignedTo = detail.AssignedTo,
                    CreatedAtUtc = detail.CreatedAtUtc,
                    CurrentStatus = detail.CurrentStatus,
                    IsDone = _flowConfiguration.DoneStatuses.Contains(detail.CurrentStatus, StringComparer.OrdinalIgnoreCase),
                    StatusHistory = history
                });
            }

            return snapshots;
        }

        private async Task<List<WorkItemDetail>> GetWorkItemDetailsAsync(
            AzureDevOpsConnection connection,
            IReadOnlyList<int> ids,
            CancellationToken cancellationToken)
        {
            var relativePath = $"{Uri.EscapeDataString(connection.Project)}/_apis/wit/workitemsbatch?api-version={_configuration.ApiVersion}";
            using var request = CreateRequest(HttpMethod.Post, connection, relativePath);
            request.Content = new StringContent(JsonSerializer.Serialize(new { ids, fields = TrackedFields }), Encoding.UTF8, "application/json");

            using var client = CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);

            var details = new List<WorkItemDetail>();
            if (!document.RootElement.TryGetProperty("value", out var values))
            {
                return details;
            }

            foreach (var item in values.EnumerateArray())
            {
                var id = item.GetProperty("id").GetInt32();
                var fields = item.GetProperty("fields");

                details.Add(new WorkItemDetail
                {
                    Id = id,
                    Title = GetString(fields, "System.Title") ?? $"Work Item #{id}",
                    CurrentStatus = GetString(fields, "System.State") ?? "New",
                    AssignedTo = GetAssignedTo(fields),
                    CreatedAtUtc = GetDate(fields, "System.CreatedDate") ?? DateTime.UtcNow
                });
            }

            return details;
        }

        private async Task<List<WorkItemStatusPeriod>> GetStatusHistoryAsync(
            AzureDevOpsConnection connection,
            int workItemId,
            DateTime createdAtUtc,
            string currentStatus,
            CancellationToken cancellationToken)
        {
            var relativePath = $"{Uri.EscapeDataString(connection.Project)}/_apis/wit/workitems/{workItemId}/updates?api-version={_configuration.ApiVersion}";
            using var request = CreateRequest(HttpMethod.Get, connection, relativePath);

            using var client = CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);

            var periods = new List<WorkItemStatusPeriod>();

            if (!document.RootElement.TryGetProperty("value", out var updates))
            {
                periods.Add(new WorkItemStatusPeriod { Status = currentStatus, EnteredAtUtc = createdAtUtc });
                return periods;
            }

            foreach (var update in updates.EnumerateArray())
            {
                if (!update.TryGetProperty("fields", out var fields) ||
                    !fields.TryGetProperty("System.State", out var stateChange))
                {
                    continue;
                }

                if (!stateChange.TryGetProperty("newValue", out var newValueElement))
                {
                    continue;
                }

                var newStatus = newValueElement.GetString() ?? currentStatus;
                var revisedAt = GetDate(update, "revisedDate") ?? createdAtUtc;

                if (periods.Count > 0)
                {
                    periods[^1].ExitedAtUtc = revisedAt;
                }

                periods.Add(new WorkItemStatusPeriod { Status = newStatus, EnteredAtUtc = revisedAt });
            }

            if (periods.Count == 0)
            {
                periods.Add(new WorkItemStatusPeriod { Status = currentStatus, EnteredAtUtc = createdAtUtc });
            }
            else
            {
                periods[0].EnteredAtUtc = createdAtUtc;
            }

            return periods;
        }

        private static string? GetString(JsonElement fields, string fieldName)
        {
            return fields.TryGetProperty(fieldName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static DateTime? GetDate(JsonElement container, string fieldName)
        {
            if (!container.TryGetProperty(fieldName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String when DateTime.TryParse(
                    value.GetString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsed) => parsed,
                _ => null
            };
        }

        private static string? GetAssignedTo(JsonElement fields)
        {
            if (!fields.TryGetProperty("System.AssignedTo", out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("displayName", out var displayName))
            {
                return displayName.GetString();
            }

            return null;
        }

        private static string Quote(string value) => $"'{Escape(value)}'";

        private static string Escape(string value) => value.Replace("'", "''");

        #region DTOs internos de deserialização

        private sealed class WorkItemDetail
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string CurrentStatus { get; set; } = string.Empty;
            public string? AssignedTo { get; set; }
            public DateTime CreatedAtUtc { get; set; }
        }

        private sealed class WiqlResult
        {
            [JsonPropertyName("workItems")]
            public List<WiqlWorkItemRef>? WorkItems { get; set; }
        }

        private sealed class WiqlWorkItemRef
        {
            public int Id { get; set; }
        }

        #endregion
    }
}
