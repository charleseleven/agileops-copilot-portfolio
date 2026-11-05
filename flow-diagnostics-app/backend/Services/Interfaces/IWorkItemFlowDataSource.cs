using AgilePredict.Models.Flow;

namespace AgilePredict.Services.Interfaces
{
    /// <summary>
    /// Abstrai a origem dos dados de Work Items para o motor de análise de fluxo (Task #212).
    /// Permite trocar o provedor (Azure DevOps, Jira, ...) sem afetar o cálculo estatístico
    /// nem a geração de insights, que dependem apenas do contrato <see cref="WorkItemFlowSnapshot"/>.
    /// A conexão (organização/projeto/PAT) é resolvida por requisição via <see cref="AzureDevOpsConnection"/>,
    /// permitindo atender a qualquer organização à qual o Scrum Master tenha acesso.
    /// </summary>
    public interface IWorkItemFlowDataSource
    {
        /// <summary>
        /// Retorna os Work Items ainda não concluídos da iteração informada, com o histórico
        /// completo de transições de status e o responsável atual.
        /// </summary>
        Task<IReadOnlyList<WorkItemFlowSnapshot>> GetActiveWorkItemsAsync(
            AzureDevOpsConnection connection,
            string iterationPath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Retorna os Work Items concluídos desde a data informada (usados para compor a linha
        /// de base histórica de Cycle Time por status).
        /// </summary>
        Task<IReadOnlyList<WorkItemFlowSnapshot>> GetCompletedWorkItemsSinceAsync(
            AzureDevOpsConnection connection,
            DateTime sinceUtc,
            CancellationToken cancellationToken = default);
    }
}
