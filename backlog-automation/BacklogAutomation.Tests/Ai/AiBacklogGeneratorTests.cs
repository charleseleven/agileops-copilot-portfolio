using System.Net;
using BacklogAutomation.Core.Ai;
using BacklogAutomation.Core.Models;
using FluentAssertions;
using Moq;

namespace BacklogAutomation.Tests.Ai;

public class AiBacklogGeneratorTests
{
    private const string LlmJson = """
        {
          "epics": [{
            "title": "Agendamento online",
            "features": [{
              "title": "Agenda do paciente",
              "userStories": [{
                "title": "Agendar consulta",
                "statement": "Como paciente, quero escolher um horário livre, para que eu não precise ligar para a recepção",
                "acceptanceCriteria": ["Dado um horário livre, quando confirmo, então a consulta é agendada"],
                "estimate": { "storyPoints": "8" },
                "tasks": [{ "title": "Criar API de horários", "estimate": { "originalEstimateHours": 8 } }],
                "bugs": [{ "title": "Horário não é liberado ao cancelar", "reproSteps": ["Cancelar consulta"], "estimate": { "originalEstimateHours": 3 } }]
              }, {
                "title": "Painel da recepção",
                "estimate": { "storyPoints": 5, "otherMeasures": { "Complexidade": "Alta", "Pontos de função": 12 } }
              }]
            }]
          }]
        }
        """;

    private readonly Mock<ILlmClient> _llmMock = new();

    [Fact]
    public async Task GenerateAsync_RespostaValida_ConverteParaBacklogComEstimativasDoDocumento()
    {
        _llmMock.Setup(llm => llm.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LlmJson);
        var sut = new AiBacklogGenerator(_llmMock.Object);

        var plan = await sut.GenerateAsync("texto do documento", new AiGenerationOptions());

        var stories = plan.Epics[0].Features[0].UserStories;
        stories[0].Estimate.StoryPoints.Should().Be(8, "números como string também são aceitos");
        stories[0].Estimate.Source.Should().Be(Estimate.SourceDocument);
        stories[0].Tasks[0].Estimate.OriginalEstimateHours.Should().Be(8);
        stories[0].Bugs[0].ReproSteps.Should().Equal("Cancelar consulta");
        stories[1].Estimate.OtherMeasures.Should().Contain("Complexidade", "Alta").And.Contain("Pontos de função", "12");
        plan.Epics[0].Estimate.Source.Should().BeNull("itens sem estimativa não recebem origem");
    }

    [Fact]
    public async Task GenerateAsync_JsonEmbrulhadoEmMarkdown_ExtraiOObjeto()
    {
        _llmMock.Setup(llm => llm.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync($"Aqui está o backlog:\n```json\n{LlmJson}\n```");
        var sut = new AiBacklogGenerator(_llmMock.Object);

        var plan = await sut.GenerateAsync("texto", new AiGenerationOptions());

        plan.Epics.Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateAsync_SemSugestao_DescartaOrigemAiSuggestedInventadaPelaLlm()
    {
        const string json = """{"epics":[{"title":"E","features":[{"title":"F","userStories":[{"title":"US","estimate":{"storyPoints":3,"source":"ai-suggested"}}]}]}]}""";
        _llmMock.Setup(llm => llm.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        var sut = new AiBacklogGenerator(_llmMock.Object);

        var plan = await sut.GenerateAsync("texto", new AiGenerationOptions { SuggestMissingEstimates = false });

        plan.Epics[0].Features[0].UserStories[0].Estimate.Source.Should().Be(Estimate.SourceDocument);
    }

    [Fact]
    public async Task GenerateAsync_ComSugestao_PreservaMarcacaoAiSuggested()
    {
        const string json = """{"epics":[{"title":"E","features":[{"title":"F","userStories":[{"title":"US","estimate":{"storyPoints":3,"source":"ai-suggested"}}]}]}]}""";
        _llmMock.Setup(llm => llm.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        var sut = new AiBacklogGenerator(_llmMock.Object);

        var plan = await sut.GenerateAsync("texto", new AiGenerationOptions { SuggestMissingEstimates = true });

        plan.Epics[0].Features[0].UserStories[0].Estimate.IsAiSuggested.Should().BeTrue();
    }

    [Fact]
    public async Task GenerateAsync_RespostaSemJson_LancaLlmException()
    {
        _llmMock.Setup(llm => llm.CompleteJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Desculpe, não consegui.");
        var sut = new AiBacklogGenerator(_llmMock.Object);

        var act = () => sut.GenerateAsync("texto", new AiGenerationOptions());

        await act.Should().ThrowAsync<LlmException>();
    }

    [Fact]
    public async Task GenerateAsync_DocumentoAcimaDoLimite_RecusaAntesDeChamarALlm()
    {
        var sut = new AiBacklogGenerator(_llmMock.Object);

        var act = () => sut.GenerateAsync(new string('x', AiBacklogGenerator.MaxSourceCharacters + 1), new AiGenerationOptions());

        await act.Should().ThrowAsync<ArgumentException>();
        _llmMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, "NUNCA invente estimativas")]
    [InlineData(true, "SUGIRA uma")]
    public void BuildSystemPrompt_RegraDeEstimativaSegueAOpcao(bool suggest, string expectedRule)
    {
        var prompt = AiBacklogGenerator.BuildSystemPrompt(new AiGenerationOptions { SuggestMissingEstimates = suggest, HoursPerDay = 6 });

        prompt.Should().Contain(expectedRule).And.Contain("INVEST").And.Contain("6h por dia").And.Contain("Epic → Feature → User Story → Task / Bug")
            .And.Contain("NUNCA decomponha");
    }

    [Fact]
    public async Task OpenAiCompatibleLlmClient_EnviaJsonModeEDevolveOConteudo()
    {
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Post, "chat/completions", HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"stop","message":{"content":"{\"epics\":[]}"}}]}""");
        var client = new OpenAiCompatibleLlmClient(new HttpClient(handler), new LlmClientOptions { ApiKey = "fake-key" });

        var content = await client.CompleteJsonAsync("system", "user");

        content.Should().Be("""{"epics":[]}""");
        var body = handler.Requests.Single().Json;
        body.GetProperty("response_format").GetProperty("type").GetString().Should().Be("json_object");
        body.GetProperty("model").GetString().Should().Be(LlmClientOptions.DefaultModel);
    }

    [Fact]
    public async Task OpenAiCompatibleLlmClient_RespostaTruncada_LancaLlmException()
    {
        var handler = new RoutingHttpMessageHandler().On(HttpMethod.Post, "chat/completions", HttpStatusCode.OK,
            """{"choices":[{"finish_reason":"length","message":{"content":"{\"epics\":["}}]}""");
        var client = new OpenAiCompatibleLlmClient(new HttpClient(handler), new LlmClientOptions { ApiKey = "fake-key" });

        var act = () => client.CompleteJsonAsync("system", "user");

        await act.Should().ThrowAsync<LlmException>().WithMessage("*truncada*");
    }
}
