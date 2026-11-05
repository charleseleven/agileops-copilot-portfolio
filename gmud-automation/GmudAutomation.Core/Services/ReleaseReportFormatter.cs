using System.Text;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IReleaseReportFormatter"/>
public sealed class ReleaseReportFormatter : IReleaseReportFormatter
{
    public string Format(ReleaseReportData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Relatório de Release (GMUD):");
        builder.AppendLine($"- Branch de release: {data.ReleaseBranchName}");
        builder.AppendLine($"- Branches de origem: {data.SourceBranchDescription}");

        builder.AppendLine("- PRs vinculadas:");
        builder.AppendLine($"  - API: {data.ApiPullRequestUrl ?? "(não gerada)"}");
        builder.AppendLine($"  - WEB: {data.WebPullRequestUrl ?? "(não gerada)"}");

        if (data.DatabasePullRequestUrls.Count == 0)
        {
            builder.AppendLine("  - Database: (nenhuma)");
        }
        else
        {
            foreach (var databasePr in data.DatabasePullRequestUrls)
            {
                builder.AppendLine($"  - Database: {databasePr}");
            }
        }

        builder.AppendLine("- Build/Pipeline:");
        builder.AppendLine($"  - TAG WEB a publicar: {data.WebTagToPublish ?? "(não capturada)"}");
        builder.AppendLine($"  - TAG API a publicar: {data.ApiTagToPublish ?? "(não capturada)"}");
        builder.AppendLine($"  - TAG WEB anterior em produção: {data.WebTagCurrentlyInProduction ?? "(desconhecida)"}");
        builder.Append($"  - TAG API anterior em produção: {data.ApiTagCurrentlyInProduction ?? "(desconhecida)"}");

        return builder.ToString();
    }
}
