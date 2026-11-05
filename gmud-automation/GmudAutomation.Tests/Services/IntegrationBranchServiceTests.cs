using FluentAssertions;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Services;

public class IntegrationBranchServiceTests
{
    private const string Sha = "7432b205df6ce05a52c0321da5b0559d3bc76076";
    private const string ProductionTag = "31229-GMUD_PROD_CLI1_29102026-" + Sha;

    private readonly Mock<IAppServiceClient> _appServiceClientMock = new();
    private readonly Mock<IAzureDevOpsClient> _azureDevOpsClientMock = new();
    private readonly Mock<IReleaseBranchNameBuilder> _nameBuilderMock = new();
    private readonly IntegrationBranchService _sut;

    public IntegrationBranchServiceTests()
    {
        _sut = new IntegrationBranchService(_appServiceClientMock.Object, _azureDevOpsClientMock.Object, _nameBuilderMock.Object);
    }

    private static ReleaseBranchRequest CreateRequest(params string[] featureBranches) => new(
        Environment: "PROD",
        ClientName: "Contoso",
        ReleaseDate: new DateOnly(2026, 9, 10),
        RepositoryName: "Contoso.PortalCliente.API",
        SiteName: "contoso-portalcliente-api-prd-cli1",
        FeatureBranchNames: featureBranches);

    [Fact]
    public async Task CreateReleaseBranchAsync_TresFeatureBranches_CriaABranchEMesclaAsTresSequencialmente()
    {
        var request = CreateRequest("feature/us_192", "feature/us_193", "feature/us_194");
        _nameBuilderMock.Setup(b => b.Build("PROD", "Contoso", request.ReleaseDate)).Returns("release/GMUD_PROD_Contoso_10092026");
        _appServiceClientMock.Setup(a => a.GetCurrentTagAsync("contoso-portalcliente-api-prd-cli1", It.IsAny<CancellationToken>())).ReturnsAsync(ProductionTag);

        var result = await _sut.CreateReleaseBranchAsync(request);

        result.Should().Be("release/GMUD_PROD_Contoso_10092026");
        _azureDevOpsClientMock.Verify(c => c.CreateBranchAsync(
            "Contoso.PortalCliente.API", "release/GMUD_PROD_CLI1_29102026", "release/GMUD_PROD_Contoso_10092026", It.IsAny<CancellationToken>()), Times.Once);

        foreach (var featureBranch in request.FeatureBranchNames)
        {
            _azureDevOpsClientMock.Verify(c => c.MergeBranchesAsync(
                "Contoso.PortalCliente.API", featureBranch, "release/GMUD_PROD_Contoso_10092026", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Theory]
    [InlineData("31229-GMUD_PROD_CLI1_29102026-" + Sha, "release/GMUD_PROD_CLI1_29102026")]
    [InlineData("31230-main_cli1-" + Sha, "main_cli1")]
    [InlineData("31231-main-" + Sha, "main")]
    public async Task CreateReleaseBranchAsync_TagDoContainer_ParteDaBranchDoSegmentoCentral(string tag, string expectedSourceBranch)
    {
        _nameBuilderMock.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>())).Returns("release/GMUD_PROD_Contoso_10092026");
        _appServiceClientMock.Setup(a => a.GetCurrentTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(tag);

        await _sut.CreateReleaseBranchAsync(CreateRequest());

        _azureDevOpsClientMock.Verify(c => c.CreateBranchAsync(
            "Contoso.PortalCliente.API", expectedSourceBranch, "release/GMUD_PROD_Contoso_10092026", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateReleaseBranchAsync_TagForaDoPadrao_LancaErroSemCriarBranch()
    {
        _nameBuilderMock.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>())).Returns("release/GMUD_PROD_Contoso_10092026");
        _appServiceClientMock.Setup(a => a.GetCurrentTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("latest");

        var act = () => _sut.CreateReleaseBranchAsync(CreateRequest("feature/us_192"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*<buildId>-<branch>-<commitSha>*");
        _azureDevOpsClientMock.Verify(c => c.CreateBranchAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateReleaseBranchAsync_MergeRetornaConflito_PropagaAMergeConflictExceptionEInterrompeAsProximasMerges()
    {
        var request = CreateRequest("feature/us_192", "feature/us_193");
        _nameBuilderMock.Setup(b => b.Build(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>())).Returns("release/GMUD_PROD_Contoso_10092026");
        _appServiceClientMock.Setup(a => a.GetCurrentTagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ProductionTag);
        _azureDevOpsClientMock
            .Setup(c => c.MergeBranchesAsync("Contoso.PortalCliente.API", "feature/us_192", "release/GMUD_PROD_Contoso_10092026", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MergeConflictException("feature/us_192", "release/GMUD_PROD_Contoso_10092026", ["Arquivo.cs"]));

        var act = () => _sut.CreateReleaseBranchAsync(request);

        await act.Should().ThrowAsync<MergeConflictException>().WithMessage("*Arquivo.cs*");
        _azureDevOpsClientMock.Verify(c => c.MergeBranchesAsync(
            "Contoso.PortalCliente.API", "feature/us_193", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
