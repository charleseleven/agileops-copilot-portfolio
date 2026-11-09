using System.Net;
using System.Text;
using AgilePredict.Models.Configuration;
using AgilePredict.Models.Flow;
using AgilePredict.Services;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AgilePredict.Tests.Services.SprintRisk
{
    public class AzureDevOpsSprintCapacityDataSourceTests
    {
        private static readonly AzureDevOpsConnection Connection = new("contoso", "Meu Projeto", "pat-123");

        private const string CurrentIteration = """
            {"value":[{"id":"it-1","name":"Sprint 8","path":"Meu Projeto\\Sprint 8",
              "attributes":{"startDate":"2026-11-02T00:00:00Z","finishDate":"2026-11-13T00:00:00Z","timeFrame":"current"}}]}
            """;

        /// <summary>Handler que responde por trecho de URL e registra as requisições recebidas.</summary>
        private sealed class RoutingHandler(Dictionary<string, (HttpStatusCode Status, string Body)> routes) : HttpMessageHandler
        {
            public List<HttpRequestMessage> Requests { get; } = new();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                lock (Requests) Requests.Add(request);
                var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
                var route = routes.OrderByDescending(r => r.Key.Length).FirstOrDefault(r => url.Contains(r.Key));
                if (route.Key is null)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
                }
                return Task.FromResult(new HttpResponseMessage(route.Value.Status)
                {
                    Content = new StringContent(route.Value.Body, Encoding.UTF8, "application/json")
                });
            }
        }

        private static Dictionary<string, (HttpStatusCode, string)> HappyRoutes(string iterations = CurrentIteration) => new()
        {
            ["/_apis/work/teamsettings/iterations?"] = (HttpStatusCode.OK, iterations),
            ["/_apis/work/teamsettings?"] = (HttpStatusCode.OK, """{"workingDays":["monday","tuesday","wednesday","thursday"]}"""),
            ["/iterations/it-1/capacities"] = (HttpStatusCode.OK, """
                {"teamMembers":[
                  {"teamMember":{"displayName":"Ana"},"activities":[{"capacityPerDay":4,"name":"Development"},{"capacityPerDay":2,"name":"Testing"}],
                   "daysOff":[{"start":"2026-11-10T00:00:00Z","end":"2026-11-11T00:00:00Z"}]},
                  {"teamMember":{"displayName":"Bia"},"activities":[{"capacityPerDay":0,"name":""}],"daysOff":[]}
                ],"totalCapacityPerDay":6}
                """),
            ["/iterations/it-1/teamdaysoff"] = (HttpStatusCode.OK, """{"daysOff":[{"start":"2026-11-05T00:00:00Z","end":"2026-11-05T00:00:00Z"}]}"""),
            ["/_apis/wit/wiql"] = (HttpStatusCode.OK, """{"workItems":[{"id":10},{"id":11}]}"""),
            ["/_apis/wit/workitemsbatch"] = (HttpStatusCode.OK, """
                {"value":[
                  {"id":10,"fields":{"System.Title":"Task A","System.WorkItemType":"Task","System.State":"In Progress",
                    "System.AssignedTo":{"displayName":"Ana"},"Microsoft.VSTS.Scheduling.OriginalEstimate":8,
                    "Microsoft.VSTS.Scheduling.RemainingWork":5,"Microsoft.VSTS.Scheduling.CompletedWork":3}},
                  {"id":11,"fields":{"System.Title":"PBI","System.WorkItemType":"Product Backlog Item","System.State":"New"}}
                ]}
                """)
        };

        private static (AzureDevOpsSprintCapacityDataSource DataSource, RoutingHandler Handler) Create(
            Dictionary<string, (HttpStatusCode, string)> routes)
        {
            var handler = new RoutingHandler(routes);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient("AzureDevOps")).Returns(() => new HttpClient(handler, disposeHandler: false));
            var dataSource = new AzureDevOpsSprintCapacityDataSource(
                factory.Object,
                Options.Create(new AzureDevOpsConfiguration { ApiUrl = "https://dev.azure.com/", ApiVersion = "7.1" }),
                Mock.Of<ILogger<AzureDevOpsSprintCapacityDataSource>>());
            return (dataSource, handler);
        }

        [Fact]
        public async Task GetSnapshotAsync_SprintAtual_MontaRetratoComCalendarioCapacidadeEHoras()
        {
            var (dataSource, _) = Create(HappyRoutes());

            var snapshot = await dataSource.GetSnapshotAsync(Connection, team: null, iterationPath: null);

            Assert.Equal("Meu Projeto Team", snapshot.Team);
            Assert.Equal("Meu Projeto\\Sprint 8", snapshot.Iteration.Path);
            Assert.Equal(new DateOnly(2026, 11, 2), snapshot.Iteration.StartDate);
            Assert.Equal(new DateOnly(2026, 11, 13), snapshot.Iteration.FinishDate);
            Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday }, snapshot.WorkingDays);
            Assert.Equal(new DateOnly(2026, 11, 5), Assert.Single(snapshot.TeamDaysOff).Start);

            Assert.Equal(2, snapshot.Members.Count);
            var ana = snapshot.Members[0];
            Assert.Equal("Ana", ana.DisplayName);
            Assert.Equal(6, ana.CapacityPerDay);                       // soma das atividades
            Assert.Equal(new DateOnly(2026, 11, 11), Assert.Single(ana.DaysOff).End);

            var task = Assert.Single(snapshot.WorkItems, w => w.Id == 10);
            Assert.Equal("Ana", task.AssignedTo);
            Assert.Equal(8, task.OriginalEstimate);
            Assert.Equal(5, task.RemainingWork);
            Assert.Equal(3, task.CompletedWork);
            Assert.Null(Assert.Single(snapshot.WorkItems, w => w.Id == 11).OriginalEstimate);
        }

        [Fact]
        public async Task GetSnapshotAsync_UsaOrganizacaoProjetoTimeEPatDaConexaoEmTodasAsChamadas()
        {
            var (dataSource, handler) = Create(HappyRoutes());

            await dataSource.GetSnapshotAsync(Connection, team: "Time Dev", iterationPath: null);

            Assert.Equal(6, handler.Requests.Count);
            Assert.All(handler.Requests, r =>
            {
                Assert.StartsWith("https://dev.azure.com/contoso/Meu%20Projeto/", r.RequestUri!.AbsoluteUri);
                Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes(":pat-123")), r.Headers.Authorization!.Parameter);
            });
            Assert.Contains(handler.Requests, r => r.RequestUri!.AbsoluteUri.Contains("/Meu%20Projeto/Time%20Dev/_apis/work/teamsettings/iterations?$timeframe=current"));
            Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/_apis/wit/wiql") && r.Method == HttpMethod.Post);
        }

        [Fact]
        public async Task GetSnapshotAsync_SprintInformadaPeloNome_ResolveEntreAsIteracoesDoTime()
        {
            var iterations = """
                {"value":[
                  {"id":"it-0","name":"Sprint 7","path":"Meu Projeto\\Sprint 7","attributes":{"startDate":"2026-10-19T00:00:00Z","finishDate":"2026-10-30T00:00:00Z"}},
                  {"id":"it-1","name":"Sprint 8","path":"Meu Projeto\\Sprint 8","attributes":{"startDate":"2026-11-02T00:00:00Z","finishDate":"2026-11-13T00:00:00Z"}}
                ]}
                """;
            var (dataSource, handler) = Create(HappyRoutes(iterations));

            var snapshot = await dataSource.GetSnapshotAsync(Connection, null, "sprint 8");

            Assert.Equal("it-1", snapshot.Iteration.Id);
            Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Query.Contains("timeframe"));
        }

        [Fact]
        public async Task GetSnapshotAsync_SprintInexistente_LancaSprintNotFound()
        {
            var (dataSource, _) = Create(HappyRoutes());

            var ex = await Assert.ThrowsAsync<SprintNotFoundException>(() => dataSource.GetSnapshotAsync(Connection, null, "Meu Projeto\\Sprint 99"));

            Assert.Contains("Sprint 99", ex.Message);
        }

        [Fact]
        public async Task GetSnapshotAsync_TimeSemSprintAtual_LancaSprintNotFound()
        {
            var (dataSource, _) = Create(HappyRoutes("""{"value":[]}"""));

            await Assert.ThrowsAsync<SprintNotFoundException>(() => dataSource.GetSnapshotAsync(Connection, null, null));
        }

        [Fact]
        public async Task GetSnapshotAsync_TimeInexistente_LancaSprintNotFoundComNomeDoTime()
        {
            var routes = HappyRoutes();
            routes["/_apis/work/teamsettings/iterations?"] = (HttpStatusCode.NotFound, "{}");
            routes["/_apis/work/teamsettings?"] = (HttpStatusCode.NotFound, "{}");
            var (dataSource, _) = Create(routes);

            var ex = await Assert.ThrowsAsync<SprintNotFoundException>(() => dataSource.GetSnapshotAsync(Connection, "Time X", null));

            Assert.Contains("Time X", ex.Message);
        }

        [Fact]
        public async Task GetSnapshotAsync_404SoNosDiasUteisDoTime_LancaSprintNotFoundEmVezDeErroGenerico()
        {
            var routes = HappyRoutes();
            routes["/_apis/work/teamsettings?"] = (HttpStatusCode.NotFound, "{}");
            var (dataSource, _) = Create(routes);

            var ex = await Assert.ThrowsAsync<SprintNotFoundException>(() => dataSource.GetSnapshotAsync(Connection, null, null));

            Assert.Contains("Meu Projeto Team", ex.Message);
        }

        [Fact]
        public async Task GetSnapshotAsync_PatInvalido_PropagaHttpRequestExceptionComStatus()
        {
            var routes = HappyRoutes();
            routes["/_apis/work/teamsettings/iterations?"] = (HttpStatusCode.Unauthorized, "");
            var (dataSource, _) = Create(routes);

            var ex = await Assert.ThrowsAsync<HttpRequestException>(() => dataSource.GetSnapshotAsync(Connection, null, null));

            Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        }

        [Fact]
        public async Task GetSnapshotAsync_SprintSemWorkItems_NaoChamaOBatch()
        {
            var routes = HappyRoutes();
            routes["/_apis/wit/wiql"] = (HttpStatusCode.OK, """{"workItems":[]}""");
            var (dataSource, handler) = Create(routes);

            var snapshot = await dataSource.GetSnapshotAsync(Connection, null, null);

            Assert.Empty(snapshot.WorkItems);
            Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("workitemsbatch"));
        }
    }
}
