using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

namespace BacklogAutomation.Core.Documents;

/// <summary>
/// Extrai o texto de um documento anexado (.md, .txt, .json, .docx, .pdf). Word é convertido para
/// Markdown preservando títulos (Título 1..6 / Heading 1..6 → "#".."######") e listas ("- "), para que um
/// documento de requisitos já estruturado em Word seja interpretado pelo parser determinístico sem IA.
/// </summary>
public sealed partial class DocumentTextExtractor : IDocumentTextExtractor
{
    public static readonly IReadOnlyList<string> SupportedExtensions = [".md", ".markdown", ".txt", ".json", ".docx", ".pdf"];

    public async Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Documento não encontrado: {filePath}", filePath);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var text = extension switch
        {
            ".md" or ".markdown" or ".txt" or ".json" => await File.ReadAllTextAsync(filePath, cancellationToken),
            ".docx" => ExtractWord(filePath),
            ".pdf" => ExtractPdf(filePath),
            _ => throw new NotSupportedException(
                $"Formato \"{extension}\" não suportado. Use um dos formatos: {string.Join(", ", SupportedExtensions)}.")
        };

        var format = extension switch
        {
            ".json" => DocumentFormatKind.Json,
            ".pdf" or ".txt" => DocumentFormatKind.PlainText,
            _ => DocumentFormatKind.Markdown
        };

        return new ExtractedDocument(Path.GetFileName(filePath), text, format);
    }

    private static string ExtractWord(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var styles = document.MainDocumentPart!.StyleDefinitionsPart?.Styles;
        var builder = new StringBuilder();

        foreach (var element in body.ChildElements)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    AppendParagraph(builder, paragraph, styles);
                    break;

                case Table table:
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>().Select(cell => cell.InnerText.Trim());
                        builder.AppendLine($"| {string.Join(" | ", cells)} |");
                    }

                    builder.AppendLine();
                    break;
            }
        }

        return builder.ToString();
    }

    private static void AppendParagraph(StringBuilder builder, Paragraph paragraph, Styles? styles)
    {
        var text = paragraph.InnerText.Trim();
        if (text.Length == 0)
        {
            builder.AppendLine();
            return;
        }

        var properties = paragraph.ParagraphProperties;
        var headingLevel = ResolveHeadingLevel(properties?.ParagraphStyleId?.Val?.Value, styles)
            ?? (properties?.OutlineLevel?.Val?.Value is { } outline ? outline + 1 : null);

        if (headingLevel is >= 1 and <= 6)
        {
            builder.AppendLine();
            builder.AppendLine($"{new string('#', headingLevel.Value)} {text}");
            return;
        }

        var isListItem = properties?.NumberingProperties is not null
            || IsListStyle(properties?.ParagraphStyleId?.Val?.Value);
        builder.AppendLine(isListItem ? $"- {text}" : text);
    }

    private static int? ResolveHeadingLevel(string? styleId, Styles? styles)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return null;
        }

        // O styleId varia com o idioma do Word ("Heading1", "Ttulo1", "Titre1"...) — o nome do estilo
        // ("heading 1") é estável, por isso é consultado primeiro na tabela de estilos do documento.
        var styleName = styles?.Elements<Style>()
            .FirstOrDefault(style => string.Equals(style.StyleId?.Value, styleId, StringComparison.OrdinalIgnoreCase))
            ?.StyleName?.Val?.Value;

        foreach (var candidate in new[] { styleName, styleId })
        {
            if (candidate is not null && HeadingStyleRegex().Match(candidate) is { Success: true } match)
            {
                return int.Parse(match.Groups["level"].Value);
            }
        }

        return null;
    }

    private static bool IsListStyle(string? styleId) =>
        styleId is not null && (styleId.Contains("List", StringComparison.OrdinalIgnoreCase)
            || styleId.Contains("Lista", StringComparison.OrdinalIgnoreCase));

    private static string ExtractPdf(string filePath)
    {
        using var pdf = PdfDocument.Open(filePath);
        var builder = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            builder.AppendLine(UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page));
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^(?:heading|t[íi]tulo|ttulo|titre|berschrift|überschrift)\s*(?<level>[1-6])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingStyleRegex();
}

public interface IDocumentTextExtractor
{
    Task<ExtractedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}

public enum DocumentFormatKind
{
    Markdown,
    PlainText,
    Json
}

public sealed record ExtractedDocument(string FileName, string Text, DocumentFormatKind Format);
