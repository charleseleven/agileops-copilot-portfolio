using AgilePredict.Models.DTOs;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Models.Flow;
using AgilePredict.Models.SprintRisk;

namespace AgilePredict.Services.Interfaces
{
    /// <summary>
    /// Coleta assíncrona dos dados da Sprint no Azure Boards (PBI 8.1, AC 1). Isola o I/O da regra
    /// analítica: trocar a fonte (outro provedor, cache, fake de teste) não mexe no cálculo.
    /// </summary>
    public interface ISprintCapacityDataSource
    {
        /// <param name="team">Time do Azure DevOps; nulo = time padrão do projeto.</param>
        /// <param name="iterationPath">Sprint; nulo = sprint atual do time.</param>
        /// <exception cref="SprintNotFoundException">Time sem sprint atual, ou sprint informada não encontrada.</exception>
        Task<SprintCapacitySnapshot> GetSnapshotAsync(
            AzureDevOpsConnection connection,
            string? team,
            string? iterationPath,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Regra analítica do bot (PBI 8.1, Task 1): função pura do retrato da sprint para a análise de risco.
    /// </summary>
    public interface ISprintRiskAnalyzer
    {
        SprintRiskAnalysis Analyze(SprintCapacitySnapshot snapshot);
    }

    /// <summary>
    /// Template dinâmico do System Prompt alimentado pelos dados da sprint (PBI 8.1, Task 2).
    /// </summary>
    public interface ISprintRiskPromptBuilder
    {
        LlmRequest Build(SprintRiskAnalysis analysis);
    }

    /// <summary>
    /// Parecer da IA sobre a análise de risco (resumo + recomendações).
    /// </summary>
    public interface ISprintRiskInsightGenerator
    {
        Task<FlowInsightResult> GenerateAsync(SprintRiskAnalysis analysis, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// O bot: coleta → análise → parecer da IA, produzindo o relatório consumido pelo dashboard (PBI 8.2).
    /// </summary>
    public interface ISprintRiskBot
    {
        Task<SprintRiskReport> GenerateReportAsync(
            AzureDevOpsConnection connection,
            string? team,
            string? iterationPath,
            bool includeAiInsight,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Sprint/time não encontrados no Azure DevOps (erro do usuário, não falha de comunicação).
    /// </summary>
    public sealed class SprintNotFoundException(string message) : Exception(message);
}
