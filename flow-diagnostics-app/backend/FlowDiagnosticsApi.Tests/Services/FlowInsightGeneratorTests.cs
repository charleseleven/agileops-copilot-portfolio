using AgilePredict.Models.DTOs;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Services;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AgilePredict.Tests.Services.Flow
{
    public class FlowInsightGeneratorTests
    {
        private readonly Mock<ILlmIntegrationService> _llmServiceMock = new();
        private readonly Mock<ILogger<FlowInsightGenerator>> _loggerMock = new();

        private FlowInsightGenerator CreateGenerator() => new(_llmServiceMock.Object, _loggerMock.Object);

        private static FlowMetricsPayload SamplePayload() => new()
        {
            IterationPath = "Applied AI Engineering\\Sprint 1",
            Cards = new List<CardFlowMetrics>
            {
                new() { ExternalId = 1, Title = "Card X", AssignedTo = "dev@example.com", CurrentStatus = "Code Review", IsCurrentStatusBottleneck = true }
            }
        };

        [Fact]
        public async Task GenerateAsync_WithWellFormedJsonResponse_ParsesSummaryAndRecommendations()
        {
            _llmServiceMock
                .Setup(s => s.SendPromptWithOptionsAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmResponse
                {
                    Success = true,
                    Content = "{\"summary\": \"Gargalo em Code Review.\", \"recommendations\": [\"Redistribuir carga do dev@example.com\"]}"
                });

            var generator = CreateGenerator();
            var result = await generator.GenerateAsync(SamplePayload());

            Assert.True(result.Success);
            Assert.Equal("Gargalo em Code Review.", result.Summary);
            Assert.Single(result.Recommendations);
            Assert.Contains("Redistribuir", result.Recommendations[0]);
        }

        [Fact]
        public async Task GenerateAsync_WithJsonWrappedInMarkdownFence_StillParses()
        {
            _llmServiceMock
                .Setup(s => s.SendPromptWithOptionsAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmResponse
                {
                    Success = true,
                    Content = "```json\n{\"summary\": \"Resumo\", \"recommendations\": [\"Ação 1\", \"Ação 2\"]}\n```"
                });

            var generator = CreateGenerator();
            var result = await generator.GenerateAsync(SamplePayload());

            Assert.True(result.Success);
            Assert.Equal("Resumo", result.Summary);
            Assert.Equal(2, result.Recommendations.Count);
        }

        [Fact]
        public async Task GenerateAsync_WithNonJsonResponse_FallsBackToTextualParsing()
        {
            _llmServiceMock
                .Setup(s => s.SendPromptWithOptionsAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmResponse
                {
                    Success = true,
                    Content = "Diagnóstico geral do fluxo.\n- Redistribuir tarefas do responsável sobrecarregado\n- Revisar prioridades da sprint"
                });

            var generator = CreateGenerator();
            var result = await generator.GenerateAsync(SamplePayload());

            Assert.True(result.Success);
            Assert.Contains("Diagnóstico geral", result.Summary);
            Assert.Equal(2, result.Recommendations.Count);
        }

        [Fact]
        public async Task GenerateAsync_WhenLlmFails_ReturnsUnsuccessfulResult()
        {
            _llmServiceMock
                .Setup(s => s.SendPromptWithOptionsAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LlmResponse { Success = false, ErrorMessage = "Timeout" });

            var generator = CreateGenerator();
            var result = await generator.GenerateAsync(SamplePayload());

            Assert.False(result.Success);
            Assert.Equal("Timeout", result.ErrorMessage);
        }

        [Fact]
        public async Task GenerateAsync_SendsSystemPromptAndSerializedPayload()
        {
            LlmRequest? capturedRequest = null;

            _llmServiceMock
                .Setup(s => s.SendPromptWithOptionsAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .Callback<LlmRequest, CancellationToken>((req, _) => capturedRequest = req)
                .ReturnsAsync(new LlmResponse { Success = true, Content = "{\"summary\":\"ok\",\"recommendations\":[]}" });

            var generator = CreateGenerator();
            await generator.GenerateAsync(SamplePayload());

            Assert.NotNull(capturedRequest);
            Assert.False(string.IsNullOrWhiteSpace(capturedRequest!.SystemPrompt));
            Assert.Contains("Code Review", capturedRequest.Prompt);
            Assert.Contains("dev@example.com", capturedRequest.Prompt);
        }
    }
}
