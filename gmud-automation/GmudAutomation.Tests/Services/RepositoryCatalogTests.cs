using FluentAssertions;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Tests.Services;

public class RepositoryCatalogTests
{
    private readonly RepositoryCatalog _sut = RepositoryCatalog.CreateExample();

    [Theory]
    [InlineData("Contoso.PortalCliente.API", RepositoryKind.Api)]
    [InlineData("Contoso.PortalCliente.WEB", RepositoryKind.Web)]
    [InlineData("Contoso.Integracao.GerenciadorJobs", RepositoryKind.Backend)]
    [InlineData("Contoso.Legado.Database", RepositoryKind.Database)]
    public void TryGetKind_RepositorioConhecido_RetornaACategoriaCorreta(string repositoryName, RepositoryKind expectedKind)
    {
        var result = _sut.TryGetKind(repositoryName, out var kind);

        result.Should().BeTrue();
        kind.Should().Be(expectedKind);
    }

    [Fact]
    public void TryGetKind_RepositorioDesconhecido_RetornaFalse()
    {
        var result = _sut.TryGetKind("Repositorio.Que.Nao.Existe", out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryGetSiteName_RepositorioApi_MontaNomeComAmbienteECliente()
    {
        var result = _sut.TryGetSiteName("Contoso.PortalCliente.API", "prd", "cli1", out var siteName);

        result.Should().BeTrue();
        siteName.Should().Be("contoso-portalcliente-api-prd-cli1");
    }

    [Fact]
    public void TryGetSiteName_RepositorioWeb_MontaNomeComAmbienteECliente()
    {
        var result = _sut.TryGetSiteName("Contoso.PortalCliente.WEB", "gmud", "cli2", out var siteName);

        result.Should().BeTrue();
        siteName.Should().Be("contoso-portalcliente-web-gmud-cli2");
    }

    [Fact]
    public void TryGetSiteName_GerenciadorDeJobs_RetornaSempreOSiteDeProducaoFixo()
    {
        var result = _sut.TryGetSiteName("Contoso.Integracao.GerenciadorJobs", "gmud", "cli2", out var siteName);

        result.Should().BeTrue();
        siteName.Should().Be("contoso-integracao-gerenciadorjobs-prd-01");
    }

    [Fact]
    public void TryGetSiteName_RepositorioDatabase_RetornaFalseSemAppService()
    {
        var result = _sut.TryGetSiteName("Contoso.Legado.Database", "prd", "cli1", out var siteName);

        result.Should().BeFalse();
        siteName.Should().BeEmpty();
    }

    [Fact]
    public void TryGetSiteName_RepositorioDesconhecido_RetornaFalse()
    {
        var result = _sut.TryGetSiteName("Repositorio.Que.Nao.Existe", "prd", "cli1", out _);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("API", "Contoso.PortalCliente.API")]
    [InlineData("web", "Contoso.PortalCliente.WEB")]
    [InlineData("Database", "Contoso.Legado.Database")]
    public void TryGetRepositoryNameByTag_TagConhecida_RetornaNomeCanonicoDoRepositorio(string tag, string expectedRepositoryName)
    {
        var result = _sut.TryGetRepositoryNameByTag(tag, out var repositoryName);

        result.Should().BeTrue();
        repositoryName.Should().Be(expectedRepositoryName);
    }

    [Fact]
    public void TryGetRepositoryNameByTag_TagDesconhecida_RetornaFalse()
    {
        var result = _sut.TryGetRepositoryNameByTag("Backend", out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void LoadFromFile_ArquivoJsonValido_CarregaAsEntradasCorretamente()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                [
                  { "name": "Acme.Portal.API", "kind": "Api", "siteNameTemplate": "acme-portal-api-{ambiente}-{cliente}", "tag": "API" },
                  { "name": "Acme.Legado.Database", "kind": "Database" }
                ]
                """);

            var catalog = RepositoryCatalog.LoadFromFile(path);

            catalog.TryGetKind("Acme.Portal.API", out var kind).Should().BeTrue();
            kind.Should().Be(RepositoryKind.Api);

            catalog.TryGetSiteName("Acme.Portal.API", "prd", "cli9", out var siteName).Should().BeTrue();
            siteName.Should().Be("acme-portal-api-prd-cli9");

            catalog.TryGetSiteName("Acme.Legado.Database", "prd", "cli9", out _).Should().BeFalse();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
