using FluentAssertions;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Services;

public class GmudAutomationOrchestratorTests
{
    private readonly Mock<IGmudHierarchyService> _hierarchyServiceMock = new();
    private readonly Mock<IGmudTechnicalImpactPublisher> _impactPublisherMock = new();
    private readonly GmudAutomationOrchestrator _sut;

    public GmudAutomationOrchestratorTests()
    {
        _sut = new GmudAutomationOrchestrator(_hierarchyServiceMock.Object, _impactPublisherMock.Object);
    }

    [Fact]
    public async Task RunAsync_GmudComTresTasksFilhas_PublicaImpactoParaCadaUmaDasTres()
    {
        var hierarchy = new GmudRequest
        {
            Id = 187,
            Title = "1.1: Mapeamento da Hierarquia da GMUD",
            ChildWorkItems =
            [
                new WorkItemInfo(188, "Task 188", "Task", "To Do", null, "url"),
                new WorkItemInfo(189, "Task 189", "Task", "To Do", null, "url"),
                new WorkItemInfo(190, "Task 190", "Task", "To Do", null, "url")
            ]
        };

        _hierarchyServiceMock.Setup(h => h.BuildHierarchyAsync(187, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hierarchy);
        _impactPublisherMock.Setup(p => p.AnalyzeAndPublishAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _sut.RunAsync(187);

        result.Should().BeSameAs(hierarchy);
        foreach (var childId in new[] { 188, 189, 190 })
        {
            _impactPublisherMock.Verify(p => p.AnalyzeAndPublishAsync(childId, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task RunAsync_GmudSemTasksFilhas_NaoPublicaImpactoParaNenhumWorkItem()
    {
        var hierarchy = new GmudRequest { Id = 187, Title = "Sem filhos", ChildWorkItems = [] };
        _hierarchyServiceMock.Setup(h => h.BuildHierarchyAsync(187, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hierarchy);

        await _sut.RunAsync(187);

        _impactPublisherMock.Verify(p => p.AnalyzeAndPublishAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
