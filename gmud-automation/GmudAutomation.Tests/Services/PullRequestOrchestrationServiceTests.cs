using FluentAssertions;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Services;

public class PullRequestOrchestrationServiceTests
{
    private readonly Mock<IAzureDevOpsClient> _clientMock = new();
    private readonly PullRequestOrchestrationService _sut;

    public PullRequestOrchestrationServiceTests()
    {
        _sut = new PullRequestOrchestrationService(_clientMock.Object);
    }

    [Fact]
    public async Task OrchestrateAsync_PrDeDatabase_RetornaOLinkSemInvocarOMetodoDeAprovacao()
    {
        var request = new RepositoryPullRequestRequest(
            "Contoso.Legado.Database", RepositoryKind.Database, "release/GMUD_PROD_Contoso_10092026", "main", "Título", "Descrição");
        var pullRequest = new PullRequestInfo(7763, "https://dev.azure.com/contoso/.../pullrequest/7763", "active", "abc123");

        _clientMock.Setup(c => c.CreatePullRequestAsync(
                "Contoso.Legado.Database", request.SourceBranch, request.TargetBranch, request.Title, request.Description, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pullRequest);

        var result = await _sut.OrchestrateAsync([request]);

        result.Should().ContainSingle();
        result[0].Url.Should().Be(pullRequest.Url);
        result[0].Completed.Should().BeFalse();
        _clientMock.Verify(c => c.CompletePullRequestAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrchestrateAsync_PrDeApi_CompletaAutomaticamenteECapturaOBuildId()
    {
        var request = new RepositoryPullRequestRequest(
            "Contoso.PortalCliente.API", RepositoryKind.Api, "release/GMUD_PROD_Contoso_10092026", "main", "Título", "Descrição");
        var pullRequest = new PullRequestInfo(7772, "https://dev.azure.com/contoso/.../pullrequest/7772", "active", "def456");

        _clientMock.Setup(c => c.CreatePullRequestAsync(
                "Contoso.PortalCliente.API", request.SourceBranch, request.TargetBranch, request.Title, request.Description, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pullRequest);
        _clientMock.Setup(c => c.GetLatestBuildIdForBranchAsync("main", It.IsAny<CancellationToken>())).ReturnsAsync(25983);

        var result = await _sut.OrchestrateAsync([request]);

        result[0].Completed.Should().BeTrue();
        result[0].BuildId.Should().Be(25983);
        _clientMock.Verify(c => c.CompletePullRequestAsync("Contoso.PortalCliente.API", 7772, "def456", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrchestrateAsync_PrDeBackendComoGerenciadorDeJobs_CompletaAutomaticamenteComoApiOuWeb()
    {
        var request = new RepositoryPullRequestRequest(
            "Contoso.Integracao.GerenciadorJobs", RepositoryKind.Backend, "release/GMUD_PROD_Contoso_10092026", "main", "Título", "Descrição");
        var pullRequest = new PullRequestInfo(7780, "https://dev.azure.com/contoso/.../pullrequest/7780", "active", "ghi789");

        _clientMock.Setup(c => c.CreatePullRequestAsync(
                "Contoso.Integracao.GerenciadorJobs", request.SourceBranch, request.TargetBranch, request.Title, request.Description, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pullRequest);
        _clientMock.Setup(c => c.GetLatestBuildIdForBranchAsync("main", It.IsAny<CancellationToken>())).ReturnsAsync(30001);

        var result = await _sut.OrchestrateAsync([request]);

        result[0].Completed.Should().BeTrue();
        result[0].BuildId.Should().Be(30001);
        _clientMock.Verify(c => c.CompletePullRequestAsync("Contoso.Integracao.GerenciadorJobs", 7780, "ghi789", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrchestrateAsync_MultiplosRepositorios_RetornaUmResultadoParaCadaUmNaOrdemInformada()
    {
        var apiRequest = new RepositoryPullRequestRequest("Contoso.PortalCliente.API", RepositoryKind.Api, "release/x", "main", "t", "d");
        var databaseRequest = new RepositoryPullRequestRequest("Contoso.Legado.Database", RepositoryKind.Database, "release/x", "main", "t", "d");

        _clientMock.Setup(c => c.CreatePullRequestAsync(
                "Contoso.PortalCliente.API", "release/x", "main", "t", "d", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PullRequestInfo(1, "url-api", "active", "sha-api"));
        _clientMock.Setup(c => c.CreatePullRequestAsync(
                "Contoso.Legado.Database", "release/x", "main", "t", "d", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PullRequestInfo(2, "url-db", "active", "sha-db"));
        _clientMock.Setup(c => c.GetLatestBuildIdForBranchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);

        var result = await _sut.OrchestrateAsync([apiRequest, databaseRequest]);

        result.Should().HaveCount(2);
        result[0].RepositoryName.Should().Be("Contoso.PortalCliente.API");
        result[1].RepositoryName.Should().Be("Contoso.Legado.Database");
    }
}
