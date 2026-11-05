using System.Globalization;
using System.Text;
using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Providers;

/// <summary>Seção de texto da descrição de um card (título opcional + parágrafo e/ou lista).</summary>
public sealed record DescriptionSection(string? Heading, string? Text, IReadOnlyList<string> Items, bool Ordered = false);

/// <summary>
/// Monta a descrição de um card em seções neutras e as renderiza no formato de cada ferramenta: HTML
/// (Azure DevOps), wiki markup (Jira REST v2) ou Markdown (Trello). Centraliza a regra de "o que não
/// couber num campo nativo vai para a descrição" — ex: critérios de aceite num processo sem esse campo,
/// ou medidas de estimativa sem campo correspondente.
/// </summary>
public static class DescriptionComposer
{
    public static List<DescriptionSection> Compose(
        WorkItemCreationRequest request,
        bool includeAcceptanceCriteria,
        bool includeReproSteps,
        IReadOnlyDictionary<string, string>? unmappedMeasures = null)
    {
        var sections = new List<DescriptionSection>();

        if (!string.IsNullOrWhiteSpace(request.Statement))
        {
            sections.Add(new DescriptionSection(null, request.Statement, []));
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            sections.Add(new DescriptionSection(null, request.Description, []));
        }

        if (includeReproSteps && request.Kind == WorkItemKind.Bug)
        {
            sections.AddRange(ComposeReproSteps(request));
        }

        if (includeAcceptanceCriteria && request.AcceptanceCriteria.Count > 0)
        {
            sections.Add(new DescriptionSection("Critérios de aceite", null, request.AcceptanceCriteria));
        }

        if (unmappedMeasures is { Count: > 0 })
        {
            var heading = request.Estimate.IsAiSuggested ? "Estimativas (sugeridas por IA)" : "Estimativas";
            sections.Add(new DescriptionSection(heading, null, unmappedMeasures.Select(pair => $"{pair.Key}: {pair.Value}").ToList()));
        }

        return sections;
    }

    public static List<DescriptionSection> ComposeReproSteps(WorkItemCreationRequest request)
    {
        var sections = new List<DescriptionSection>();
        if (request.ReproSteps.Count > 0)
        {
            sections.Add(new DescriptionSection("Passos para reproduzir", null, request.ReproSteps, Ordered: true));
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedResult))
        {
            sections.Add(new DescriptionSection("Resultado esperado", request.ExpectedResult, []));
        }

        if (!string.IsNullOrWhiteSpace(request.ActualResult))
        {
            sections.Add(new DescriptionSection("Resultado atual", request.ActualResult, []));
        }

        return sections;
    }

    /// <summary>Todas as medidas da estimativa em texto legível ("Story Points" → "5", "Horas estimadas" → "4h"...).</summary>
    public static Dictionary<string, string> DescribeEstimate(Estimate estimate)
    {
        var measures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (estimate.StoryPoints is { } points)
        {
            measures[MeasureNames.StoryPoints] = FormatNumber(points);
        }

        if (estimate.OriginalEstimateHours is { } original)
        {
            measures[MeasureNames.OriginalEstimate] = $"{FormatNumber(original)}h";
        }

        if (estimate.RemainingHours is { } remaining)
        {
            measures[MeasureNames.Remaining] = $"{FormatNumber(remaining)}h";
        }

        if (estimate.CompletedHours is { } completed)
        {
            measures[MeasureNames.Completed] = $"{FormatNumber(completed)}h";
        }

        if (!string.IsNullOrWhiteSpace(estimate.Size))
        {
            measures[MeasureNames.Size] = estimate.Size;
        }

        foreach (var (key, value) in estimate.OtherMeasures)
        {
            measures[key] = value;
        }

        return measures;
    }

    public static string FormatNumber(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string ToHtml(IEnumerable<DescriptionSection> sections)
    {
        var builder = new StringBuilder();
        foreach (var section in sections)
        {
            if (section.Heading is not null)
            {
                builder.Append($"<p><b>{Encode(section.Heading)}</b></p>");
            }

            if (!string.IsNullOrWhiteSpace(section.Text))
            {
                builder.Append($"<div>{Encode(section.Text)}</div>");
            }

            if (section.Items.Count > 0)
            {
                builder.Append(ToHtmlList(section.Items, section.Ordered));
            }
        }

        return builder.ToString();
    }

    public static string ToHtmlList(IEnumerable<string> items, bool ordered = false)
    {
        var tag = ordered ? "ol" : "ul";
        return $"<{tag}>{string.Concat(items.Select(item => $"<li>{Encode(item)}</li>"))}</{tag}>";
    }

    public static string ToMarkdown(IEnumerable<DescriptionSection> sections)
    {
        var blocks = new List<string>();
        foreach (var section in sections)
        {
            var block = new StringBuilder();
            if (section.Heading is not null)
            {
                block.AppendLine($"**{section.Heading}**");
            }

            if (!string.IsNullOrWhiteSpace(section.Text))
            {
                block.AppendLine(section.Text.Trim());
            }

            for (var i = 0; i < section.Items.Count; i++)
            {
                var marker = section.Ordered ? $"{i + 1}." : "-";
                block.AppendLine($"{marker} {section.Items[i].Replace("\n", "\n  ")}");
            }

            blocks.Add(block.ToString().TrimEnd());
        }

        return string.Join("\n\n", blocks);
    }

    public static string ToJiraWiki(IEnumerable<DescriptionSection> sections)
    {
        var blocks = new List<string>();
        foreach (var section in sections)
        {
            var block = new StringBuilder();
            if (section.Heading is not null)
            {
                block.AppendLine($"*{section.Heading}*");
            }

            if (!string.IsNullOrWhiteSpace(section.Text))
            {
                block.AppendLine(section.Text.Trim());
            }

            foreach (var item in section.Items)
            {
                block.AppendLine($"{(section.Ordered ? "#" : "*")} {item.Replace("\n", " \\\\ ")}");
            }

            blocks.Add(block.ToString().TrimEnd());
        }

        return string.Join("\n\n", blocks);
    }

    // Só os caracteres reservados do HTML — WebUtility.HtmlEncode também converteria acentos em entidades (&#227;).
    private static string Encode(string text) => text.Trim()
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")
        .Replace("\n", "<br/>");
}

public static class MeasureNames
{
    public const string StoryPoints = "Story Points";
    public const string OriginalEstimate = "Horas estimadas";
    public const string Remaining = "Horas restantes";
    public const string Completed = "Horas concluídas";
    public const string Size = "Tamanho";
}
