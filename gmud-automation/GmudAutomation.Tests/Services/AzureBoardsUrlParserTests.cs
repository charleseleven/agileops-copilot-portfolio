using FluentAssertions;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Tests.Services;

public class AzureBoardsUrlParserTests
{
    private readonly AzureBoardsUrlParser _sut = new();

    [Fact]
    public void TryParse_UrlDaOrganizacaoContoso_RetornaTrueComOrganizacaoProjetoEIdCorretos()
    {
        const string url = "https://dev.azure.com/contoso/PortalCliente/_workitems/edit/4321";

        var result = _sut.TryParse(url, out var reference);

        result.Should().BeTrue();
        reference.Organization.Should().Be("contoso");
        reference.Project.Should().Be("PortalCliente");
        reference.WorkItemId.Should().Be(4321);
    }

    [Fact]
    public void TryParse_UrlComProjetoContendoEspacosCodificados_DecodificaONomeDoProjeto()
    {
        const string url = "https://dev.azure.com/eleven11C/Applied%20AI%20Engineering/_workitems/edit/187";

        var result = _sut.TryParse(url, out var reference);

        result.Should().BeTrue();
        reference.Organization.Should().Be("eleven11C");
        reference.Project.Should().Be("Applied AI Engineering");
        reference.WorkItemId.Should().Be(187);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é uma url")]
    [InlineData("https://dev.azure.com/contoso/PortalCliente/_apis/wit/workitems/187")]
    [InlineData("https://learn.microsoft.com/azure/devops")]
    public void TryParse_UrlInvalidaOuSemPadraoDeWorkItem_RetornaFalseSemLancarExcecao(string url)
    {
        var act = () => _sut.TryParse(url, out _);

        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    [Fact]
    public void TryParse_UrlNula_RetornaFalseSemLancarExcecao()
    {
        var act = () => _sut.TryParse(null!, out _);

        act.Should().NotThrow();
        act().Should().BeFalse();
    }
}
