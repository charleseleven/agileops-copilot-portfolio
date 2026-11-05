namespace GmudAutomation.Core.Models;

/// <summary>Dados consolidados para o relatório de release da GMUD (US 3.2): branches, PRs e tags de build.</summary>
public sealed record ReleaseReportData(
    string ReleaseBranchName,
    string SourceBranchDescription,
    string? ApiPullRequestUrl,
    string? WebPullRequestUrl,
    IReadOnlyList<string> DatabasePullRequestUrls,
    string? WebTagToPublish,
    string? ApiTagToPublish,
    string? WebTagCurrentlyInProduction,
    string? ApiTagCurrentlyInProduction);
