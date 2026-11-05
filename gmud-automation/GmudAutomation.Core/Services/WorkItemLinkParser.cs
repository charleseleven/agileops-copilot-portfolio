using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace GmudAutomation.Core.Services;

/// <summary>Extrai, via HTML Agility Pack, os IDs de work items filhos referenciados na descrição de uma US.</summary>
public sealed partial class WorkItemLinkParser : IWorkItemLinkParser
{
    [GeneratedRegex(@"/_workitems/edit/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WorkItemIdRegex();

    public IReadOnlyList<int> ExtractChildWorkItemIds(string? descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return Array.Empty<int>();
        }

        var document = new HtmlDocument();
        document.LoadHtml(descriptionHtml);

        var anchorNodes = document.DocumentNode.SelectNodes("//a[@href]");
        if (anchorNodes is null)
        {
            return Array.Empty<int>();
        }

        var ids = new List<int>();
        foreach (var anchor in anchorNodes)
        {
            var href = anchor.GetAttributeValue("href", string.Empty);
            var match = WorkItemIdRegex().Match(href);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
