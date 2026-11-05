using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.Flow;

namespace AgilePredict.Services.Interfaces
{
    /// <summary>
    /// Motor de cálculo estatístico de fluxo (Task #213): processa Lead Time, Cycle Time
    /// segmentado por status e vazão semanal, estruturando os dados em um payload padronizado.
    /// </summary>
    public interface IFlowMetricsCalculator
    {
        /// <summary>
        /// Calcula o payload de métricas de fluxo para a iteração informada.
        /// </summary>
        /// <param name="iterationPath">Sprint/iteração analisada.</param>
        /// <param name="activeItems">Work Items ainda ativos (não concluídos) da iteração.</param>
        /// <param name="baselineCompletedItems">Work Items concluídos na janela de baseline (ex.: últimos 90 dias), usados para calcular médias e desvios-padrão por status.</param>
        /// <param name="asOfUtc">Instante de referência para os cálculos (facilita testes determinísticos); usa <see cref="DateTime.UtcNow"/> quando omitido.</param>
        FlowMetricsPayload Calculate(
            string iterationPath,
            IReadOnlyList<WorkItemFlowSnapshot> activeItems,
            IReadOnlyList<WorkItemFlowSnapshot> baselineCompletedItems,
            DateTime? asOfUtc = null);
    }
}
