using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Providers;

/// <summary>
/// Ferramenta de gestão onde os cards são criados (Azure DevOps, Jira, Trello...). Cada implementação
/// traduz a hierarquia Epic → Feature → User Story → Task/Bug e as estimativas para os tipos e campos
/// nativos da ferramenta — e, quando um campo não existe no processo/projeto de destino, registra o valor
/// na descrição do card em vez de falhar (nenhuma estimativa informada no documento é perdida).
/// </summary>
public interface IWorkItemTrackerProvider
{
    string Name { get; }

    /// <summary>Descobre a configuração do destino (processo, tipos de item, listas) antes da primeira criação.</summary>
    Task PrepareAsync(CancellationToken cancellationToken = default);

    Task<CreatedWorkItem> CreateAsync(WorkItemCreationRequest request, CancellationToken cancellationToken = default);
}
