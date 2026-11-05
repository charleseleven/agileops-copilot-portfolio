using FluentAssertions;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Tests.Services;

public class TechnicalImpactReportFormatterTests
{
    private readonly TechnicalImpactReportFormatter _sut = new();

    [Fact]
    public void FormatComment_ListaVazia_RetornaMensagemDeNenhumProjetoIdentificado()
    {
        var result = _sut.FormatComment([]);

        result.Should().Be("Análise automática de impacto técnico (GMUD): nenhum projeto identificado nos comentários.");
    }

    [Fact]
    public void FormatComment_ImpactoComBranchExistente_RetornaLinhaIndicandoBranchEncontrada()
    {
        TechnicalImpact[] impacts = [new("Contoso.PortalCliente.API", "feature/us_192", BranchExists: true)];

        var result = _sut.FormatComment(impacts);

        result.Should().Contain("Contoso.PortalCliente.API").And.Contain("feature/us_192").And.Contain("encontrada");
        result.Should().NotContain("NÃO encontrada");
    }

    [Fact]
    public void FormatComment_ImpactoComBranchInexistente_RetornaLinhaDeAlertaParaVerificarAntesDoMerge()
    {
        TechnicalImpact[] impacts = [new("Contoso.PortalCliente.WEB", "feature/us_194", BranchExists: false)];

        var result = _sut.FormatComment(impacts);

        result.Should().Contain("NÃO encontrada").And.Contain("antes do merge");
    }

    [Fact]
    public void FormatComment_MultiplosImpactos_RetornaUmaLinhaPorProjeto()
    {
        TechnicalImpact[] impacts =
        [
            new("Contoso.PortalCliente.API", "feature/us_192", BranchExists: true),
            new("Contoso.PortalCliente.Database", "feature/us_192", BranchExists: false)
        ];

        var result = _sut.FormatComment(impacts);

        result.Should().Contain("Contoso.PortalCliente.API").And.Contain("Contoso.PortalCliente.Database");
    }
}
