using System.ComponentModel.DataAnnotations;

namespace AgilePredict.Models.Configuration
{
    /// <summary>
    /// Regras de negócio do bot preditivo de gargalos em Sprints ativas (PBI 8.1, Task 1): limites
    /// de utilização da capacidade, ritmo de entrega e quais work items carregam horas.
    /// </summary>
    public class SprintRiskConfiguration
    {
        public const string SectionName = "SprintRiskSettings";

        /// <summary>
        /// Utilização (trabalho restante ÷ capacidade restante) a partir da qual a sprint fica "em risco".
        /// </summary>
        [Range(0.1, 5.0, ErrorMessage = "O limite de atenção deve estar entre 0.1 e 5.0")]
        public double WarningUtilizationThreshold { get; set; } = 0.85;

        /// <summary>
        /// Utilização a partir da qual o atraso é considerado provável (trabalho ≥ capacidade).
        /// </summary>
        [Range(0.1, 5.0, ErrorMessage = "O limite crítico deve estar entre 0.1 e 5.0")]
        public double CriticalUtilizationThreshold { get; set; } = 1.0;

        /// <summary>
        /// Fração do ritmo exigido abaixo da qual o ritmo observado de entrega gera alerta
        /// (ex.: 0.9 = entregando menos de 90% das horas/dia necessárias para fechar a sprint).
        /// </summary>
        [Range(0.1, 1.0, ErrorMessage = "A tolerância de ritmo deve estar entre 0.1 e 1.0")]
        public double PaceToleranceRatio { get; set; } = 0.9;

        /// <summary>
        /// Atraso projetado (em dias úteis) a partir do qual o alerta de ritmo passa de atenção para crítico.
        /// </summary>
        [Range(0.5, 30, ErrorMessage = "O atraso crítico deve estar entre 0.5 e 30 dias úteis")]
        public double CriticalSlipWorkingDays { get; set; } = 2;

        /// <summary>
        /// Tipos de work item que carregam horas (Original Estimate / Remaining / Completed Work).
        /// PBIs, Features e Epics ficam fora para não contar horas em dobro.
        /// </summary>
        public List<string> HourTrackedWorkItemTypes { get; set; } = new() { "Task", "Bug" };

        /// <summary>
        /// Estados que contam como entregue.
        /// </summary>
        public List<string> DoneStates { get; set; } = new() { "Done", "Closed", "Resolved" };

        /// <summary>
        /// Estados ignorados na análise.
        /// </summary>
        public List<string> ExcludedStates { get; set; } = new() { "Removed" };

        /// <summary>
        /// Fuso usado para definir "hoje" e os dias úteis da sprint.
        /// </summary>
        [Required]
        public string TimeZoneId { get; set; } = "America/Sao_Paulo";

        /// <summary>
        /// Template opcional do System Prompt (Task 2 do PBI 8.1). Vazio = template padrão embutido
        /// (<see cref="AgilePredict.Services.SprintRiskPromptBuilder.DefaultTemplate"/>).
        /// Aceita apenas os placeholders documentados; um placeholder desconhecido gera erro.
        /// </summary>
        public string? SystemPromptTemplate { get; set; }
    }
}
