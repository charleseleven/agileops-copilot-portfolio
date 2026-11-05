using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Providers;
using BacklogAutomation.Core.Services;
using FluentAssertions;
using Moq;

namespace BacklogAutomation.Tests.Services;

public class BacklogCreationServiceTests
{
    private readonly Mock<IWorkItemTrackerProvider> _providerMock = new();
    private readonly List<WorkItemCreationRequest> _requests = [];
    private int _nextId = 100;

    public BacklogCreationServiceTests()
    {
        _providerMock.SetupGet(p => p.Name).Returns("Fake");
        _providerMock.Setup(p => p.CreateAsync(It.IsAny<WorkItemCreationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkItemCreationRequest request, CancellationToken _) =>
            {
                _requests.Add(request);
                var id = (_nextId++).ToString();
                return new CreatedWorkItem(request.Kind, request.Title, id, $"https://fake/{id}", request.Kind.ToString(), []);
            });
    }

    private static BacklogPlan SamplePlan() => new()
    {
        Epics =
        [
            new EpicItem
            {
                Title = "Epic A",
                Features =
                [
                    new FeatureItem
                    {
                        Title = "Feature A1",
                        UserStories =
                        [
                            new UserStoryItem
                            {
                                Title = "US 1",
                                Statement = "Como x, quero y, para que z",
                                Estimate = new Estimate { StoryPoints = 5 },
                                Tasks = [new TaskItem { Title = "Task 1", Estimate = new Estimate { OriginalEstimateHours = 6, CompletedHours = 2 } }],
                                Bugs = [new BugItem { Title = "Bug 1", Estimate = new Estimate { OriginalEstimateHours = 3, RemainingHours = 1 } }]
                            }
                        ]
                    }
                ]
            }
        ]
    };

    [Fact]
    public async Task CreateAsync_CriaCadaItemDepoisDoPaiEVinculaAoPai()
    {
        var sut = new BacklogCreationService(_providerMock.Object);

        var result = await sut.CreateAsync(SamplePlan());

        result.CreatedItems.Select(i => i.Title).Should().Equal("Epic A", "Feature A1", "US 1", "Task 1", "Bug 1");
        _requests[0].Parent.Should().BeNull();
        _requests[1].Parent!.Id.Should().Be("100");
        _requests[2].Parent!.Id.Should().Be("101");
        _requests[3].Parent!.Id.Should().Be("102");
        _requests[4].Parent!.Id.Should().Be("102");
        _requests[3].EpicTitle.Should().Be("Epic A");
        _requests[3].FeatureTitle.Should().Be("Feature A1");
        _providerMock.Verify(p => p.PrepareAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_PreservaEstimativasEDefineRestanteQuandoAusente()
    {
        var sut = new BacklogCreationService(_providerMock.Object);

        await sut.CreateAsync(SamplePlan());

        _requests[2].Estimate.StoryPoints.Should().Be(5);
        _requests[3].Estimate.OriginalEstimateHours.Should().Be(6);
        _requests[3].Estimate.RemainingHours.Should().Be(4, "restante = estimado - concluído");
        _requests[4].Estimate.RemainingHours.Should().Be(1, "o restante informado no documento prevalece");
        _requests[2].Statement.Should().Be("Como x, quero y, para que z");
    }

    [Fact]
    public async Task CreateAsync_AplicaTagDaAutomacaoEDeEstimativaSugerida()
    {
        var plan = SamplePlan();
        plan.Epics[0].Features[0].UserStories[0].Estimate.Source = Estimate.SourceAiSuggested;
        var sut = new BacklogCreationService(_providerMock.Object);

        await sut.CreateAsync(plan);

        _requests.Should().OnlyContain(r => r.Tags.Contains(BacklogCreationService.AutomationTag));
        _requests[2].Tags.Should().Contain(BacklogCreationService.AiSuggestedEstimateTag);
        _requests[3].Tags.Should().NotContain(BacklogCreationService.AiSuggestedEstimateTag);
    }

    [Fact]
    public async Task CreateAsync_FalhaNoMeio_InformaOQueJaFoiCriado()
    {
        _providerMock.Setup(p => p.CreateAsync(It.Is<WorkItemCreationRequest>(r => r.Title == "US 1"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("HTTP 400"));
        var sut = new BacklogCreationService(_providerMock.Object);

        var act = () => sut.CreateAsync(SamplePlan());

        var exception = (await act.Should().ThrowAsync<BacklogCreationException>()).Which;
        exception.Message.Should().Contain("User Story \"US 1\"").And.Contain("HTTP 400");
        exception.CreatedItems.Select(i => i.Title).Should().Equal("Epic A", "Feature A1");
    }
}
