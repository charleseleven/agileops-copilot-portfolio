using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.Flow;

namespace AgilePredict.Services.Interfaces
{
    /// <summary>
    /// Orquestra o pipeline completo da US #211: coleta (Task #212) → cálculo estatístico (Task #213)
    /// → geração de insights por IA (Task #214), produzindo o relatório consumido pelo frontend (Task #215).
    /// </summary>
    public interface IFlowDiagnosticsOrchestrator
    {
        Task<FlowDiagnosticReport> GenerateReportAsync(
            AzureDevOpsConnection connection,
            string iterationPath,
            CancellationToken cancellationToken = default);
    }
}
