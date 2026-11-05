namespace GmudAutomation.Core.Models;

/// <summary>Resultado da orquestração de PR para um repositório: se foi completada e o buildId capturado (quando aplicável).</summary>
public sealed record PullRequestOutcome(
    string RepositoryName,
    RepositoryKind Kind,
    int PullRequestId,
    string Url,
    bool Completed,
    int? BuildId);
