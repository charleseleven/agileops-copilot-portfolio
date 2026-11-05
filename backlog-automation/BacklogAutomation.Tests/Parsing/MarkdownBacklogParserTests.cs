using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Parsing;
using FluentAssertions;

namespace BacklogAutomation.Tests.Parsing;

public class MarkdownBacklogParserTests
{
    private readonly MarkdownBacklogParser _sut = new(new EstimateParser());

    private const string Document = """
        # Documento de requisitos
        Preâmbulo que deve ser ignorado.

        # Epic: Gestão de Pedidos
        Permitir compras sem atendimento humano.
        Tags: mvp, ecommerce
        Prioridade: Alta
        Valor de negócio: 80

        ## Feature: Carrinho
        ### User Story: Adicionar item ao carrinho (5 SP)
        Como cliente,
        quero adicionar produtos ao carrinho,
        para que eu possa comprá-los depois.

        **Critérios de aceite:**
        - Dado um produto com estoque, quando clico em adicionar, então ele aparece no carrinho
        - Dado um produto sem estoque, quando tento adicioná-lo, então vejo "Produto indisponível"

        - Task: Criar endpoint POST /cart (6h)
        - [Task] Criar componente de carrinho — 1 dia
        - Tarefa: Integrar gateway (sandbox)

        #### Bug: Total não atualiza (2h)
        Passos para reproduzir:
        1. Adicionar dois produtos
        2. Remover um deles
        Resultado esperado: total recalculado
        Resultado atual: total antigo
        """;

    [Fact]
    public void Parse_DocumentoEstruturado_MontaAHierarquiaCompleta()
    {
        var plan = _sut.Parse(Document);

        plan.Epics.Should().ContainSingle();
        var epic = plan.Epics[0];
        epic.Title.Should().Be("Gestão de Pedidos");
        epic.Description.Should().Be("Permitir compras sem atendimento humano.");
        epic.Tags.Should().Equal("mvp", "ecommerce");
        epic.Priority.Should().Be(2);
        epic.Estimate.OtherMeasures.Should().Contain("Valor de negócio", "80");

        var story = epic.Features.Should().ContainSingle().Subject.UserStories.Should().ContainSingle().Subject;
        story.Title.Should().Be("Adicionar item ao carrinho");
        story.Estimate.StoryPoints.Should().Be(5);
        story.AcceptanceCriteria.Should().HaveCount(2);
        story.Tasks.Select(task => task.Title).Should().Equal(
            "Criar endpoint POST /cart", "Criar componente de carrinho", "Integrar gateway (sandbox)");
    }

    [Fact]
    public void Parse_EstimativasNoTitulo_SaoExtraidasEConvertidas()
    {
        var tasks = _sut.Parse(Document).Epics[0].Features[0].UserStories[0].Tasks;

        tasks[0].Estimate.OriginalEstimateHours.Should().Be(6);
        tasks[1].Estimate.OriginalEstimateHours.Should().Be(8);
        tasks[2].Estimate.IsEmpty.Should().BeTrue("\"(sandbox)\" não é estimativa e deve permanecer no título");
    }

    [Fact]
    public void Parse_NarrativaEmVariasLinhas_EUnidaNumUnicoStatement()
    {
        var story = _sut.Parse(Document).Epics[0].Features[0].UserStories[0];

        story.Statement.Should().Be("Como cliente, quero adicionar produtos ao carrinho, para que eu possa comprá-los depois.");
    }

    [Fact]
    public void Parse_BugComPassosEResultados_PreencheOsCamposDoBug()
    {
        var bug = _sut.Parse(Document).Epics[0].Features[0].UserStories[0].Bugs.Should().ContainSingle().Subject;

        bug.Title.Should().Be("Total não atualiza");
        bug.Estimate.OriginalEstimateHours.Should().Be(2);
        bug.ReproSteps.Should().Equal("Adicionar dois produtos", "Remover um deles");
        bug.ExpectedResult.Should().Be("total recalculado");
        bug.ActualResult.Should().Be("total antigo");
    }

    [Fact]
    public void Parse_CriteriosEmGherkinSemMarcadores_AgrupaCadaCenarioNumCriterio()
    {
        const string text = """
            # Epic: E
            ## Feature: F
            ### US: Remover item
            Critérios de aceite:
            Cenário: último item
            Dado um carrinho com um item
            Quando removo o item
            Então vejo "carrinho vazio"
            Cenário: vários itens
            Dado um carrinho com dois itens
            Quando removo um
            Então resta um item
            """;

        var criteria = _sut.Parse(text).Epics[0].Features[0].UserStories[0].AcceptanceCriteria;

        criteria.Should().HaveCount(2);
        criteria[0].Should().StartWith("Cenário: último item").And.EndWith("Então vejo \"carrinho vazio\"");
    }

    [Fact]
    public void Parse_SecaoDeEstimativas_ReconheceStoryPointsHorasEMedidasLivres()
    {
        const string text = """
            # Epic: E
            ## Feature: F
            ### História de usuário: Cadastro
            **Estimativas:**
            - Story Points: 8
            - Pontos de função: 12
            - Tamanho: G
            """;

        var estimate = _sut.Parse(text).Epics[0].Features[0].UserStories[0].Estimate;

        estimate.StoryPoints.Should().Be(8);
        estimate.Size.Should().Be("G");
        estimate.OtherMeasures.Should().Contain("Pontos de função", "12");
    }

    [Fact]
    public void Parse_PalavrasChaveEmPortuguesEIngles_SaoReconhecidas()
    {
        const string text = """
            ## Épico: Atendimento
            ### Funcionalidade: Agenda
            #### PBI 1.2 - Agendar consulta (3 pontos)
            - Defeito: Horário duplicado (1h)
            """;

        var story = _sut.Parse(text).Epics[0].Features[0].UserStories[0];

        story.Title.Should().Be("Agendar consulta");
        story.Estimate.StoryPoints.Should().Be(3);
        story.Bugs.Should().ContainSingle().Which.Estimate.OriginalEstimateHours.Should().Be(1);
    }

    [Fact]
    public void Parse_UserStorySemFeature_LancaErroComALinha()
    {
        const string text = """
            # Epic: E
            ### User Story: Órfã
            """;

        var act = () => _sut.Parse(text);

        act.Should().Throw<BacklogParseException>().Where(ex => ex.LineNumber == 2 && ex.Message.Contains("Feature"));
    }

    [Fact]
    public void Parse_TaskSemUserStory_LancaErro()
    {
        var act = () => _sut.Parse("# Epic: E\n## Feature: F\n- Task: solta (2h)");

        act.Should().Throw<BacklogParseException>().Where(ex => ex.LineNumber == 3);
    }

    [Theory]
    [InlineData("# Epic: X\n## Feature: Y\n### User Story: Z", true)]
    [InlineData("# Epic: X\n## Feature: Y", false)]
    [InlineData("O paciente precisa se cadastrar com CPF e fazer login. Estimamos em 5 pontos.", false)]
    public void LooksStructured_DetectaDocumentosComHierarquiaExplicita(string text, bool expected)
    {
        MarkdownBacklogParser.LooksStructured(text).Should().Be(expected);
    }

    [Fact]
    public void Parse_SubSecaoLivre_ViraDestaqueNaDescricao()
    {
        const string text = """
            # Epic: E
            ## Feature: F
            ### User Story: Z
            Como admin, quero X, para que Y.
            #### Regras de negócio
            O CPF deve ser validado.
            """;

        var story = _sut.Parse(text).Epics[0].Features[0].UserStories[0];

        story.Description.Should().Be("**Regras de negócio**\nO CPF deve ser validado.");
    }
}
