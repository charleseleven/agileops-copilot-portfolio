using FluentAssertions;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Tests.Services;

public class ProjectTagExtractorTests
{
    private readonly ProjectTagExtractor _sut = new();

    [Theory]
    [InlineData("Alterado o projeto Contoso.PortalCliente.API para incluir o novo endpoint.", "Contoso.PortalCliente.API")]
    [InlineData("Ajuste realizado em Contoso.PortalCliente.WEB conforme solicitado.", "Contoso.PortalCliente.WEB")]
    [InlineData("Script criado em Contoso.PortalCliente.Database.", "Contoso.PortalCliente.Database")]
    public void ExtractProjectTags_ComentarioComUmaTagConhecida_RetornaONomeDoProjeto(string comentario, string projetoEsperado)
    {
        var result = _sut.ExtractProjectTags(comentario);

        result.Should().ContainSingle().Which.Should().Be(projetoEsperado);
    }

    [Fact]
    public void ExtractProjectTags_ComentarioComMultiplasTags_RetornaTodosOsProjetosDistintos()
    {
        const string comentario = "Alterações em Contoso.PortalCliente.API e Contoso.PortalCliente.Database para essa GMUD.";

        var result = _sut.ExtractProjectTags(comentario);

        result.Should().BeEquivalentTo(["Contoso.PortalCliente.API", "Contoso.PortalCliente.Database"]);
    }

    [Fact]
    public void ExtractProjectTags_ComentarioSemTagsConhecidas_RetornaListaVaziaSemLancarExcecao()
    {
        var act = () => _sut.ExtractProjectTags("Apenas um comentário informativo sem menção a projetos.");

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void ExtractProjectTags_ComentarioNulo_RetornaListaVaziaSemLancarExcecao()
    {
        var act = () => _sut.ExtractProjectTags(null);

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Theory]
    [InlineData("<p><strong>Alterações (WEB)</strong></p><p>Ajuste no componente.</p>", "Contoso.PortalCliente.WEB")]
    [InlineData("Alterações API: endpoint novo criado.", "Contoso.PortalCliente.API")]
    [InlineData("Rodado script de correção no Database hoje.", "Contoso.Legado.Database")]
    public void ExtractProjectTags_ComentarioComTagCurtaSemPrefixo_ResolveParaONomeCanonicoDoRepositorio(string comentario, string projetoEsperado)
    {
        var result = _sut.ExtractProjectTags(comentario);

        result.Should().ContainSingle().Which.Should().Be(projetoEsperado);
    }

    [Fact]
    public void ExtractProjectTags_ComentarioRealDeTaskComTagWebRepetida_RetornaApenasUmImpactoDistinto()
    {
        const string comentario =
            "<p><strong>Alterações (WEB)</strong></p><p>Foram corrigidas e formatadas as colunas.</p>" +
            "<p><strong>Alterações WEB:</strong></p><ul><li><code>usePrintCotacao.ts</code></li></ul>";

        var result = _sut.ExtractProjectTags(comentario);

        result.Should().ContainSingle().Which.Should().Be("Contoso.PortalCliente.WEB");
    }

    [Fact]
    public void ExtractProjectTags_NomeQualificadoNaoDuplicaComATagCurtaEmbutida_RetornaApenasUmaOcorrencia()
    {
        const string comentario = "Ajuste realizado em Contoso.PortalCliente.WEB conforme solicitado.";

        var result = _sut.ExtractProjectTags(comentario);

        result.Should().ContainSingle().Which.Should().Be("Contoso.PortalCliente.WEB");
    }
}
