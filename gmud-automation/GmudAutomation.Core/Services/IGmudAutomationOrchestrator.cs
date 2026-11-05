using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>
/// Orquestra o processo completo de GMUD (Epic: Automação e Orquestração do Processo de GMUD):
/// mapeia a hierarquia da User Story principal (US 1.1) e, para cada work item filho, identifica
/// o impacto técnico e publica o resultado no card correspondente (US 1.2).
/// </summary>
public interface IGmudAutomationOrchestrator
{
    Task<GmudRequest> RunAsync(int gmudWorkItemId, CancellationToken cancellationToken = default);
}
