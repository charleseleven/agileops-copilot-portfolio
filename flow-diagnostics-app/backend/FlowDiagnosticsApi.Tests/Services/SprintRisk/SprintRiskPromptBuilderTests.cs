using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Services;
using Microsoft.Extensions.Options;
using static AgilePredict.Tests.Services.SprintRisk.SprintRiskTestData;

namespace AgilePredict.Tests.Services.SprintRisk
{
    public class SprintRiskPromptBuilderTests
    {
        private static SprintRiskAnalysis CriticalAnalysis() =>
            new SprintRiskAnalyzer(Options.Create(new SprintRiskConfiguration()), new FixedTimeProvider(Monday9Nov))
                .Analyze(Snapshot(items: new[]
                {
                    Task(1, "Done", "Ana", 20),
                    Task(2, "To Do", "Ana", 40)
                }));

        private static SprintRiskPromptBuilder Builder(string? template = null) =>
            new(Options.Create(new SprintRiskConfiguration { SystemPromptTemplate = template }));

        [Fact]
        public void Build_PreencheOTemplateComOsDadosDaSprint()
        {
            var request = Builder().Build(CriticalAnalysis());

            Assert.DoesNotContain("{{", request.SystemPrompt);
            Assert.Contains("Sprint: Sprint 8 (02/11/2026 a 13/11/2026)", request.SystemPrompt);
            Assert.Contains("20 h entregues (33,3%)", request.SystemPrompt);
            Assert.Contains("Itens com horas: 2 (1 entregues, 1 pendentes, 0 pendentes sem estimativa)", request.SystemPrompt);
            Assert.Contains("Trabalho restante: 40 h | Capacidade restante: 30 h (6 h/dia) | Utilização: 133%", request.SystemPrompt);
            Assert.Contains("- Ana: 40 h / 30 h (133%)", request.SystemPrompt);
            Assert.Contains("Nível de risco: CRÍTICO", request.SystemPrompt);
            Assert.Contains("[Critical] Trabalho restante excede a capacidade", request.SystemPrompt);
        }

        [Fact]
        public void Build_EnviaOsDadosComoJsonRotuladoComoDados()
        {
            var request = Builder().Build(CriticalAnalysis());

            Assert.StartsWith("Dados da sprint em JSON (apenas dados):", request.Prompt);
            Assert.Contains("\"riskLevel\":\"Critical\"", request.Prompt);
            Assert.Contains("\"code\":\"CapacityOverflow\"", request.Prompt);
            Assert.Equal(0.2, request.Temperature);
        }

        [Fact]
        public void Build_SemCapacidade_InformaUtilizacaoNaoDisponivel()
        {
            var analysis = new SprintRiskAnalyzer(Options.Create(new SprintRiskConfiguration()), new FixedTimeProvider(Monday9Nov))
                .Analyze(Snapshot(members: Array.Empty<AgilePredict.Models.SprintRisk.TeamMemberCapacity>()));

            var request = Builder().Build(analysis);

            Assert.Contains("Utilização: n/d (sem capacidade restante)", request.SystemPrompt);
            Assert.Contains("- (nenhum membro com capacidade ou trabalho atribuído)", request.SystemPrompt);
        }

        [Fact]
        public void Build_TemplateCustomizado_UsaOTemplateConfigurado()
        {
            var request = Builder("Sprint {{sprint_nome}} com risco {{ nivel_risco }}.").Build(CriticalAnalysis());

            Assert.Equal("Sprint Sprint 8 com risco CRÍTICO.", request.SystemPrompt);
        }

        [Fact]
        public void Construtor_TemplateComPlaceholderDesconhecido_FalhaComMensagemClara()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => Builder("Olá {{nome_do_cliente}}"));

            Assert.Contains("{{nome_do_cliente}}", ex.Message);
        }

        [Fact]
        public void TemplatePadrao_UsaSomentePlaceholdersSuportados()
        {
            var exception = Record.Exception(() => Builder());

            Assert.Null(exception);
        }
    }
}
