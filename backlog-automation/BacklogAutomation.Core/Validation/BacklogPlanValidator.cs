using System.Text.RegularExpressions;
using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Validation;

public enum ValidationSeverity
{
    Warning,
    Error
}

public sealed record ValidationIssue(ValidationSeverity Severity, WorkItemKind Kind, string ItemTitle, string Message);

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationIssue> Errors => Issues.Where(issue => issue.Severity == ValidationSeverity.Error);

    public IEnumerable<ValidationIssue> Warnings => Issues.Where(issue => issue.Severity == ValidationSeverity.Warning);
}

/// <summary>
/// Confere o backlog antes de qualquer escrita na ferramenta de destino. Erros (bloqueiam o <c>create</c>):
/// hierarquia vazia, título ausente/longo demais, estimativa negativa. Avisos (boas práticas, não bloqueiam):
/// User Story fora do formato "Como/Quero/Para que", sem critérios de aceite, story points fora da
/// sequência de Fibonacci, task maior que 2 dias, estimativa ausente quando os irmãos têm estimativa.
/// </summary>
public sealed partial class BacklogPlanValidator
{
    // Limite do campo System.Title no Azure DevOps (o mais restritivo entre Azure DevOps, Jira e Trello).
    public const int MaxTitleLength = 255;

    private static readonly decimal[] FibonacciPoints = [0, 0.5m, 1, 2, 3, 5, 8, 13, 20, 21, 34, 40, 55, 89, 100];

    private readonly decimal _hoursPerDay;

    public BacklogPlanValidator(decimal hoursPerDay = 8m)
    {
        _hoursPerDay = hoursPerDay;
    }

    public ValidationReport Validate(BacklogPlan plan)
    {
        var issues = new List<ValidationIssue>();

        if (plan.Epics.Count == 0)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, WorkItemKind.Epic, "(backlog)",
                "Nenhum Epic encontrado. O backlog precisa da hierarquia Epic → Feature → User Story → Task/Bug."));
            return new ValidationReport(issues);
        }

        foreach (var item in plan.EnumerateAll())
        {
            ValidateCommon(item, issues);
        }

        foreach (var epic in plan.Epics)
        {
            if (epic.Features.Count == 0)
            {
                Warn(issues, epic, "Epic sem Features.");
            }

            foreach (var feature in epic.Features)
            {
                if (feature.UserStories.Count == 0)
                {
                    Warn(issues, feature, "Feature sem User Stories.");
                }

                WarnMissingSiblingEstimates(issues, feature.UserStories, "story points");

                foreach (var story in feature.UserStories)
                {
                    ValidateStory(story, issues);
                }
            }
        }

        return new ValidationReport(issues);
    }

    private void ValidateStory(UserStoryItem story, List<ValidationIssue> issues)
    {
        var narrative = story.Statement ?? story.Description ?? string.Empty;
        if (!UserStoryNarrativeRegex().IsMatch(narrative))
        {
            Warn(issues, story, "Narrativa fora do formato \"Como <persona>, quero <ação>, para que <benefício>\".");
        }

        if (story.AcceptanceCriteria.Count == 0)
        {
            Warn(issues, story, "Sem critérios de aceite — a história não é testável (INVEST).");
        }

        if (story.Estimate.StoryPoints is { } points && !FibonacciPoints.Contains(points))
        {
            Warn(issues, story, $"{points} story points não está na sequência de Fibonacci (1, 2, 3, 5, 8, 13...).");
        }

        WarnMissingSiblingEstimates(issues, story.Tasks, "horas");

        foreach (var task in story.Tasks)
        {
            if (task.Estimate.OriginalEstimateHours is { } hours && hours > _hoursPerDay * 2)
            {
                Warn(issues, task, $"Task de {hours}h (> 2 dias) — considere quebrá-la em tasks menores.");
            }
        }
    }

    private static void ValidateCommon(WorkItemDraft item, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(item.Title))
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, item.Kind, "(sem título)", $"{item.Kind.DisplayName()} sem título."));
        }
        else if (item.Title.Length > MaxTitleLength)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, item.Kind, item.Title[..60] + "...",
                $"Título com {item.Title.Length} caracteres (máximo {MaxTitleLength})."));
        }

        var estimate = item.Estimate;
        if (estimate.StoryPoints < 0 || estimate.OriginalEstimateHours < 0 || estimate.RemainingHours < 0 || estimate.CompletedHours < 0)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, item.Kind, item.Title, "Estimativa negativa."));
        }

        if (item.Priority is < 1 or > 4)
        {
            issues.Add(new ValidationIssue(ValidationSeverity.Error, item.Kind, item.Title, $"Prioridade {item.Priority} inválida (use 1 a 4)."));
        }
    }

    private static void WarnMissingSiblingEstimates<T>(List<ValidationIssue> issues, IReadOnlyList<T> siblings, string unit)
        where T : WorkItemDraft
    {
        if (siblings.Count < 2 || siblings.All(sibling => sibling.Estimate.IsEmpty) || siblings.All(sibling => !sibling.Estimate.IsEmpty))
        {
            return;
        }

        foreach (var sibling in siblings.Where(sibling => sibling.Estimate.IsEmpty))
        {
            Warn(issues, sibling, $"Sem estimativa ({unit}), enquanto itens do mesmo nível foram estimados.");
        }
    }

    private static void Warn(List<ValidationIssue> issues, WorkItemDraft item, string message) =>
        issues.Add(new ValidationIssue(ValidationSeverity.Warning, item.Kind, item.Title, message));

    [GeneratedRegex(@"^\s*(como|eu como|enquanto|as an?)\b.+\b(quero|desejo|preciso|i want|i need)\b.+\b(para|a fim de|de modo que|so that|in order to)\b", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex UserStoryNarrativeRegex();
}
