using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace GmudAutomation.Core.Pipeline.Analysis;

/// <summary>
/// Etapa 2: extrai do HTML da Descrição todos os work items citados — links <c>_workitems/edit/&lt;id&gt;</c>
/// e menções <c>#&lt;id&gt;</c> no texto — na ordem em que aparecem (a ordem define a sequência dos merges).
/// </summary>
public static partial class WorkItemReferenceParser
{
    [GeneratedRegex(@"/_workitems/edit/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EditLinkRegex();

    // "#123" isolado; não casa âncoras HTML de cor/URL ("#fff", "page#top") nem números colados em palavras.
    [GeneratedRegex(@"(?<![\w&/])#(\d{1,9})\b")]
    private static partial Regex MentionRegex();

    public static IReadOnlyList<int> Extract(string? descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return Array.Empty<int>();
        }

        var document = new HtmlDocument();
        document.LoadHtml(descriptionHtml);

        var ids = new List<int>();
        foreach (var node in document.DocumentNode.DescendantsAndSelf())
        {
            if (node.NodeType == HtmlNodeType.Element && node.Name.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                var href = WebUtility.HtmlDecode(node.GetAttributeValue("href", string.Empty));
                foreach (Match match in EditLinkRegex().Matches(href))
                {
                    Add(ids, match.Groups[1].Value);
                }
            }
            else if (node.NodeType == HtmlNodeType.Text)
            {
                var text = WebUtility.HtmlDecode(node.InnerText);
                foreach (Match match in MentionRegex().Matches(text))
                {
                    Add(ids, match.Groups[1].Value);
                }

                // URLs coladas como texto (sem <a>) também contam.
                foreach (Match match in EditLinkRegex().Matches(text))
                {
                    Add(ids, match.Groups[1].Value);
                }
            }
        }

        return ids;
    }

    /// <summary>Texto puro de um campo HTML (descrição/comentário), para a detecção de projetos.</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var hrefs = document.DocumentNode.Descendants("a").Select(a => a.GetAttributeValue("href", string.Empty));
        return WebUtility.HtmlDecode(document.DocumentNode.InnerText + " " + string.Join(" ", hrefs));
    }

    private static void Add(List<int> ids, string value)
    {
        if (int.TryParse(value, out var id) && id > 0 && !ids.Contains(id))
        {
            ids.Add(id);
        }
    }
}
