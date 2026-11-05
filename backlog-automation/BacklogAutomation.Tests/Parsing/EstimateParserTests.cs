using BacklogAutomation.Core.Parsing;
using FluentAssertions;

namespace BacklogAutomation.Tests.Parsing;

public class EstimateParserTests
{
    private readonly EstimateParser _sut = new(hoursPerDay: 8m);

    [Theory]
    [InlineData("5 SP", 5)]
    [InlineData("8 pontos", 8)]
    [InlineData("13 story points", 13)]
    [InlineData("Story Points: 3", 3)]
    [InlineData("Esforço: 21", 21)]
    [InlineData("**Pontos:** 2", 2)]
    [InlineData("0,5 pts", 0.5)]
    public void Parse_StoryPointsEmDiferentesFormatos_PreencheStoryPoints(string text, double expected)
    {
        var result = _sut.Parse(text);

        result.StoryPoints.Should().Be((decimal)expected);
        result.OriginalEstimateHours.Should().BeNull();
    }

    [Theory]
    [InlineData("4h", 4)]
    [InlineData("6 horas", 6)]
    [InlineData("2 horas e 30 min", 2.5)]
    [InlineData("1,5 dia", 12)]
    [InlineData("2 dias", 16)]
    [InlineData("Horas: 7", 7)]
    [InlineData("Estimativa: 3h", 3)]
    public void Parse_HorasEDias_ConverteParaHorasEstimadas(string text, double expected)
    {
        var result = _sut.Parse(text);

        result.OriginalEstimateHours.Should().Be((decimal)expected);
    }

    [Fact]
    public void Parse_JornadaCustomizada_ConverteDiasComAJornadaInformada()
    {
        var sut = new EstimateParser(hoursPerDay: 6m);

        sut.Parse("2 dias").OriginalEstimateHours.Should().Be(12);
    }

    [Fact]
    public void Parse_VariasMedidasNaMesmaLinha_PreencheTodosOsCampos()
    {
        var result = _sut.Parse("5 SP, 8h; Restante: 3h | Concluído: 5h, Tamanho: M");

        result.StoryPoints.Should().Be(5);
        result.OriginalEstimateHours.Should().Be(8);
        result.RemainingHours.Should().Be(3);
        result.CompletedHours.Should().Be(5);
        result.Size.Should().Be("M");
    }

    [Fact]
    public void Parse_MedidaLivre_VaiParaOtherMeasuresComORotuloOriginal()
    {
        var result = _sut.Parse("Pontos de função: 12, Complexidade: Alta");

        result.StoryPoints.Should().BeNull();
        result.OtherMeasures.Should().Contain("Pontos de função", "12").And.Contain("Complexidade", "Alta");
    }

    [Fact]
    public void Parse_PontosDeFuncaoSemDoisPontos_NaoConfundeComStoryPoints()
    {
        var result = _sut.Parse("12 pontos de função");

        result.StoryPoints.Should().BeNull();
    }

    [Theory]
    [InlineData("5 SP")]
    [InlineData("4h")]
    [InlineData("2 horas e 30 min")]
    [InlineData("1 dia")]
    [InlineData("3 SP, 6h")]
    public void TryParseInline_SomenteMedidasComUnidade_RetornaTrue(string text)
    {
        _sut.TryParseInline(text, out var estimate).Should().BeTrue();
        estimate.IsEmpty.Should().BeFalse();
    }

    [Theory]
    [InlineData("sandbox")]
    [InlineData("MVP")]
    [InlineData("v2")]
    [InlineData("5 telas")]
    [InlineData("login via Google em 2h")]
    public void TryParseInline_TextoQueNaoEEstimativa_RetornaFalse(string text)
    {
        _sut.TryParseInline(text, out var estimate).Should().BeFalse();
        estimate.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Parse_TextoVazio_RetornaEstimativaVazia()
    {
        _sut.Parse("   ").IsEmpty.Should().BeTrue();
    }
}
