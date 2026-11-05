using AgilePredict.Models.Flow;

namespace AgilePredict.Services.Interfaces
{
    /// <summary>
    /// Extrai organização e projeto de qualquer URL do Azure Boards (link de work item, board,
    /// sprint, backlog, etc.), permitindo que o Scrum Master simplesmente cole o link de qualquer
    /// organização à qual tenha acesso em vez de digitar organização/projeto manualmente.
    /// </summary>
    public interface IAzureDevOpsBoardsUrlParser
    {
        /// <summary>
        /// Faz o parse da URL. Lança <see cref="ArgumentException"/> se a URL não for reconhecida
        /// como uma URL válida do Azure DevOps.
        /// </summary>
        AzureDevOpsProjectReference Parse(string url);

        /// <summary>
        /// Tenta extrair o Iteration Path de uma URL de sprint do Azure Boards (taskboard ou
        /// backlog, ex.: ".../_sprints/taskboard/{time}/{projeto}/{sprint}"). Retorna null quando
        /// a URL não é reconhecida como uma URL de sprint — não lança exceção, pois é usado como
        /// tentativa best-effort para tolerar o usuário colando a URL da sprint por engano no
        /// campo de Iteration Path.
        /// </summary>
        string? TryExtractIterationPath(string url);
    }
}
