namespace GmudAutomation.Core.Models;

/// <summary>Dados de um Pull Request criado no Azure Repos.</summary>
public sealed record PullRequestInfo(int Id, string Url, string Status, string LastMergeSourceCommitId);
