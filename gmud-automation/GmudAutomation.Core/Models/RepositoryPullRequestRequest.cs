namespace GmudAutomation.Core.Models;

/// <summary>Solicitação de abertura de PR para um repositório específico da GMUD (US 3.1).</summary>
public sealed record RepositoryPullRequestRequest(
    string RepositoryName,
    RepositoryKind Kind,
    string SourceBranch,
    string TargetBranch,
    string Title,
    string Description);
