using System.Net;
using AgilePredict.Controllers;
using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.Flow;
using AgilePredict.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AgilePredict.Tests.Controllers
{
    public class FlowDiagnosticsControllerTests
    {
        private readonly Mock<IFlowDiagnosticsOrchestrator> _orchestratorMock = new();
        private readonly Mock<IAzureDevOpsBoardsUrlParser> _urlParserMock = new();
        private readonly Mock<ILogger<FlowDiagnosticsController>> _loggerMock = new();

        private FlowDiagnosticsController CreateController(AzureDevOpsConfiguration? defaults = null)
        {
            defaults ??= new AzureDevOpsConfiguration();
            return new FlowDiagnosticsController(
                _orchestratorMock.Object,
                _urlParserMock.Object,
                Options.Create(defaults),
                _loggerMock.Object);
        }

        [Fact]
        public async Task GetDiagnostics_WithBoardsUrl_ResolvesConnectionAndReturnsReport()
        {
            _urlParserMock
                .Setup(p => p.Parse("https://dev.azure.com/eleven11C/Applied%20AI%20Engineering/_workitems/edit/211"))
                .Returns(new AzureDevOpsProjectReference("eleven11C", "Applied AI Engineering"));

            var expectedReport = new FlowDiagnosticReport();
            AzureDevOpsConnection? capturedConnection = null;

            _orchestratorMock
                .Setup(o => o.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), "Applied AI Engineering\\Sprint 1", It.IsAny<CancellationToken>()))
                .Callback<AzureDevOpsConnection, string, CancellationToken>((c, _, _) => capturedConnection = c)
                .ReturnsAsync(expectedReport);

            var controller = CreateController();
            var request = new FlowDiagnosticsRequest
            {
                BoardsUrl = "https://dev.azure.com/eleven11C/Applied%20AI%20Engineering/_workitems/edit/211",
                IterationPath = "Applied AI Engineering\\Sprint 1",
                PersonalAccessToken = "pat123"
            };

            var result = await controller.GetDiagnostics(request);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(expectedReport, okResult.Value);
            Assert.Equal("eleven11C", capturedConnection!.Organization);
            Assert.Equal("Applied AI Engineering", capturedConnection.Project);
            Assert.Equal("pat123", capturedConnection.PersonalAccessToken);
        }

        [Fact]
        public async Task GetDiagnostics_WithSprintUrlPastedIntoIterationPath_ExtractsIterationPathInstead()
        {
            const string sprintUrl = "https://dev.azure.com/contoso/Contoso%20Projetos/_sprints/taskboard/Time/Contoso%20Projetos/2026-09-Sprint-17";

            _urlParserMock
                .Setup(p => p.Parse(sprintUrl))
                .Returns(new AzureDevOpsProjectReference("contoso", "Contoso Projetos"));
            _urlParserMock
                .Setup(p => p.TryExtractIterationPath(sprintUrl))
                .Returns("Contoso Projetos\\2026-09-Sprint-17");

            var expectedReport = new FlowDiagnosticReport();
            _orchestratorMock
                .Setup(o => o.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), "Contoso Projetos\\2026-09-Sprint-17", It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedReport);

            var controller = CreateController();
            var request = new FlowDiagnosticsRequest
            {
                BoardsUrl = sprintUrl,
                IterationPath = sprintUrl, // usuário colou a mesma URL por engano
                PersonalAccessToken = "pat123"
            };

            var result = await controller.GetDiagnostics(request);

            Assert.IsType<OkObjectResult>(result.Result);
            _orchestratorMock.Verify(o => o.GenerateReportAsync(
                It.IsAny<AzureDevOpsConnection>(), "Contoso Projetos\\2026-09-Sprint-17", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetDiagnostics_WithUnrecognizableUrlInIterationPath_ReturnsBadRequest()
        {
            const string workItemUrl = "https://dev.azure.com/eleven11C/Applied%20AI%20Engineering/_workitems/edit/211";

            _urlParserMock.Setup(p => p.Parse(workItemUrl))
                .Returns(new AzureDevOpsProjectReference("eleven11C", "Applied AI Engineering"));
            _urlParserMock.Setup(p => p.TryExtractIterationPath(workItemUrl)).Returns((string?)null);

            var controller = CreateController();
            var request = new FlowDiagnosticsRequest
            {
                BoardsUrl = workItemUrl,
                IterationPath = workItemUrl,
                PersonalAccessToken = "pat123"
            };

            var result = await controller.GetDiagnostics(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            _orchestratorMock.Verify(o => o.GenerateReportAsync(
                It.IsAny<AzureDevOpsConnection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetDiagnostics_WithoutBoardsUrlOrOrganizationOrDefault_ReturnsBadRequest()
        {
            var controller = CreateController();
            var request = new FlowDiagnosticsRequest
            {
                IterationPath = "Sprint 1",
                PersonalAccessToken = "pat123"
            };

            var result = await controller.GetDiagnostics(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetDiagnostics_WithoutPersonalAccessTokenOrDefault_ReturnsBadRequest()
        {
            _urlParserMock.Setup(p => p.Parse(It.IsAny<string>()))
                .Returns(new AzureDevOpsProjectReference("org", "proj"));

            var controller = CreateController();
            var request = new FlowDiagnosticsRequest
            {
                BoardsUrl = "https://dev.azure.com/org/proj/_workitems/edit/1",
                IterationPath = "Sprint 1"
            };

            var result = await controller.GetDiagnostics(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized, "inválido")]
        [InlineData(HttpStatusCode.Forbidden, "permissão")]
        [InlineData(HttpStatusCode.NotFound, "não encontrados")]
        [InlineData(HttpStatusCode.BadRequest, "iteração")]
        public async Task GetDiagnostics_WhenAzureDevOpsReturnsError_MapsToSpecificFriendlyMessage(HttpStatusCode statusCode, string expectedSubstring)
        {
            _urlParserMock.Setup(p => p.Parse(It.IsAny<string>()))
                .Returns(new AzureDevOpsProjectReference("org", "proj"));

            _orchestratorMock
                .Setup(o => o.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("boom", null, statusCode));

            var controller = CreateController();
            var request = new FlowDiagnosticsRequest
            {
                BoardsUrl = "https://dev.azure.com/org/proj/_workitems/edit/1",
                IterationPath = "Sprint 1",
                PersonalAccessToken = "pat123"
            };

            var result = await controller.GetDiagnostics(request);

            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
            var messageProperty = objectResult.Value!.GetType().GetProperty("message");
            var message = (string)messageProperty!.GetValue(objectResult.Value)!;
            Assert.Contains(expectedSubstring, message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetDiagnostics_UsesServerDefaultsWhenRequestOmitsOrganizationProjectAndToken()
        {
            var defaults = new AzureDevOpsConfiguration
            {
                Organization = "default-org",
                Project = "default-project",
                PersonalAccessToken = "default-pat"
            };

            var expectedReport = new FlowDiagnosticReport();
            AzureDevOpsConnection? capturedConnection = null;

            _orchestratorMock
                .Setup(o => o.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), "Sprint 1", It.IsAny<CancellationToken>()))
                .Callback<AzureDevOpsConnection, string, CancellationToken>((c, _, _) => capturedConnection = c)
                .ReturnsAsync(expectedReport);

            var controller = CreateController(defaults);
            var request = new FlowDiagnosticsRequest { IterationPath = "Sprint 1" };

            var result = await controller.GetDiagnostics(request);

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal("default-org", capturedConnection!.Organization);
            Assert.Equal("default-project", capturedConnection.Project);
            Assert.Equal("default-pat", capturedConnection.PersonalAccessToken);
        }

        [Fact]
        public async Task GetDiagnostics_WithBoardsUrlForNonDefaultOrgAndNoToken_UsesOrganizationSpecificDefaultToken()
        {
            const string boardsUrl = "https://dev.azure.com/geosaude/Projeto/_workitems/edit/1";

            _urlParserMock.Setup(p => p.Parse(boardsUrl))
                .Returns(new AzureDevOpsProjectReference("geosaude", "Projeto"));

            var defaults = new AzureDevOpsConfiguration
            {
                Organization = "default-org",
                Project = "default-project",
                PersonalAccessToken = "default-pat",
                OrganizationTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["geosaude"] = "geosaude-pat"
                }
            };

            var expectedReport = new FlowDiagnosticReport();
            AzureDevOpsConnection? capturedConnection = null;

            _orchestratorMock
                .Setup(o => o.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), "Sprint 1", It.IsAny<CancellationToken>()))
                .Callback<AzureDevOpsConnection, string, CancellationToken>((c, _, _) => capturedConnection = c)
                .ReturnsAsync(expectedReport);

            var controller = CreateController(defaults);
            var request = new FlowDiagnosticsRequest { BoardsUrl = boardsUrl, IterationPath = "Sprint 1" };

            var result = await controller.GetDiagnostics(request);

            Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal("geosaude-pat", capturedConnection!.PersonalAccessToken);
        }

        [Fact]
        public async Task GetDiagnostics_WithBoardsUrlForNonDefaultOrgWithoutOrganizationSpecificToken_DoesNotReuseDefaultOrgToken()
        {
            const string boardsUrl = "https://dev.azure.com/outra-org/Projeto/_workitems/edit/1";

            _urlParserMock.Setup(p => p.Parse(boardsUrl))
                .Returns(new AzureDevOpsProjectReference("outra-org", "Projeto"));

            var defaults = new AzureDevOpsConfiguration
            {
                Organization = "default-org",
                Project = "default-project",
                PersonalAccessToken = "default-pat"
            };

            var controller = CreateController(defaults);
            var request = new FlowDiagnosticsRequest { BoardsUrl = boardsUrl, IterationPath = "Sprint 1" };

            var result = await controller.GetDiagnostics(request);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            _orchestratorMock.Verify(o => o.GenerateReportAsync(
                It.IsAny<AzureDevOpsConnection>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
