using AgilePredict.Models.DTOs;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Models.Flow;
using AgilePredict.Models.SprintRisk;
using AgilePredict.Services;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace AgilePredict.Tests.Services.SprintRisk
{
    public class SprintRiskBotTests
    {
        private static readonly AzureDevOpsConnection Connection = new("contoso", "Projeto", "pat");

        private readonly Mock<ISprintCapacityDataSource> _dataSource = new();
        private readonly Mock<ISprintRiskAnalyzer> _analyzer = new();
        private readonly Mock<ISprintRiskInsightGenerator> _insight = new();
        private readonly SprintRiskAnalysis _analysis = new() { IterationPath = "Projeto\\Sprint 8", RiskLevel = SprintRiskLevel.Critical };

        public SprintRiskBotTests()
        {
            var snapshot = SprintRiskTestData.Snapshot();
            _dataSource.Setup(d => d.GetSnapshotAsync(Connection, "Time", "Projeto\\Sprint 8", It.IsAny<CancellationToken>()))
                .ReturnsAsync(snapshot);
            _analyzer.Setup(a => a.Analyze(snapshot)).Returns(_analysis);
        }

        private SprintRiskBot Bot() => new(_dataSource.Object, _analyzer.Object, _insight.Object, Mock.Of<ILogger<SprintRiskBot>>());

        [Fact]
        public async Task GenerateReportAsync_ComParecer_JuntaAnaliseEInsightDaIa()
        {
            _insight.Setup(i => i.GenerateAsync(_analysis, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FlowInsightResult { Success = true, Summary = "Risco alto", Recommendations = new() { "Redistribuir" } });

            var report = await Bot().GenerateReportAsync(Connection, "Time", "Projeto\\Sprint 8", includeAiInsight: true);

            Assert.Same(_analysis, report.Analysis);
            Assert.Equal("Risco alto", report.AiSummary);
            Assert.Equal(new[] { "Redistribuir" }, report.AiRecommendations);
            Assert.False(report.AiInsightUnavailable);
        }

        [Fact]
        public async Task GenerateReportAsync_SemParecer_NaoChamaALlm()
        {
            var report = await Bot().GenerateReportAsync(Connection, "Time", "Projeto\\Sprint 8", includeAiInsight: false);

            Assert.Same(_analysis, report.Analysis);
            Assert.False(report.AiInsightUnavailable);
            _insight.Verify(i => i.GenerateAsync(It.IsAny<SprintRiskAnalysis>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GenerateReportAsync_LlmFalha_MantemAnaliseESinalizaIndisponibilidade()
        {
            _insight.Setup(i => i.GenerateAsync(_analysis, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("timeout"));

            var report = await Bot().GenerateReportAsync(Connection, "Time", "Projeto\\Sprint 8", includeAiInsight: true);

            Assert.Same(_analysis, report.Analysis);
            Assert.True(report.AiInsightUnavailable);
        }

        [Fact]
        public async Task GenerateReportAsync_LlmSemSucesso_SinalizaIndisponibilidade()
        {
            _insight.Setup(i => i.GenerateAsync(_analysis, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FlowInsightResult { Success = false, ErrorMessage = "quota" });

            var report = await Bot().GenerateReportAsync(Connection, "Time", "Projeto\\Sprint 8", includeAiInsight: true);

            Assert.True(report.AiInsightUnavailable);
            Assert.Empty(report.AiSummary);
        }
    }

    public class SprintRiskInsightGeneratorTests
    {
        private readonly Mock<ILlmIntegrationService> _llm = new();
        private readonly Mock<ISprintRiskPromptBuilder> _prompt = new();
        private readonly SprintRiskAnalysis _analysis = new();
        private readonly LlmRequest _request = new() { Prompt = "dados", SystemPrompt = "sistema" };

        public SprintRiskInsightGeneratorTests() => _prompt.Setup(p => p.Build(_analysis)).Returns(_request);

        private SprintRiskInsightGenerator Generator() => new(_llm.Object, _prompt.Object, Mock.Of<ILogger<SprintRiskInsightGenerator>>());

        private void LlmReturns(LlmResponse response) =>
            _llm.Setup(l => l.SendPromptWithOptionsAsync(_request, It.IsAny<CancellationToken>())).ReturnsAsync(response);

        [Fact]
        public async Task GenerateAsync_EnviaOPromptDinamicoEInterpretaOJson()
        {
            LlmReturns(new LlmResponse
            {
                Success = true,
                Content = "```json\n{\"summary\":\"Sprint em risco\",\"recommendations\":[\"Mover #2 para Bia\",\" \"]}\n```"
            });

            var result = await Generator().GenerateAsync(_analysis);

            Assert.True(result.Success);
            Assert.Equal("Sprint em risco", result.Summary);
            Assert.Equal(new[] { "Mover #2 para Bia" }, result.Recommendations);
        }

        [Fact]
        public async Task GenerateAsync_RespostaForaDoFormato_UsaTextoComoResumo()
        {
            LlmReturns(new LlmResponse { Success = true, Content = "A sprint está atrasada." });

            var result = await Generator().GenerateAsync(_analysis);

            Assert.True(result.Success);
            Assert.Equal("A sprint está atrasada.", result.Summary);
            Assert.Empty(result.Recommendations);
        }

        [Fact]
        public async Task GenerateAsync_LlmComErro_RetornaFalha()
        {
            LlmReturns(new LlmResponse { Success = false, ErrorMessage = "401" });

            var result = await Generator().GenerateAsync(_analysis);

            Assert.False(result.Success);
            Assert.Equal("401", result.ErrorMessage);
        }
    }
}
