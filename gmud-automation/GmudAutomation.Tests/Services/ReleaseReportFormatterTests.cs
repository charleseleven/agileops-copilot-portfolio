using FluentAssertions;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Tests.Services;

public class ReleaseReportFormatterTests
{
    private readonly ReleaseReportFormatter _sut = new();

    [Fact]
    public void Format_DadosCompletos_IncluiBranchPrsETagsNoTexto()
    {
        var data = new ReleaseReportData(
            ReleaseBranchName: "release/GMUD_PROD_Contoso_10092026",
            SourceBranchDescription: "main_hur",
            ApiPullRequestUrl: "https://dev.azure.com/.../API/pullrequest/7772",
            WebPullRequestUrl: "https://dev.azure.com/.../WEB/pullrequest/7771",
            DatabasePullRequestUrls: ["https://dev.azure.com/.../Database/pullrequest/7763"],
            WebTagToPublish: "25982",
            ApiTagToPublish: "25983",
            WebTagCurrentlyInProduction: "25448",
            ApiTagCurrentlyInProduction: "25450");

        var result = _sut.Format(data);

        result.Should().Contain("release/GMUD_PROD_Contoso_10092026")
            .And.Contain("main_hur")
            .And.Contain("pullrequest/7772")
            .And.Contain("pullrequest/7771")
            .And.Contain("pullrequest/7763")
            .And.Contain("25982").And.Contain("25983").And.Contain("25448").And.Contain("25450");
    }

    [Fact]
    public void Format_SemPrDeDatabase_IndicaNenhumaSemLancarExcecao()
    {
        var data = new ReleaseReportData(
            ReleaseBranchName: "release/x", SourceBranchDescription: "main",
            ApiPullRequestUrl: null, WebPullRequestUrl: null, DatabasePullRequestUrls: [],
            WebTagToPublish: null, ApiTagToPublish: null, WebTagCurrentlyInProduction: null, ApiTagCurrentlyInProduction: null);

        var act = () => _sut.Format(data);

        act.Should().NotThrow();
        act().Should().Contain("(nenhuma)").And.Contain("(não gerada)").And.Contain("(não capturada)");
    }
}
