using BacklogAutomation.Core.Ai;
using BacklogAutomation.Core.Documents;
using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Parsing;

namespace BacklogAutomation.Core.Services;

public enum ParserMode
{
    /// <summary>JSON → leitura direta; Markdown/Word estruturado → parser determinístico; demais → IA.</summary>
    Auto,
    Markdown,
    Ai,
    Json
}

public sealed record BacklogPlanBuildResult(BacklogPlan Plan, ParserMode StrategyUsed);

/// <summary>
/// Decide COMO transformar a entrada (documento anexado ou prompt) em <see cref="BacklogPlan"/>: prefere o
/// caminho determinístico (JSON revisado ou Markdown/Word já estruturado — reprodutível e sem custo de IA)
/// e só recorre à LLM quando o conteúdo é texto livre ou quando o usuário força <c>--parser ai</c>.
/// </summary>
public sealed class BacklogPlanBuilder
{
    private readonly IDocumentTextExtractor _documentTextExtractor;
    private readonly MarkdownBacklogParser _markdownParser;
    private readonly IAiBacklogGenerator? _aiGenerator;

    public BacklogPlanBuilder(IDocumentTextExtractor documentTextExtractor, MarkdownBacklogParser markdownParser, IAiBacklogGenerator? aiGenerator)
    {
        _documentTextExtractor = documentTextExtractor ?? throw new ArgumentNullException(nameof(documentTextExtractor));
        _markdownParser = markdownParser ?? throw new ArgumentNullException(nameof(markdownParser));
        _aiGenerator = aiGenerator;
    }

    public async Task<BacklogPlanBuildResult> FromFileAsync(
        string filePath, ParserMode mode, AiGenerationOptions aiOptions, CancellationToken cancellationToken = default)
    {
        var document = await _documentTextExtractor.ExtractAsync(filePath, cancellationToken);

        var strategy = mode switch
        {
            ParserMode.Auto when document.Format == DocumentFormatKind.Json => ParserMode.Json,
            ParserMode.Auto when MarkdownBacklogParser.LooksStructured(document.Text) => ParserMode.Markdown,
            ParserMode.Auto => ParserMode.Ai,
            _ => mode
        };

        var plan = strategy switch
        {
            ParserMode.Json => BacklogJson.Deserialize(document.Text),
            ParserMode.Markdown => _markdownParser.Parse(document.Text),
            _ => await GenerateWithAiAsync(document.Text, aiOptions with { SourceName = document.FileName }, cancellationToken)
        };

        return new BacklogPlanBuildResult(plan, strategy);
    }

    public async Task<BacklogPlanBuildResult> FromPromptAsync(
        string prompt, ParserMode mode, AiGenerationOptions aiOptions, CancellationToken cancellationToken = default)
    {
        // Um prompt colado já no formato estruturado ("Epic: ... / Feature: ... / US: ...") não precisa de IA.
        if (mode == ParserMode.Markdown || (mode == ParserMode.Auto && MarkdownBacklogParser.LooksStructured(prompt)))
        {
            return new BacklogPlanBuildResult(_markdownParser.Parse(prompt), ParserMode.Markdown);
        }

        if (mode == ParserMode.Json)
        {
            return new BacklogPlanBuildResult(BacklogJson.Deserialize(prompt), ParserMode.Json);
        }

        var plan = await GenerateWithAiAsync(prompt, aiOptions with { SourceName = null }, cancellationToken);
        return new BacklogPlanBuildResult(plan, ParserMode.Ai);
    }

    private Task<BacklogPlan> GenerateWithAiAsync(string text, AiGenerationOptions aiOptions, CancellationToken cancellationToken)
    {
        if (_aiGenerator is null)
        {
            throw new InvalidOperationException(
                "A entrada não está estruturada (Epic:/Feature:/User Story:) e a IA não está configurada. "
                + "Defina LLM_API_KEY (ou GROQ_API_KEY) para gerar o backlog com IA, ou estruture o documento conforme o README.");
        }

        return _aiGenerator.GenerateAsync(text, aiOptions, cancellationToken);
    }
}
