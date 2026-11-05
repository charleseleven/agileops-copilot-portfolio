using System.Text;
using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Providers;
using BacklogAutomation.Core.Validation;

namespace BacklogAutomation.Core.Formatting;

/// <summary>Renderiza o backlog como árvore legível no terminal, com estimativas e totais — a "prévia" que o PO revisa antes do <c>create</c>.</summary>
public sealed class BacklogPlanFormatter
{
    public string FormatTree(BacklogPlan plan)
    {
        var builder = new StringBuilder();

        foreach (var epic in plan.Epics)
        {
            AppendItem(builder, 0, epic, RollUpPoints(epic));
            foreach (var feature in epic.Features)
            {
                AppendItem(builder, 1, feature, RollUpPoints(feature));
                foreach (var story in feature.UserStories)
                {
                    AppendItem(builder, 2, story, rolledUpPoints: null);
                    if (story.AcceptanceCriteria.Count > 0)
                    {
                        builder.AppendLine($"{Indent(3)}  ✓ {story.AcceptanceCriteria.Count} critério(s) de aceite");
                    }

                    foreach (var task in story.Tasks)
                    {
                        AppendItem(builder, 3, task, rolledUpPoints: null);
                    }

                    foreach (var bug in story.Bugs)
                    {
                        AppendItem(builder, 3, bug, rolledUpPoints: null);
                    }
                }
            }
        }

        builder.AppendLine();
        builder.Append(FormatTotals(plan));
        return builder.ToString();
    }

    public string FormatTotals(BacklogPlan plan)
    {
        var items = plan.EnumerateAll().ToList();
        var stories = items.OfType<UserStoryItem>().ToList();
        var tasks = items.OfType<TaskItem>().ToList();
        var bugs = items.OfType<BugItem>().ToList();

        var points = stories.Sum(story => story.Estimate.StoryPoints ?? 0);
        var taskHours = tasks.Sum(task => task.Estimate.OriginalEstimateHours ?? 0);
        var bugHours = bugs.Sum(bug => bug.Estimate.OriginalEstimateHours ?? 0);

        return $"Total: {plan.Epics.Count} Epic(s), {items.Count(i => i.Kind == WorkItemKind.Feature)} Feature(s), "
            + $"{stories.Count} User Story(ies) ({DescriptionComposer.FormatNumber(points)} SP), "
            + $"{tasks.Count} Task(s) ({DescriptionComposer.FormatNumber(taskHours)}h), "
            + $"{bugs.Count} Bug(s) ({DescriptionComposer.FormatNumber(bugHours)}h)"
            + Environment.NewLine;
    }

    public string FormatValidation(ValidationReport report)
    {
        if (report.Issues.Count == 0)
        {
            return "Validação: nenhum problema encontrado." + Environment.NewLine;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Validação: {report.Errors.Count()} erro(s), {report.Warnings.Count()} aviso(s)");
        foreach (var issue in report.Issues.OrderByDescending(issue => issue.Severity))
        {
            var marker = issue.Severity == ValidationSeverity.Error ? "ERRO " : "aviso";
            builder.AppendLine($"  [{marker}] {issue.Kind.DisplayName()} \"{issue.ItemTitle}\": {issue.Message}");
        }

        return builder.ToString();
    }

    private static void AppendItem(StringBuilder builder, int depth, WorkItemDraft item, decimal? rolledUpPoints)
    {
        var label = item.Kind switch
        {
            WorkItemKind.Epic => "[EPIC]   ",
            WorkItemKind.Feature => "[FEATURE]",
            WorkItemKind.UserStory => "[US]     ",
            WorkItemKind.Task => "[TASK]   ",
            _ => "[BUG]    "
        };

        var measures = DescriptionComposer.DescribeEstimate(item.Estimate)
            .Select(pair => pair.Key switch
            {
                MeasureNames.StoryPoints => $"{pair.Value} SP",
                MeasureNames.OriginalEstimate => pair.Value,
                MeasureNames.Remaining => $"restante {pair.Value}",
                MeasureNames.Completed => $"concluído {pair.Value}",
                MeasureNames.Size => $"tam. {pair.Value}",
                _ => $"{pair.Key}: {pair.Value}"
            })
            .ToList();

        if (rolledUpPoints is > 0 && item.Estimate.StoryPoints is null)
        {
            measures.Add($"Σ {DescriptionComposer.FormatNumber(rolledUpPoints.Value)} SP");
        }

        var estimateText = measures.Count > 0 ? $"  ({string.Join(", ", measures)})" : string.Empty;
        var suggested = item.Estimate.IsAiSuggested ? " *sugerida por IA" : string.Empty;
        builder.AppendLine($"{Indent(depth)}{label} {item.Title}{estimateText}{suggested}");
    }

    private static decimal RollUpPoints(EpicItem epic) => epic.Features.Sum(RollUpPoints);

    private static decimal RollUpPoints(FeatureItem feature) => feature.UserStories.Sum(story => story.Estimate.StoryPoints ?? 0);

    private static string Indent(int depth) => new(' ', depth * 2);
}
