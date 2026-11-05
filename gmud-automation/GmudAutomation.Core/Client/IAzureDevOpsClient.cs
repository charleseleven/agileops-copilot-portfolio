using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Client;

/// <summary>
/// Abstrai o acesso às APIs REST do Azure DevOps (Boards e Repos), isolando a regra de negócio
/// da comunicação HTTP real para permitir testes de unidade sem chamadas de rede.
/// </summary>
public interface IAzureDevOpsClient
{
    Task<WorkItemInfo> GetWorkItemAsync(int workItemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Work Item com as relações expandidas (filhos, relacionados, anexos e links de commit/PR/branch) —
    /// usado pelo fluxo automatizado de GMUD (etapas 2 a 4).
    /// </summary>
    Task<WorkItemDetails> GetWorkItemDetailsAsync(int workItemId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkItemComment>> GetWorkItemCommentsAsync(int workItemId, CancellationToken cancellationToken = default);

    /// <summary>Retorna os IDs dos work items filhos (relação Hierarchy-Forward, ex: Tasks/Bugs de uma User Story).</summary>
    Task<IReadOnlyList<int>> GetChildWorkItemIdsAsync(int workItemId, CancellationToken cancellationToken = default);

    Task<bool> BranchExistsAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default);

    /// <summary>Publica um comentário no card do Work Item, usado para registrar o resultado da análise automática.</summary>
    Task AddWorkItemCommentAsync(int workItemId, string commentText, CancellationToken cancellationToken = default);

    /// <summary>Cria uma nova branch (Task 198) a partir do commit apontado por <paramref name="sourceBranchName"/>.</summary>
    Task<string> CreateBranchAsync(string repositoryName, string sourceBranchName, string newBranchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mescla <paramref name="sourceBranchName"/> em <paramref name="targetBranchName"/> (Task 199).
    /// Lança <see cref="MergeConflictException"/> quando a API retorna conflito (HTTP 409).
    /// </summary>
    Task MergeBranchesAsync(string repositoryName, string sourceBranchName, string targetBranchName, CancellationToken cancellationToken = default);

    /// <summary>Cria um Pull Request (Task 202), opcionalmente como rascunho (usado para repositórios de Database).</summary>
    Task<PullRequestInfo> CreatePullRequestAsync(
        string repositoryName, string sourceBranchName, string targetBranchName, string title, string description, bool isDraft,
        CancellationToken cancellationToken = default);

    /// <summary>Completa (merge automático) um Pull Request já criado (Task 203).</summary>
    Task CompletePullRequestAsync(string repositoryName, int pullRequestId, string lastMergeSourceCommitId, CancellationToken cancellationToken = default);

    /// <summary>Retorna o ID do build mais recente disparado para a branch informada, ou null se nenhum for encontrado (Task 204).</summary>
    Task<int?> GetLatestBuildIdForBranchAsync(string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retorna a data (queueTime) do build mais recente disparado para a branch informada, ou null se
    /// nenhum build for encontrado — usado para decidir se uma release de GMUD é recente o bastante
    /// (dentro de 14 dias) para ser mesclada automaticamente numa nova branch de release.
    /// </summary>
    Task<DateTimeOffset?> GetLatestBuildDateForBranchAsync(string branchName, CancellationToken cancellationToken = default);

    /// <summary>Baixa o conteúdo binário de um anexo de Work Item (Task 206).</summary>
    Task<byte[]> DownloadAttachmentAsync(string attachmentUrl, CancellationToken cancellationToken = default);

    /// <summary>Envia um novo anexo (já renomeado) e retorna a URL do anexo criado (Task 206).</summary>
    Task<string> UploadAttachmentAsync(string fileName, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>Vincula um anexo já enviado a um Work Item (Task 206).</summary>
    Task AttachFileToWorkItemAsync(int workItemId, string attachmentUrl, string comment, CancellationToken cancellationToken = default);
}
