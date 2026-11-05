using System.ComponentModel.DataAnnotations;

namespace AgilePredict.Models.Configuration
{
    /// <summary>
    /// Regras de negócio ágeis usadas pelo motor estatístico de fluxo (Feature #210):
    /// quais status marcam início/fim do Cycle Time e qual o limite de permanência
    /// tolerado em cada status crítico antes de ser sinalizado como gargalo.
    /// </summary>
    public class FlowAnalyticsConfiguration
    {
        /// <summary>
        /// Seção no appsettings.json
        /// </summary>
        public const string SectionName = "FlowAnalyticsSettings";

        /// <summary>
        /// Janela, em dias, usada para compor a linha de base histórica (AC #1: "últimos 90 dias").
        /// </summary>
        [Range(1, 365, ErrorMessage = "A janela de baseline deve estar entre 1 e 365 dias")]
        public int BaselineWindowDays { get; set; } = 90;

        /// <summary>
        /// Status que marcam o início da contagem de Cycle Time (quando o card passa a ser "trabalho ativo").
        /// </summary>
        public List<string> StartStatuses { get; set; } = new() { "In Progress", "Doing", "Code Review" };

        /// <summary>
        /// Status finais que marcam a conclusão de um card (fim do Cycle Time / Lead Time).
        /// </summary>
        public List<string> DoneStatuses { get; set; } = new() { "Done", "Closed", "Resolved" };

        /// <summary>
        /// Limite padrão (em dias) de permanência em um status crítico quando não há amostra
        /// histórica suficiente para calcular um limite estatístico (média + desvio padrão).
        /// Reflete o exemplo do critério de aceitação: "Code Review > 3 dias".
        /// </summary>
        [Range(1, 60, ErrorMessage = "O limite padrão deve estar entre 1 e 60 dias")]
        public double DefaultCriticalThresholdDays { get; set; } = 3.0;

        /// <summary>
        /// Múltiplo do desvio padrão somado à média histórica para definir o limite estatístico
        /// de permanência por status (quando há amostra histórica disponível).
        /// </summary>
        [Range(0.5, 5.0, ErrorMessage = "O multiplicador de desvio padrão deve estar entre 0.5 e 5.0")]
        public double StdDevMultiplier { get; set; } = 1.5;

        /// <summary>
        /// Amostra mínima de itens históricos concluídos, por status, necessária para confiar
        /// no limite estatístico calculado; abaixo disso usa-se <see cref="DefaultCriticalThresholdDays"/>.
        /// </summary>
        [Range(1, 100, ErrorMessage = "A amostra mínima deve estar entre 1 e 100")]
        public int MinimumSampleSizeForBaseline { get; set; } = 5;
    }
}
