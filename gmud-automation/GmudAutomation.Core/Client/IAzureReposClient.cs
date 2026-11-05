using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Client;

/// <summary>
/// Operações Git e de Pipelines no Azure Repos usadas pelo fluxo automatizado de GMUD (etapas 7 a 11).
/// Separado de <see cref="IAzureDevOpsClient"/> para que cada etapa dependa só do que usa.
/// </summary>
/// <remarks>
/// Não há operação de exclusão de branch nem de "force push": toda atualização de ref exige o
/// <c>oldObjectId</c> esperado, então uma branch que mudou no servidor nunca é sobrescrita.
/// </remarks>
public interface IAzureReposClient
{
    Task<IReadOnlyList<GitRepositoryInfo>> ListRepositoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Retorna a branch, ou null quando ela (ou o repositório) não existe.</summary>
    Task<GitBranchInfo?> GetBranchAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default);

    /// <summary>Lista as branches cujo nome começa com <paramref name="prefix"/> (ex: "release/GMUD_PROD_CLI1_").</summary>
    Task<IReadOnlyList<GitBranchInfo>> ListBranchesAsync(string repositoryName, string prefix, CancellationToken cancellationToken = default);

    /// <summary>Cria <paramref name="newBranchName"/> apontando para <paramref name="commitId"/>; falha se a branch já existir.</summary>
    Task CreateBranchFromCommitAsync(string repositoryName, string newBranchName, string commitId, CancellationToken cancellationToken = default);

    /// <summary>Move a branch de <paramref name="expectedOldCommitId"/> para <paramref name="newCommitId"/> (falha se ela mudou no meio do caminho).</summary>
    Task UpdateBranchAsync(string repositoryName, string branchName, string expectedOldCommitId, string newCommitId, CancellationToken cancellationToken = default);

    /// <summary>Commits alcançáveis por <paramref name="itemCommitId"/> e não por <paramref name="compareCommitId"/>, do mais recente ao mais antigo.</summary>
    Task<IReadOnlyList<GitCommitInfo>> GetCommitsAheadAsync(
        string repositoryName, string itemCommitId, string compareCommitId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitFileChange>> GetCommitChangesAsync(string repositoryName, string commitId, CancellationToken cancellationToken = default);

    /// <summary>Ancestral comum mais recente entre dois commits, ou null se não houver.</summary>
    Task<string?> GetMergeBaseAsync(string repositoryName, string commitId, string otherCommitId, CancellationToken cancellationToken = default);

    /// <summary>Arquivos que diferem entre <paramref name="baseCommitId"/> e <paramref name="targetCommitId"/>.</summary>
    Task<IReadOnlyList<GitFileChange>> GetDiffAsync(
        string repositoryName, string baseCommitId, string targetCommitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calcula no servidor o merge de <paramref name="sourceCommitId"/> em <paramref name="targetCommitId"/> e
    /// devolve o commit gerado — SEM mover nenhuma branch (isso é feito depois com <see cref="UpdateBranchAsync"/>).
    /// </summary>
    Task<GitMergeResult> MergeCommitsAsync(
        string repositoryName, string targetCommitId, string sourceCommitId, string commitMessage, CancellationToken cancellationToken = default);

    Task<byte[]> GetFileContentAsync(string repositoryName, string path, string commitId, CancellationToken cancellationToken = default);

    /// <summary>Cria um commit com <paramref name="changes"/> sobre a branch (que deve estar em <paramref name="expectedOldCommitId"/>) e retorna o novo commit.</summary>
    Task<string> PushChangesAsync(
        string repositoryName, string branchName, string expectedOldCommitId, string commitMessage, IReadOnlyList<GitPushChange> changes,
        CancellationToken cancellationToken = default);

    /// <summary>Builds da branch, do mais recente ao mais antigo.</summary>
    Task<IReadOnlyList<BuildInfo>> ListBuildsForBranchAsync(string branchName, int top = 10, CancellationToken cancellationToken = default);
}
