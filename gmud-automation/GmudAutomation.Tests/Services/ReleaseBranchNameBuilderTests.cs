using FluentAssertions;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Tests.Services;

public class ReleaseBranchNameBuilderTests
{
    private readonly ReleaseBranchNameBuilder _sut = new();

    [Fact]
    public void Build_AmbienteClienteEDataInformados_RetornaNomeNoPadraoReleaseGmud()
    {
        var result = _sut.Build("PROD", "Cliente", new DateOnly(2026, 9, 10));

        result.Should().Be("release/GMUD_PROD_Cliente_10092026");
    }

    [Fact]
    public void Build_AmbienteHomologacao_RetornaNomeComOAmbienteInformado()
    {
        var result = _sut.Build("HOM", "Contoso", new DateOnly(2026, 1, 5));

        result.Should().Be("release/GMUD_HOM_Contoso_05012026");
    }
}
