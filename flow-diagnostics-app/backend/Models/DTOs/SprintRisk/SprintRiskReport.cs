using System.ComponentModel.DataAnnotations;

namespace AgilePredict.Models.DTOs.SprintRisk
{
    /// <summary>
    /// Relatório formatado do bot de risco (PBI 8.2, Task 2): análise determinística + parecer da IA.
    /// </summary>
    public sealed class SprintRiskReport
    {
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
        public SprintRiskAnalysis Analysis { get; set; } = new();
        public string AiSummary { get; set; } = string.Empty;
        public List<string> AiRecommendations { get; set; } = new();

        /// <summary>
        /// A LLM falhou; a análise continua válida porque não depende dela.
        /// </summary>
        public bool AiInsightUnavailable { get; set; }
    }

    /// <summary>
    /// Requisição do relatório de risco. Enviada via POST porque pode carregar um PAT.
    /// </summary>
    public sealed class SprintRiskRequest
    {
        /// <summary>Qualquer URL do Azure Boards da organização/projeto.</summary>
        public string? BoardsUrl { get; set; }

        public string? Organization { get; set; }
        public string? Project { get; set; }

        /// <summary>Time do Azure DevOps. Vazio = time padrão "&lt;Projeto&gt; Team".</summary>
        [StringLength(256)]
        public string? Team { get; set; }

        /// <summary>Sprint a analisar. Vazio = sprint atual do time.</summary>
        public string? IterationPath { get; set; }

        /// <summary>PAT com acesso à organização. Omitir para usar o PAT configurado no servidor.</summary>
        public string? PersonalAccessToken { get; set; }

        /// <summary>
        /// Gera o parecer da IA (padrão). O dashboard desliga nos refreshes automáticos para não
        /// chamar a LLM a cada ciclo; a análise determinística vem sempre.
        /// </summary>
        public bool IncludeAiInsight { get; set; } = true;
    }
}
