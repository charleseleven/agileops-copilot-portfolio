using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Validation;
using FluentAssertions;

namespace BacklogAutomation.Tests.Validation;

public class BacklogPlanValidatorTests
{
    private readonly BacklogPlanValidator _sut = new(hoursPerDay: 8m);

    private static BacklogPlan PlanWith(UserStoryItem story) => new()
    {
        Epics = [new EpicItem { Title = "Epic", Features = [new FeatureItem { Title = "Feature", UserStories = [story] }] }]
    };

    private static UserStoryItem GoodStory() => new()
    {
        Title = "Agendar consulta",
        Statement = "Como paciente, quero agendar uma consulta, para que eu não precise ligar",
        AcceptanceCriteria = ["Dado um horário livre, quando confirmo, então a consulta é agendada"],
        Estimate = new Estimate { StoryPoints = 5 },
        Tasks = [new TaskItem { Title = "Criar API", Estimate = new Estimate { OriginalEstimateHours = 6 } }]
    };

    [Fact]
    public void Validate_HistoriaBemEscrita_SemProblemas()
    {
        _sut.Validate(PlanWith(GoodStory())).Issues.Should().BeEmpty();
    }

    [Fact]
    public void Validate_BacklogSemEpics_RetornaErro()
    {
        var report = _sut.Validate(new BacklogPlan());

        report.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Validate_TituloVazioOuEstimativaNegativa_RetornaErros()
    {
        var story = GoodStory();
        story.Title = " ";
        story.Tasks[0].Estimate.OriginalEstimateHours = -1;

        var report = _sut.Validate(PlanWith(story));

        report.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Validate_HistoriaForaDasBoasPraticas_GeraAvisosSemBloquear()
    {
        var story = new UserStoryItem
        {
            Title = "Tela de agenda",
            Estimate = new Estimate { StoryPoints = 7 },
            Tasks = [new TaskItem { Title = "Fazer tudo", Estimate = new Estimate { OriginalEstimateHours = 40 } }]
        };

        var report = _sut.Validate(PlanWith(story));

        report.HasErrors.Should().BeFalse();
        report.Warnings.Select(w => w.Message).Should().Contain(m => m.Contains("Como <persona>"))
            .And.Contain(m => m.Contains("critérios de aceite"))
            .And.Contain(m => m.Contains("Fibonacci"))
            .And.Contain(m => m.Contains("quebrá-la"));
    }

    [Fact]
    public void Validate_TaskSemEstimativaEntreIrmasEstimadas_GeraAviso()
    {
        var story = GoodStory();
        story.Tasks.Add(new TaskItem { Title = "Sem estimativa" });

        var report = _sut.Validate(PlanWith(story));

        report.Warnings.Should().ContainSingle(w => w.ItemTitle == "Sem estimativa");
    }
}
