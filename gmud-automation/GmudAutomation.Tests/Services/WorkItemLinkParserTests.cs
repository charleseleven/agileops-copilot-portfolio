using FluentAssertions;
using GmudAutomation.Core.Services;

namespace GmudAutomation.Tests.Services;

public class WorkItemLinkParserTests
{
    private readonly WorkItemLinkParser _sut = new();

    [Fact]
    public void ExtractChildWorkItemIds_DescricaoComTresLinksDeTasks_RetornaAsTresUrlsCorretas()
    {
        const string descriptionHtml = """
            <div>Escopo da GMUD:
                <a href="https://dev.azure.com/eleven11C/Applied%20AI%20Engineering/_workitems/edit/188">Task 188</a>
                <a href="https://dev.azure.com/eleven11C/Applied%20AI%20Engineering/_workitems/edit/189">Task 189</a>
                <a href="https://dev.azure.com/eleven11C/Applied%20AI%20Engineering/_workitems/edit/190">Task 190</a>
            </div>
            """;

        var result = _sut.ExtractChildWorkItemIds(descriptionHtml);

        result.Should().BeEquivalentTo([188, 189, 190], options => options.WithStrictOrdering());
    }

    [Fact]
    public void ExtractChildWorkItemIds_DescricaoSemLinks_RetornaListaVaziaSemLancarExcecao()
    {
        const string descriptionHtml = "<div>Nenhum link de task aqui.</div>";

        var act = () => _sut.ExtractChildWorkItemIds(descriptionHtml);

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void ExtractChildWorkItemIds_DescricaoNula_RetornaListaVaziaSemLancarExcecao()
    {
        var act = () => _sut.ExtractChildWorkItemIds(null);

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void ExtractChildWorkItemIds_DescricaoComLinksParaOutrosDominios_IgnoraLinksQueNaoSaoDeWorkItems()
    {
        const string descriptionHtml = """
            <div>
                <a href="https://dev.azure.com/eleven11C/Applied%20AI%20Engineering/_workitems/edit/188">Task 188</a>
                <a href="https://learn.microsoft.com/azure/devops">Documentação</a>
            </div>
            """;

        var result = _sut.ExtractChildWorkItemIds(descriptionHtml);

        result.Should().ContainSingle().Which.Should().Be(188);
    }
}
