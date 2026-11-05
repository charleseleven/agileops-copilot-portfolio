using System.Net;
using AgilePredict.Models.Configuration;
using AgilePredict.Models.Flow;
using AgilePredict.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace AgilePredict.Tests.Services.Flow
{
    public class AzureDevOpsWorkItemFlowDataSourceTests
    {
        private readonly Mock<ILogger<AzureDevOpsWorkItemFlowDataSource>> _loggerMock = new();

        private readonly AzureDevOpsConfiguration _config = new()
        {
            ApiUrl = "https://dev.azure.com/",
            ApiVersion = "7.1"
        };

        private readonly FlowAnalyticsConfiguration _flowConfig = new()
        {
            DoneStatuses = new List<string> { "Done" }
        };

        private static Mock<IHttpClientFactory> CreateFactoryMock(Mock<HttpMessageHandler> handlerMock)
        {
            var factoryMock = new Mock<IHttpClientFactory>();
            // disposeHandler: false — o data source descarta o HttpClient retornado por chamada
            // (padrão correto de IHttpClientFactory), o que por padrão também descartaria o
            // handler mockado (compartilhado entre chamadas) e quebraria a chamada seguinte.
            factoryMock
                .Setup(f => f.CreateClient("AzureDevOps"))
                .Returns(() => new HttpClient(handlerMock.Object, disposeHandler: false));
            return factoryMock;
        }

        private AzureDevOpsWorkItemFlowDataSource CreateDataSource(Mock<HttpMessageHandler> handlerMock)
        {
            return new AzureDevOpsWorkItemFlowDataSource(
                CreateFactoryMock(handlerMock).Object,
                Options.Create(_config),
                Options.Create(_flowConfig),
                _loggerMock.Object);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

        [Fact]
        public async Task GetActiveWorkItemsAsync_BuildsSnapshotWithStatusHistoryAndAssignee()
        {
            var handlerMock = new Mock<HttpMessageHandler>();

            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.EndsWith("/wiql")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() => JsonResponse("{\"workItems\":[{\"id\":101}]}"));

            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.EndsWith("/workitemsbatch")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() => JsonResponse(@"{
                    ""value"": [{
                        ""id"": 101,
                        ""fields"": {
                            ""System.Title"": ""Card Test"",
                            ""System.State"": ""In Progress"",
                            ""System.AssignedTo"": { ""displayName"": ""Jane Doe"" },
                            ""System.CreatedDate"": ""2026-01-01T00:00:00Z""
                        }
                    }]
                }"));

            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsolutePath.EndsWith("/updates")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() => JsonResponse(@"{
                    ""value"": [
                        { ""rev"": 1, ""revisedDate"": ""2026-01-01T00:00:00Z"", ""fields"": { ""System.State"": { ""newValue"": ""To Do"" } } },
                        { ""rev"": 2, ""revisedDate"": ""2026-01-02T00:00:00Z"", ""fields"": { ""System.State"": { ""oldValue"": ""To Do"", ""newValue"": ""In Progress"" } } }
                    ]
                }"));

            var dataSource = CreateDataSource(handlerMock);
            var connection = new AzureDevOpsConnection("org", "proj", "pat123");

            var result = await dataSource.GetActiveWorkItemsAsync(connection, "proj\\Sprint 1");

            var snapshot = Assert.Single(result);
            Assert.Equal(101, snapshot.ExternalId);
            Assert.Equal("Card Test", snapshot.Title);
            Assert.Equal("Jane Doe", snapshot.AssignedTo);
            Assert.Equal("In Progress", snapshot.CurrentStatus);
            Assert.False(snapshot.IsDone);

            Assert.Equal(2, snapshot.StatusHistory.Count);
            Assert.Equal("To Do", snapshot.StatusHistory[0].Status);
            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.StatusHistory[0].EnteredAtUtc);
            Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), snapshot.StatusHistory[0].ExitedAtUtc);

            Assert.Equal("In Progress", snapshot.StatusHistory[1].Status);
            Assert.Null(snapshot.StatusHistory[1].ExitedAtUtc);
        }

        [Fact]
        public async Task GetActiveWorkItemsAsync_WhenNoWorkItemsMatch_ReturnsEmptyWithoutFurtherCalls()
        {
            var handlerMock = new Mock<HttpMessageHandler>();

            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() => JsonResponse("{\"workItems\":[]}"));

            var dataSource = CreateDataSource(handlerMock);
            var connection = new AzureDevOpsConnection("org", "proj", "pat123");

            var result = await dataSource.GetActiveWorkItemsAsync(connection, "proj\\Sprint 1");

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetActiveWorkItemsAsync_UsesOrganizationAndProjectFromConnectionInRequestUrl()
        {
            HttpRequestMessage? capturedRequest = null;
            var handlerMock = new Mock<HttpMessageHandler>();

            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest ??= req)
                .ReturnsAsync(() => JsonResponse("{\"workItems\":[]}"));

            var dataSource = CreateDataSource(handlerMock);
            var connection = new AzureDevOpsConnection("contoso", "Contoso Projetos", "pat-abc");

            await dataSource.GetActiveWorkItemsAsync(connection, "Sprint 1");

            Assert.NotNull(capturedRequest);
            // AbsoluteUri preserva o escaping (Uri.ToString() decodifica de volta para exibição).
            var url = capturedRequest!.RequestUri!.AbsoluteUri;
            Assert.Contains("dev.azure.com/contoso/", url);
            Assert.Contains(Uri.EscapeDataString("Contoso Projetos"), url);

            var expectedToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":pat-abc"));
            Assert.Equal("Basic", capturedRequest.Headers.Authorization!.Scheme);
            Assert.Equal(expectedToken, capturedRequest.Headers.Authorization.Parameter);
        }

        [Fact]
        public async Task GetActiveWorkItemsAsync_WithDifferentConnections_NeverMixesUpPersonalAccessTokens()
        {
            var capturedTokens = new List<string?>();
            var handlerMock = new Mock<HttpMessageHandler>();

            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedTokens.Add(req.Headers.Authorization?.Parameter))
                .ReturnsAsync(() => JsonResponse("{\"workItems\":[]}"));

            var dataSource = CreateDataSource(handlerMock);

            await dataSource.GetActiveWorkItemsAsync(new AzureDevOpsConnection("org-a", "proj-a", "pat-a"), "Sprint 1");
            await dataSource.GetActiveWorkItemsAsync(new AzureDevOpsConnection("org-b", "proj-b", "pat-b"), "Sprint 1");

            var tokenA = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":pat-a"));
            var tokenB = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(":pat-b"));

            Assert.Equal(new List<string?> { tokenA, tokenB }, capturedTokens);
        }
    }
}
