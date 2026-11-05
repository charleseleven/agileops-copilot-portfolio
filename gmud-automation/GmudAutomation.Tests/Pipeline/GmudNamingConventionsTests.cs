using FluentAssertions;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline;
using GmudAutomation.Core.Pipeline.Configuration;

namespace GmudAutomation.Tests.Pipeline;

public class GmudNamingConventionsTests
{
    private const string Sha = "6618eda39fb8daf10b122ac0b76d9fcec8629a78";

    private readonly GmudNamingConventions _sut = new(new NamingConfig(),
    [
        new ClientConfig("Contoso Saúde", ContainerName: "CSAUDE", AppServiceName: "csaude")
    ]);

    [Fact]
    public void ParseTitle_TituloPadrao_ExtraiDataClienteETipo()
    {
        var result = _sut.ParseTitle("GMUD 22-10-2026 - Contoso - Implantação");

        result.Date.Should().Be(new DateOnly(2026, 10, 22));
        result.ClientTitleName.Should().Be("Contoso");
        result.ChangeType.Should().Be("Implantação");
    }

    [Fact]
    public void ParseTitle_ClienteComHifenSemEspacos_MantemONomeComposto()
    {
        var result = _sut.ParseTitle("GMUD 04-11-2026 - Sao-Jose - Implantação");

        result.ClientTitleName.Should().Be("Sao-Jose");
    }

    [Theory]
    [InlineData("GMUD Contoso - Implantação", "data")]
    [InlineData("GMUD 2026-10-22 - Contoso", "data")]
    [InlineData("GMUD 31-02-2026 - Contoso", "data")]
    [InlineData("GMUD 22-10-2026", "cliente")]
    [InlineData("   ", "titulo")]
    public void ParseTitle_ForaDoPadrao_LancaValidacaoIndicandoOCampo(string title, string field)
    {
        var act = () => _sut.ParseTitle(title);

        act.Should().Throw<GmudValidationException>().Which.Field.Should().Be(field);
    }

    [Fact]
    public void FormatDate_NormalizaParaDDMMAAAA()
    {
        GmudNamingConventions.FormatDate(new DateOnly(2026, 10, 22)).Should().Be("22102026");
    }

    [Fact]
    public void ResolveClient_ClienteConfigurado_UsaNomesDaConfiguracaoIgnorandoAcento()
    {
        var client = _sut.ResolveClient("Contoso Saude");

        client.ContainerName.Should().Be("CSAUDE");
        client.AppServiceName.Should().Be("csaude");
        client.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void ResolveClient_ClienteNaoConfigurado_NormalizaONomeDoTitulo()
    {
        var client = _sut.ResolveClient("São José");

        client.ContainerName.Should().Be("SAOJOSE");
        client.AppServiceName.Should().Be("saojose");
        client.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void PadroesDeBranch_SeguemAConvencaoDaEspecificacao()
    {
        var date = new DateOnly(2026, 10, 22);

        _sut.GmudBranch(25482).Should().Be("feature/us_25482");
        _sut.ItemBranch(20611).Should().Be("feature/us_20611");
        _sut.ProductionRelease("CLI1", date).Should().Be("release/GMUD_PROD_CLI1_22102026");
        _sut.GmudRelease("CLI2", new DateOnly(2026, 11, 4)).Should().Be("release/GMUD_GMUD_CLI2_04112026");
        _sut.SourceBranchFromRelease("GMUD_PROD_CLI1_22102026").Should().Be("release/GMUD_PROD_CLI1_22102026");
        _sut.ReleaseNameFromBranch("release/GMUD_PROD_CLI1_22102026").Should().Be("GMUD_PROD_CLI1_22102026");
        _sut.MergeCommitMessage(25482, "feature/us_20611").Should().Be("GMUD 25482: merge feature/us_20611");
    }

    [Theory]
    [InlineData("GMUD_PROD_CLI1_29102026", "release/GMUD_PROD_CLI1_29102026")]
    [InlineData("main_cli1", "main_cli1")]
    [InlineData("MAIN_CLI1", "MAIN_CLI1")]
    [InlineData("main", "main")]
    [InlineData("mainline", "release/mainline")]
    public void SourceBranchFromRelease_ReleaseOuMainDoCliente_MontaABranchPublicada(string releaseName, string expected)
    {
        _sut.SourceBranchFromRelease(releaseName).Should().Be(expected);
    }

    [Fact]
    public void SourceBranchFromRelease_ListaSemPrefixoConfigurada_UsaAConfiguracao()
    {
        var custom = new GmudNamingConventions(new NamingConfig { SourceBranchesWithoutPrefix = ["hotfix_*"] });

        custom.SourceBranchFromRelease("hotfix_cli1").Should().Be("hotfix_cli1");
        custom.SourceBranchFromRelease("main_cli1").Should().Be("release/main_cli1");
    }

    [Theory]
    [InlineData("31229-GMUD_PROD_CLI1_29102026-7432b205df6ce05a52c0321da5b0559d3bc76076", 31229, "GMUD_PROD_CLI1_29102026")]
    [InlineData("31230-main_cli1-7432b205df6ce05a52c0321da5b0559d3bc76076", 31230, "main_cli1")]
    [InlineData("31231-GMUD-PROD-CLI1-7432b205df6ce05a52c0321da5b0559d3bc76076", 31231, "GMUD-PROD-CLI1")]
    public void TryParseContainerTag_DecompoeIdBranchESha(string tag, int buildId, string releaseName)
    {
        GmudNamingConventions.TryParseContainerTag(tag, out var parsed).Should().BeTrue();

        parsed.BuildId.Should().Be(buildId);
        parsed.ReleaseName.Should().Be(releaseName);
        parsed.CommitSha.Should().Be("7432b205df6ce05a52c0321da5b0559d3bc76076");
    }

    [Theory]
    [InlineData("25448")]
    [InlineData("latest")]
    [InlineData(null)]
    public void TryParseContainerTag_ForaDoPadrao_RetornaFalse(string? tag)
    {
        GmudNamingConventions.TryParseContainerTag(tag, out _).Should().BeFalse();
    }

    [Fact]
    public void AppServiceName_PreencheAmbienteECliente()
    {
        var repository = new RepositoryConfig("Contoso.PortalCliente.WEB", RepositoryKind.Web, AppService: "contoso-portalcliente-web-{ambiente}-{cliente}");

        _sut.AppServiceName(repository, "prd", _sut.ResolveClient("Cli1")).Should().Be("contoso-portalcliente-web-prd-cli1");
    }

    [Fact]
    public void ConfiguracaoPersonalizada_MudaOsNomesSemAlterarOFluxo()
    {
        var custom = new GmudNamingConventions(new NamingConfig { GmudBranch = "gmud/{id}", ProductionRelease = "rel/{cliente}-{data}" });

        custom.GmudBranch(10).Should().Be("gmud/10");
        custom.ProductionRelease("CLI1", new DateOnly(2026, 1, 2)).Should().Be("rel/CLI1-02012026");
    }

    [Theory]
    [InlineData("contosoregistry.azurecr.io/contoso.portalcliente.web:30945-GMUD_PROD_CLI1_22102026-" + Sha)]
    [InlineData("DOCKER|contosoregistry.azurecr.io/contoso.portalcliente.web:30945-GMUD_PROD_CLI1_22102026-" + Sha)]
    public void TryParseContainerImage_TagPadrao_DecompoeBuildReleaseECommit(string image)
    {
        GmudNamingConventions.TryParseContainerImage(image, out var parsed).Should().BeTrue();

        parsed.Repository.Should().Be("contosoregistry.azurecr.io/contoso.portalcliente.web");
        parsed.BuildId.Should().Be(30945);
        parsed.ReleaseName.Should().Be("GMUD_PROD_CLI1_22102026");
        parsed.CommitSha.Should().Be(Sha);
    }

    [Fact]
    public void TryParseContainerImage_RegistroComPorta_UsaOUltimoDoisPontosDepoisDaBarra()
    {
        GmudNamingConventions.TryParseContainerImage($"registro:5000/app:12-GMUD_PROD_X_01012026-{Sha}", out var parsed).Should().BeTrue();

        parsed.Repository.Should().Be("registro:5000/app");
        parsed.BuildId.Should().Be(12);
    }

    [Theory]
    [InlineData("contosoregistry.azurecr.io/app:latest")]
    [InlineData("contosoregistry.azurecr.io/app:30945-GMUD_PROD_X-6618eda3")]
    [InlineData("contosoregistry.azurecr.io/app")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseContainerImage_ForaDoPadrao_RetornaFalse(string? image)
    {
        GmudNamingConventions.TryParseContainerImage(image, out _).Should().BeFalse();
    }

    [Fact]
    public void ContainerTag_MontaNoFormatoBuildReleaseCommit()
    {
        GmudNamingConventions.ContainerTag(31002, "GMUD_PROD_CLI1_22102026", Sha).Should().Be($"31002-GMUD_PROD_CLI1_22102026-{Sha}");
    }

    [Fact]
    public void DatabaseScriptName_SegueANomenclaturaUSScriptTask()
    {
        _sut.DatabaseScriptName(20611, 20612).Should().Be("US20611_SCRIPT_TASK20612");
    }

    [Theory]
    [InlineData("US20611_SCRIPT_TASK20612.txt", true)]
    [InlineData("us20611_script_task20612.TXT", true)]
    [InlineData("US20611_SCRIPT_TASK20612_2.txt", true)]
    [InlineData("US20611_SCRIPT_TASK206123.txt", false)]
    [InlineData("US20611_SCRIPT_TASK20699.txt", false)]
    [InlineData("20611_SCRIPT_20612.txt", false)]
    [InlineData("script.txt", false)]
    public void IsDatabaseScriptName_ConfereUserStoryETask(string fileName, bool expected)
    {
        _sut.IsDatabaseScriptName(fileName, 20611, 20612).Should().Be(expected);
    }
}
