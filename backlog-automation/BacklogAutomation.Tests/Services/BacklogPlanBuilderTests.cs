using BacklogAutomation.Core.Ai;
using BacklogAutomation.Core.Documents;
using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Parsing;
using BacklogAutomation.Core.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Moq;

namespace BacklogAutomation.Tests.Services;

public sealed class BacklogPlanBuilderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("backlog-tests-").FullName;
    private readonly Mock<IAiBacklogGenerator> _aiMock = new();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private BacklogPlanBuilder CreateSut(bool withAi = true) =>
        new(new DocumentTextExtractor(), new MarkdownBacklogParser(new EstimateParser()), withAi ? _aiMock.Object : null);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task FromFileAsync_MarkdownEstruturado_UsaParserDeterministicoSemIa()
    {
        var path = WriteFile("backlog.md", "# Epic: E\n## Feature: F\n### User Story: US (3 SP)\n- Task: T (2h)");

        var result = await CreateSut().FromFileAsync(path, ParserMode.Auto, new AiGenerationOptions());

        result.StrategyUsed.Should().Be(ParserMode.Markdown);
        result.Plan.Epics[0].Features[0].UserStories[0].Tasks[0].Estimate.OriginalEstimateHours.Should().Be(2);
        _aiMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FromFileAsync_TextoLivre_DelegaParaAIaComONomeDoArquivo()
    {
        var path = WriteFile("ata.txt", "O paciente precisa agendar consultas pelo celular (8 pontos).");
        _aiMock.Setup(ai => ai.GenerateAsync(It.IsAny<string>(), It.IsAny<AiGenerationOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BacklogPlan());

        var result = await CreateSut().FromFileAsync(path, ParserMode.Auto, new AiGenerationOptions { HoursPerDay = 6 });

        result.StrategyUsed.Should().Be(ParserMode.Ai);
        _aiMock.Verify(ai => ai.GenerateAsync(
            It.Is<string>(text => text.Contains("8 pontos")),
            It.Is<AiGenerationOptions>(options => options.SourceName == "ata.txt" && options.HoursPerDay == 6),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task FromFileAsync_TextoLivreSemIaConfigurada_ExplicaComoConfigurar()
    {
        var path = WriteFile("ata.txt", "Texto livre sem estrutura.");

        var act = () => CreateSut(withAi: false).FromFileAsync(path, ParserMode.Auto, new AiGenerationOptions());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*LLM_API_KEY*");
    }

    [Fact]
    public async Task FromFileAsync_JsonRevisado_CarregaDiretoPreservandoEstimativas()
    {
        var plan = new BacklogPlan
        {
            Epics = [new EpicItem { Title = "E", Features = [new FeatureItem { Title = "F", UserStories = [new UserStoryItem { Title = "US", Estimate = new Estimate { StoryPoints = 13 } }] }] }]
        };
        var path = WriteFile("backlog.json", BacklogJson.Serialize(plan));

        var result = await CreateSut().FromFileAsync(path, ParserMode.Auto, new AiGenerationOptions());

        result.StrategyUsed.Should().Be(ParserMode.Json);
        result.Plan.Epics[0].Features[0].UserStories[0].Estimate.StoryPoints.Should().Be(13);
    }

    [Fact]
    public async Task FromPromptAsync_PromptEstruturado_NaoUsaIa()
    {
        var result = await CreateSut().FromPromptAsync("Epic: E\n# Epic: E\n## Feature: F\n### US: Login (2 SP)", ParserMode.Auto, new AiGenerationOptions());

        result.StrategyUsed.Should().Be(ParserMode.Markdown);
        _aiMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FromFileAsync_WordComTitulosEmPortugues_EConvertidoParaMarkdownEstruturado()
    {
        var path = Path.Combine(_directory, "requisitos.docx");
        CreateWordDocument(path,
            ("Ttulo1", "Epic: Agendamento"),
            ("Ttulo2", "Feature: Agenda do paciente"),
            ("Ttulo3", "User Story: Agendar consulta (8 SP)"),
            (null, "Como paciente, quero agendar pelo celular, para que eu não precise ligar."),
            ("PargrafodaLista", "Task: Criar API de horários (6h)"));

        var result = await CreateSut().FromFileAsync(path, ParserMode.Auto, new AiGenerationOptions());

        result.StrategyUsed.Should().Be(ParserMode.Markdown);
        var story = result.Plan.Epics[0].Features[0].UserStories.Should().ContainSingle().Subject;
        story.Estimate.StoryPoints.Should().Be(8);
        story.Statement.Should().StartWith("Como paciente");
        story.Tasks.Should().ContainSingle().Which.Estimate.OriginalEstimateHours.Should().Be(6);
    }

    [Fact]
    public async Task FromFileAsync_FormatoNaoSuportado_LancaNotSupportedException()
    {
        var path = WriteFile("planilha.xlsx", "x");

        var act = () => CreateSut().FromFileAsync(path, ParserMode.Auto, new AiGenerationOptions());

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    /// <summary>Gera um .docx como o Word em português gera: styleId "Ttulo1" com nome de estilo "heading 1".</summary>
    private static void CreateWordDocument(string path, params (string? StyleId, string Text)[] paragraphs)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();

        var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            Enumerable.Range(1, 3).Select(level => new Style(new StyleName { Val = $"heading {level}" })
            {
                StyleId = $"Ttulo{level}",
                Type = StyleValues.Paragraph
            }).Append(new Style(new StyleName { Val = "List Paragraph" }) { StyleId = "PargrafodaLista", Type = StyleValues.Paragraph }));

        main.Document = new Document(new Body(paragraphs.Select(p =>
        {
            var paragraph = new Paragraph(new Run(new Text(p.Text)));
            if (p.StyleId is not null)
            {
                paragraph.PrependChild(new ParagraphProperties(new ParagraphStyleId { Val = p.StyleId }));
            }

            return (OpenXmlElement)paragraph;
        })));
    }
}
