using System.Text;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Reporting;

/// <summary>
/// Monta <c>infra.md</c> (cenário 10): por App Service, a tag atual e a nova tag; os scripts de banco na ordem
/// de execução (ordem das user stories na Descrição, depois dos itens) com as branches do desenvolvedor no
/// Database; e as branches por repositório de código.
/// A troca de tag é sempre da infra — a automação não altera App Services.
/// </summary>
public static class InfraReportFormatter
{
    public const string FileName = "infra.md";

    public static string Format(GmudManifest manifest, GmudConfig config, GmudNamingConventions naming)
    {
        var report = new StringBuilder()
            .AppendLine($"# GMUD #{manifest.Gmud.Id} — pacote para a infra")
            .AppendLine()
            .AppendLine($"- Título: {manifest.Gmud.Title}")
            .AppendLine($"- Cliente: {manifest.Gmud.Client} · Data: {manifest.Gmud.Date}")
            .AppendLine($"- User stories: {string.Join(", ", manifest.UserStories.Select(us => $"#{us.Id}"))}")
            .AppendLine()
            .AppendLine("## Troca de tag por App Service")
            .AppendLine()
            .AppendLine("| App Service | Ambiente | Repositório | Tag atual | Nova tag | Build |")
            .AppendLine("|---|---|---|---|---|---|");

        var rows = 0;
        foreach (var (repositoryName, entry) in manifest.Repositories)
        {
            var repository = config.Repositories.FirstOrDefault(r => string.Equals(r.Name, repositoryName, StringComparison.OrdinalIgnoreCase));
            if (repository?.AppService is null)
            {
                continue;
            }

            foreach (var environment in new[] { naming.ProductionEnvironment, naming.GmudEnvironment })
            {
                manifest.Environments.TryGetValue(environment, out var byKey);
                AppServiceEntry? appService = null;
                byKey?.TryGetValue(repository.EffectiveKey, out appService);
                entry.Builds.TryGetValue(environment, out var build);

                var currentTag = GmudNamingConventions.TryParseContainerImage(appService?.Image, out var current) ? current.Tag : appService?.Image ?? "—";
                var newTag = build?.NewTag is null ? "pendente" : $"`{build.NewTag}`";
                var buildText = build?.BuildId is null ? "—" : $"{build.BuildId} ({build.Result ?? build.Status})";

                report.AppendLine($"| {appService?.AppService ?? "?"} | {environment} | {repositoryName} | `{currentTag}` | {newTag} | {buildText} |");
                rows++;
            }
        }

        if (rows == 0)
        {
            report.AppendLine("| — | — | — | — | — | — |");
        }

        report.AppendLine().AppendLine("## Scripts de banco (ordem de execução)").AppendLine();
        var order = 0;
        foreach (var (userStory, item) in manifest.AllItems())
        {
            foreach (var script in item.DatabaseAttachments)
            {
                report.AppendLine($"{++order}. `{script}` — user story #{userStory.Id}, {item.Type} #{item.Id}");
            }
        }

        if (order == 0)
        {
            report.AppendLine("Nenhum script de banco nesta GMUD.");
        }

        var databaseBranches = manifest.Database?.Branches.Where(b => DatabaseBranchStatus.AffectsDatabase(b.Status)).ToList() ?? [];
        if (databaseBranches.Count > 0)
        {
            report.AppendLine().AppendLine($"## Branches do desenvolvedor em `{manifest.Database!.Repository}`").AppendLine()
                .AppendLine("Sem branch da GMUD nem release: a alteração do banco sai da feature de cada user story.").AppendLine()
                .AppendLine("| User story | Branch | Commit | Commits à frente | Situação |")
                .AppendLine("|---|---|---|---|---|");
            foreach (var branch in databaseBranches)
            {
                var name = branch.Url is null ? $"`{branch.Branch}`" : $"[{branch.Branch}]({branch.Url})";
                var commit = branch.Commit is null ? "—" : branch.Commit[..Math.Min(8, branch.Commit.Length)];
                var state = branch.Status switch
                {
                    DatabaseBranchStatus.Changed => "alterada",
                    DatabaseBranchStatus.NoCommits => $"⚠️ sem commit à frente de {branch.BaseBranch}",
                    DatabaseBranchStatus.Missing => "**não existe** (o card cita o banco)",
                    DatabaseBranchStatus.ScriptsOnly => "sem branch — só scripts anexados",
                    _ => branch.Status
                };
                report.AppendLine($"| #{branch.UserStoryId} | {name} | {commit} | {branch.CommitsAhead?.ToString() ?? "—"} | {state} |");
            }
        }

        report.AppendLine().AppendLine("## Branches").AppendLine()
            .AppendLine("| Repositório | Base (produção) | Branch da GMUD | Releases |")
            .AppendLine("|---|---|---|---|");
        foreach (var (repositoryName, entry) in manifest.Repositories)
        {
            report.AppendLine(
                $"| {repositoryName} | {entry.BaseBranch ?? "—"} @ {(entry.BaseCommit is null ? "—" : entry.BaseCommit[..Math.Min(8, entry.BaseCommit.Length)])} | " +
                $"{entry.GmudBranch} | {(entry.Releases.Count == 0 ? "—" : string.Join(", ", entry.Releases.Values))} |");
        }

        if (manifest.Warnings.Count > 0)
        {
            report.AppendLine().AppendLine("## Avisos").AppendLine();
            foreach (var warning in manifest.Warnings)
            {
                report.AppendLine($"- {warning}");
            }
        }

        return report.ToString();
    }
}
