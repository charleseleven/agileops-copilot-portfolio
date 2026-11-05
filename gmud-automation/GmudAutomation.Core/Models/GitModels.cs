namespace GmudAutomation.Core.Models;

/// <summary>Repositório Git do Azure Repos (o ID aparece nos links de artefato dos cards; o nome, no catálogo).</summary>
/// <param name="DefaultBranch">Branch padrão sem "refs/heads/" (ex: "main"); nula se o repositório estiver vazio.</param>
public sealed record GitRepositoryInfo(string Id, string Name, string? DefaultBranch = null);

/// <summary>Branch (ref <c>refs/heads/...</c>) e o commit para o qual ela aponta.</summary>
public sealed record GitBranchInfo(string Name, string ObjectId);

/// <summary>Commit com a mensagem e os pais — os pais distinguem commits de merge dos commits de conteúdo.</summary>
public sealed record GitCommitInfo(string CommitId, string Comment, IReadOnlyList<string> Parents)
{
    public bool IsMergeCommit => Parents.Count > 1;
}

/// <summary>Arquivo alterado em um commit ou em um diff entre dois commits.</summary>
/// <param name="ChangeType">Tipo informado pela API (ex: "add", "edit", "delete", "rename", "edit, rename").</param>
public sealed record GitFileChange(string Path, string ChangeType);

/// <summary>Resultado de um merge calculado no servidor: o commit gerado ou os arquivos em conflito.</summary>
public sealed record GitMergeResult(string? MergeCommitId, IReadOnlyList<string> ConflictingFiles)
{
    public bool HasConflicts => MergeCommitId is null;
}

/// <summary>Alteração de arquivo a ser enviada em um push (Pushes API).</summary>
/// <param name="ChangeType">"add", "edit" ou "delete".</param>
/// <param name="Content">Conteúdo completo do arquivo (nulo para "delete").</param>
public sealed record GitPushChange(string Path, string ChangeType, byte[]? Content);

/// <summary>Build do Azure Pipelines (etapa 11: o ID é o prefixo da tag do container).</summary>
public sealed record BuildInfo(
    int Id,
    string BuildNumber,
    string Status,
    string? Result,
    string DefinitionName,
    string SourceBranch,
    string SourceVersion,
    DateTimeOffset? QueueTime);
