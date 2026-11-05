using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.Flow;
using AgilePredict.Services;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AgilePredict.Tests.Services.Flow
{
    public class FlowDiagnosticsOrchestratorTests
    {
        private readonly Mock<IWorkItemFlowDataSource> _dataSourceMock = new();
        private readonly Mock<IFlowMetricsCalculator> _calculatorMock = new();
        private readonly Mock<IFlowInsightGenerator> _insightGeneratorMock = new();
        private readonly Mock<ILogger<FlowDiagnosticsOrchestrator>> _loggerMock = new();

        private FlowDiagnosticsOrchestrator CreateOrchestrator()
        {
            var config = new FlowAnalyticsConfiguration { BaselineWindowDays = 90 };
            return new FlowDiagnosticsOrchestrator(
                _dataSourceMock.Object,
                _calculatorMock.Object,
                _insightGeneratorMock.Object,
                Options.Create(config),
                _loggerMock.Object);
        }

        private static readonly AzureDevOpsConnection Connection = new("org", "proj", "pat123");

        private FlowMetricsPayload SetupPipeline()
        {
            var activeItems = new List<WorkItemFlowSnapshot> { new() { ExternalId = 1 } };
            var completedItems = new List<WorkItemFlowSnapshot> { new() { ExternalId = 2 } };

            _dataSourceMock
                .Setup(d => d.GetActiveWorkItemsAsync(Connection, "Sprint 1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(activeItems);
            _dataSourceMock
                .Setup(d => d.GetCompletedWorkItemsSinceAsync(Connection, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(completedItems);

            var payload = new FlowMetricsPayload
            {
                IterationPath = "Sprint 1",
                BottleneckAlerts = new List<BottleneckAlert>
                {
                    new() { ExternalId = 1, Status = "Code Review", AssignedTo = "dev@example.com" }
                }
            };

            _calculatorMock
                .Setup(c => c.Calculate("Sprint 1", activeItems, completedItems, null))
                .Returns(payload);

            return payload;
        }

        [Fact]
        public async Task GenerateReportAsync_WithSuccessfulInsight_PopulatesAiFields()
        {
            var payload = SetupPipeline();

            _insightGeneratorMock
                .Setup(g => g.GenerateAsync(payload, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FlowInsightResult
                {
                    Success = true,
                    Summary = "Resumo gerado",
                    Recommendations = new List<string> { "Redistribuir carga" }
                });

            var orchestrator = CreateOrchestrator();
            var report = await orchestrator.GenerateReportAsync(Connection, "Sprint 1");

            Assert.False(report.AiInsightUnavailable);
            Assert.Equal("Resumo gerado", report.AiDiagnosticSummary);
            Assert.Single(report.AiRecommendations);
            Assert.Single(report.BottleneckAlerts);
        }

        [Fact]
        public async Task GenerateReportAsync_WhenInsightGenerationFails_StillReturnsStatisticalReport()
        {
            var payload = SetupPipeline();

            _insightGeneratorMock
                .Setup(g => g.GenerateAsync(payload, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FlowInsightResult { Success = false, ErrorMessage = "LLM indisponível" });

            var orchestrator = CreateOrchestrator();
            var report = await orchestrator.GenerateReportAsync(Connection, "Sprint 1");

            Assert.True(report.AiInsightUnavailable);
            Assert.Empty(report.AiDiagnosticSummary);
            Assert.Single(report.BottleneckAlerts);
        }

        [Fact]
        public async Task GenerateReportAsync_WhenInsightGeneratorThrows_StillReturnsStatisticalReport()
        {
            SetupPipeline();

            _insightGeneratorMock
                .Setup(g => g.GenerateAsync(It.IsAny<FlowMetricsPayload>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            var orchestrator = CreateOrchestrator();
            var report = await orchestrator.GenerateReportAsync(Connection, "Sprint 1");

            Assert.True(report.AiInsightUnavailable);
            Assert.NotNull(report.Metrics);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GenerateReportAsync_WithInvalidIterationPath_Throws(string? iterationPath)
        {
            var orchestrator = CreateOrchestrator();
            await Assert.ThrowsAsync<ArgumentException>(() => orchestrator.GenerateReportAsync(Connection, iterationPath!));
        }
    }
}
