using FluentAssertions;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Services;

public class GmudHierarchyServiceTests
{
    private readonly Mock<IAzureDevOpsClient> _clientMock = new();
    private readonly Mock<IWorkItemLinkParser> _linkParserMock = new();
    private readonly GmudHierarchyService _sut;

    public GmudHierarchyServiceTests()
    {
        _sut = new GmudHierarchyService(_clientMock.Object, _linkParserMock.Object);
    }

    [Fact]
    public async Task BuildHierarchyAsync_UsPrincipalComTresTasksFilhas_RetornaGmudRequestComAsTresTasks()
    {
        var mainWorkItem = new WorkItemInfo(187, "1.1: Mapeamento da Hierarquia da GMUD", "Product Backlog Item", "Approved",
            "<div>...</div>", "https://dev.azure.com/eleven11C/_apis/wit/workItems/187");

        _clientMock.Setup(c => c.GetWorkItemAsync(187, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mainWorkItem);
        _linkParserMock.Setup(p => p.ExtractChildWorkItemIds(mainWorkItem.DescriptionHtml))
            .Returns([188, 189, 190]);

        foreach (var childId in new[] { 188, 189, 190 })
        {
            _clientMock.Setup(c => c.GetWorkItemAsync(childId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkItemInfo(childId, $"Task {childId}", "Task", "To Do", null,
                    $"https://dev.azure.com/eleven11C/_apis/wit/workItems/{childId}"));
        }

        var result = await _sut.BuildHierarchyAsync(187);

        result.Id.Should().Be(187);
        result.ChildWorkItems.Should().HaveCount(3);
        result.ChildWorkItems.Select(w => w.Id).Should().BeEquivalentTo([188, 189, 190]);
    }

    [Fact]
    public async Task BuildHierarchyAsync_UsSemTasksFilhas_RetornaGmudRequestComListaVaziaSemLancarExcecao()
    {
        var mainWorkItem = new WorkItemInfo(187, "1.1: Mapeamento da Hierarquia da GMUD", "Product Backlog Item", "Approved",
            "<div>sem links</div>", "https://dev.azure.com/eleven11C/_apis/wit/workItems/187");

        _clientMock.Setup(c => c.GetWorkItemAsync(187, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mainWorkItem);
        _linkParserMock.Setup(p => p.ExtractChildWorkItemIds(mainWorkItem.DescriptionHtml))
            .Returns([]);

        var result = await _sut.BuildHierarchyAsync(187);

        result.ChildWorkItems.Should().BeEmpty();
        _clientMock.Verify(c => c.GetWorkItemAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
