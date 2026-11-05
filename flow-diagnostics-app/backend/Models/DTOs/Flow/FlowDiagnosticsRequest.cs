using System.ComponentModel.DataAnnotations;

namespace AgilePredict.Models.DTOs.Flow
{
    /// <summary>
    /// Requisição de diagnóstico de fluxo. A organização/projeto podem ser informados de duas
    /// formas: colando qualquer URL do Azure Boards dessa organização (<see cref="BoardsUrl"/>,
    /// mais prático) ou explicitamente (<see cref="Organization"/> + <see cref="Project"/>).
    /// Quando nenhum dos dois é informado, o backend usa os valores padrão configurados no
    /// servidor (se houver). O <see cref="PersonalAccessToken"/> segue a mesma lógica de fallback.
    /// Por conter um segredo, esta requisição é enviada via POST (nunca em query string).
    /// </summary>
    public class FlowDiagnosticsRequest
    {
        /// <summary>
        /// Qualquer URL do Azure Boards da organização a ser analisada (link de work item, board,
        /// sprint, backlog, etc.). Organização e projeto são extraídos automaticamente dela.
        /// </summary>
        public string? BoardsUrl { get; set; }

        /// <summary>
        /// Organização no Azure DevOps (alternativa a <see cref="BoardsUrl"/>).
        /// </summary>
        public string? Organization { get; set; }

        /// <summary>
        /// Projeto no Azure DevOps (alternativa a <see cref="BoardsUrl"/>).
        /// </summary>
        public string? Project { get; set; }

        /// <summary>
        /// Caminho da iteração/sprint a ser analisada (ex.: "Applied AI Engineering\Sprint 4").
        /// </summary>
        [Required(ErrorMessage = "O caminho da iteração (sprint) é obrigatório")]
        public string IterationPath { get; set; } = string.Empty;

        /// <summary>
        /// Personal Access Token com acesso à organização informada. Se omitido, usa o PAT
        /// padrão configurado no servidor (quando houver).
        /// </summary>
        public string? PersonalAccessToken { get; set; }
    }
}
