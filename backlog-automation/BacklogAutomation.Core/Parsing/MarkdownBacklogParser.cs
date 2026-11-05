using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Parsing;

/// <summary>
/// Parser determinístico (sem IA) para documentos que já trazem o backlog estruturado — Markdown ou Word
/// com títulos (convertido para Markdown pelo <c>DocumentTextExtractor</c>). O tipo do item vem da
/// palavra-chave no título, não do nível do heading, para tolerar documentos "imperfeitos":
/// <code>
/// # Epic: Gestão de Pedidos
/// ## Feature: Carrinho de compras
/// ### User Story: Adicionar item ao carrinho (5 SP)
/// Como cliente, quero adicionar itens ao carrinho, para que eu possa comprá-los depois.
/// **Critérios de aceite:**
/// - Dado um produto em estoque, quando clico em "Adicionar", então o carrinho exibe o item
/// - Task: Criar endpoint POST /cart (4h)
/// - Bug: Total não atualiza ao remover item (2h)
/// </code>
/// </summary>
public sealed partial class MarkdownBacklogParser
{
    private readonly EstimateParser _estimateParser;

    public MarkdownBacklogParser(EstimateParser estimateParser)
    {
        _estimateParser = estimateParser ?? throw new ArgumentNullException(nameof(estimateParser));
    }

    /// <summary>
    /// Indica se o texto já está estruturado com palavras-chave de hierarquia (ao menos um Epic e uma User
    /// Story) — nesse caso o parser determinístico é preferível à IA (resultado reprodutível e sem custo).
    /// </summary>
    public static bool LooksStructured(string text)
    {
        var hasEpic = false;
        var hasStory = false;
        foreach (var line in SplitLines(text))
        {
            if (TryMatchItem(line.Trim(), out var kind, out _))
            {
                hasEpic |= kind == WorkItemKind.Epic;
                hasStory |= kind == WorkItemKind.UserStory;
            }

            if (hasEpic && hasStory)
            {
                return true;
            }
        }

        return false;
    }

    public BacklogPlan Parse(string text)
    {
        var plan = new BacklogPlan();
        var state = new ParserState();
        var lines = SplitLines(text);

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var trimmed = lines[index].Trim();

            if (TryMatchItem(trimmed, out var kind, out var rawTitle))
            {
                state.FlushDescription();
                var item = CreateItem(kind, rawTitle);
                Attach(plan, state, item, lineNumber);
                state.Current = item;
                state.Mode = BodyMode.Description;
                state.StatementOpen = false;
                continue;
            }

            if (state.Current is null)
            {
                // Preâmbulo do documento (título, objetivo, contexto) antes do primeiro Epic — ignorado.
                continue;
            }

            HandleBodyLine(state, trimmed);
        }

        state.FlushDescription();
        return plan;
    }

    private WorkItemDraft CreateItem(WorkItemKind kind, string rawTitle)
    {
        WorkItemDraft item = kind switch
        {
            WorkItemKind.Epic => new EpicItem(),
            WorkItemKind.Feature => new FeatureItem(),
            WorkItemKind.UserStory => new UserStoryItem(),
            WorkItemKind.Task => new TaskItem(),
            _ => new BugItem()
        };

        var title = StripMarkup(rawTitle);
        var inline = TrailingEstimateRegex().Match(title);
        if (inline.Success && _estimateParser.TryParseInline(inline.Groups["estimate"].Value, out var estimate))
        {
            title = title[..inline.Index].TrimEnd(' ', '-', '–', '—', ':');
            item.Estimate.MergeFrom(estimate);
        }

        item.Title = title.Trim();
        return item;
    }

    private static void Attach(BacklogPlan plan, ParserState state, WorkItemDraft item, int lineNumber)
    {
        switch (item)
        {
            case EpicItem epic:
                plan.Epics.Add(epic);
                state.Epic = epic;
                state.Feature = null;
                state.Story = null;
                break;

            case FeatureItem feature:
                (state.Epic ?? throw new BacklogParseException(lineNumber,
                    $"A Feature \"{feature.Title}\" não está dentro de um Epic. Declare um \"Epic:\" antes dela."))
                    .Features.Add(feature);
                state.Feature = feature;
                state.Story = null;
                break;

            case UserStoryItem story:
                (state.Feature ?? throw new BacklogParseException(lineNumber,
                    $"A User Story \"{story.Title}\" não está dentro de uma Feature. Declare uma \"Feature:\" antes dela."))
                    .UserStories.Add(story);
                state.Story = story;
                break;

            case TaskItem task:
                (state.Story ?? throw new BacklogParseException(lineNumber,
                    $"A Task \"{task.Title}\" não está dentro de uma User Story. Declare uma \"User Story:\" antes dela."))
                    .Tasks.Add(task);
                break;

            case BugItem bug:
                (state.Story ?? throw new BacklogParseException(lineNumber,
                    $"O Bug \"{bug.Title}\" não está dentro de uma User Story. Declare uma \"User Story:\" antes dele."))
                    .Bugs.Add(bug);
                break;
        }
    }

    private void HandleBodyLine(ParserState state, string trimmed)
    {
        var current = state.Current!;

        if (trimmed.Length == 0)
        {
            state.StatementOpen = false;
            if (state.Mode == BodyMode.Description)
            {
                state.DescriptionLines.Add(string.Empty);
            }

            return;
        }

        if (TryMatchSectionHeader(trimmed, out var sectionLabel, out var isMarkdownHeading))
        {
            var sectionMode = ResolveSection(Normalize(sectionLabel));
            if (sectionMode is not null)
            {
                state.Mode = sectionMode.Value;
                state.StatementOpen = false;
                return;
            }

            if (isMarkdownHeading)
            {
                // Sub-seção livre (ex: "#### Regras de negócio") — vira um destaque dentro da descrição.
                state.Mode = BodyMode.Description;
                state.DescriptionLines.Add($"**{sectionLabel}**");
                return;
            }
        }

        if (TryHandleLabeledLine(state, current, trimmed))
        {
            return;
        }

        var listItem = ListItemRegex().Match(trimmed);
        var content = listItem.Success ? StripMarkup(listItem.Groups["text"].Value) : StripMarkup(trimmed);

        switch (state.Mode)
        {
            case BodyMode.AcceptanceCriteria:
                AddAcceptanceCriterion(current, content, isListItem: listItem.Success);
                return;

            case BodyMode.ReproSteps when current is BugItem bug:
                bug.ReproSteps.Add(content);
                return;

            case BodyMode.Estimates:
                current.Estimate.MergeFrom(_estimateParser.Parse(content));
                return;
        }

        if (current is UserStoryItem story && TryHandleStatement(state, story, content))
        {
            return;
        }

        state.DescriptionLines.Add(trimmed);
    }

    private bool TryHandleLabeledLine(ParserState state, WorkItemDraft current, string trimmed)
    {
        var match = LabeledLineRegex().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var label = match.Groups["label"].Value.Trim();
        var value = StripMarkup(match.Groups["value"].Value);
        var normalized = Normalize(label);

        if (TagsLabelRegex().IsMatch(normalized))
        {
            current.Tags.AddRange(value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(tag => tag.TrimStart('#'))
                .Where(tag => !current.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
            return true;
        }

        if (PriorityLabelRegex().IsMatch(normalized))
        {
            current.Priority = ParsePriority(value) ?? current.Priority;
            return true;
        }

        if (ActivityLabelRegex().IsMatch(normalized) && current is TaskItem task)
        {
            task.Activity = value;
            return true;
        }

        if (current is BugItem bug)
        {
            if (ExpectedResultLabelRegex().IsMatch(normalized))
            {
                bug.ExpectedResult = value;
                return true;
            }

            if (ActualResultLabelRegex().IsMatch(normalized))
            {
                bug.ActualResult = value;
                return true;
            }
        }

        if (ResolveSection(normalized) is { } inlineSection)
        {
            // "Critérios de aceite: Dado X, quando Y, então Z" — seção com o primeiro valor na mesma linha.
            state.Mode = inlineSection;
            switch (inlineSection)
            {
                case BodyMode.AcceptanceCriteria:
                    AddAcceptanceCriterion(current, value, isListItem: true);
                    break;
                case BodyMode.ReproSteps when current is BugItem bugItem:
                    bugItem.ReproSteps.Add(value);
                    break;
                case BodyMode.Estimates:
                    current.Estimate.MergeFrom(_estimateParser.Parse(value));
                    break;
            }

            return true;
        }

        if (state.Mode == BodyMode.Estimates || EstimateLabelRegex().IsMatch(normalized))
        {
            current.Estimate.MergeFrom(_estimateParser.Parse($"{label}: {value}"));
            return true;
        }

        return false;
    }

    private static bool TryHandleStatement(ParserState state, UserStoryItem story, string content)
    {
        if (StatementStartRegex().IsMatch(content) && story.Statement is null)
        {
            story.Statement = content;
            state.StatementOpen = true;
            return true;
        }

        if (state.StatementOpen && story.Statement is not null && StatementContinuationRegex().IsMatch(content))
        {
            story.Statement = $"{story.Statement.TrimEnd(',', ' ')}, {char.ToLowerInvariant(content[0])}{content[1..]}";
            return true;
        }

        state.StatementOpen = false;
        return false;
    }

    private static void AddAcceptanceCriterion(WorkItemDraft current, string content, bool isListItem)
    {
        var criteria = current switch
        {
            UserStoryItem story => story.AcceptanceCriteria,
            FeatureItem feature => feature.AcceptanceCriteria,
            _ => null
        };

        if (criteria is null)
        {
            return;
        }

        // Gherkin sem marcador de lista: "Cenário:" abre um novo critério; "Dado/Quando/Então/E" continuam o anterior.
        if (!isListItem && criteria.Count > 0 && GherkinContinuationRegex().IsMatch(content))
        {
            criteria[^1] = $"{criteria[^1]}\n{content}";
            return;
        }

        criteria.Add(content);
    }

    private static BodyMode? ResolveSection(string normalizedLabel)
    {
        if (AcceptanceCriteriaLabelRegex().IsMatch(normalizedLabel))
        {
            return BodyMode.AcceptanceCriteria;
        }

        if (ReproStepsLabelRegex().IsMatch(normalizedLabel))
        {
            return BodyMode.ReproSteps;
        }

        if (EstimatesSectionLabelRegex().IsMatch(normalizedLabel))
        {
            return BodyMode.Estimates;
        }

        if (DescriptionLabelRegex().IsMatch(normalizedLabel))
        {
            return BodyMode.Description;
        }

        return null;
    }

    private static bool TryMatchSectionHeader(string trimmed, out string label, out bool isMarkdownHeading)
    {
        var match = SectionHeaderRegex().Match(trimmed);
        isMarkdownHeading = match.Success && match.Groups["hashes"].Success;
        label = match.Success ? match.Groups["label"].Value.Trim() : string.Empty;
        return match.Success;
    }

    internal static bool TryMatchItem(string trimmed, out WorkItemKind kind, out string title)
    {
        kind = default;
        title = string.Empty;

        var heading = HeadingRegex().Match(trimmed);
        if (heading.Success)
        {
            var item = ItemKeywordRegex().Match(StripMarkup(heading.Groups["text"].Value));
            if (item.Success)
            {
                kind = ResolveKind(item.Groups["keyword"].Value);
                title = item.Groups["title"].Value;
                return title.Trim().Length > 0;
            }

            return false;
        }

        var listItem = ListItemKeywordRegex().Match(trimmed);
        if (listItem.Success)
        {
            kind = ResolveKind(listItem.Groups["keyword"].Value);
            title = listItem.Groups["title"].Value;
            return title.Trim().Length > 0;
        }

        return false;
    }

    private static WorkItemKind ResolveKind(string keyword) => Normalize(keyword) switch
    {
        "epic" or "epico" => WorkItemKind.Epic,
        "feature" or "funcionalidade" => WorkItemKind.Feature,
        "task" or "tarefa" => WorkItemKind.Task,
        "bug" or "defeito" => WorkItemKind.Bug,
        _ => WorkItemKind.UserStory
    };

    private static int? ParsePriority(string value)
    {
        if (EstimateParser.ParseNumber(value) is { } number && number is >= 1 and <= 4)
        {
            return (int)number;
        }

        return Normalize(value) switch
        {
            var v when v.StartsWith("critic", StringComparison.Ordinal) || v.StartsWith("highest", StringComparison.Ordinal)
                || v.StartsWith("urgente", StringComparison.Ordinal) || v.StartsWith("must", StringComparison.Ordinal) => 1,
            var v when v.StartsWith("alta", StringComparison.Ordinal) || v.StartsWith("high", StringComparison.Ordinal)
                || v.StartsWith("should", StringComparison.Ordinal) => 2,
            var v when v.StartsWith("media", StringComparison.Ordinal) || v.StartsWith("medium", StringComparison.Ordinal)
                || v.StartsWith("normal", StringComparison.Ordinal) || v.StartsWith("could", StringComparison.Ordinal) => 3,
            var v when v.StartsWith("baixa", StringComparison.Ordinal) || v.StartsWith("low", StringComparison.Ordinal)
                || v.StartsWith("won", StringComparison.Ordinal) => 4,
            _ => null
        };
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string StripMarkup(string text) => BoldMarkerRegex().Replace(text, string.Empty).Trim();

    private static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return Regex.Replace(new string(chars), @"\s+", " ");
    }

    private enum BodyMode
    {
        Description,
        AcceptanceCriteria,
        ReproSteps,
        Estimates
    }

    private sealed class ParserState
    {
        public EpicItem? Epic { get; set; }
        public FeatureItem? Feature { get; set; }
        public UserStoryItem? Story { get; set; }
        public WorkItemDraft? Current { get; set; }
        public BodyMode Mode { get; set; }
        public bool StatementOpen { get; set; }
        public List<string> DescriptionLines { get; } = [];

        public void FlushDescription()
        {
            if (Current is not null)
            {
                var description = string.Join('\n', DescriptionLines).Trim('\n', ' ');
                if (description.Length > 0)
                {
                    Current.Description = string.IsNullOrWhiteSpace(Current.Description)
                        ? description
                        : $"{Current.Description}\n\n{description}";
                }
            }

            DescriptionLines.Clear();
        }
    }

    [GeneratedRegex(@"^#{1,6}\s+(?<text>.+?)\s*#*$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(
        @"^(?<keyword>epic|[ée]pico|feature|funcionalidade|user\s*story|us|hist[óo]ria(?:\s+de\s+usu[áa]rio)?|pbi|product\s+backlog\s+item|task|tarefa|bug|defeito)(?:\s*#?\d+(?:\.\d+)*)?\s*[:\-–—.]\s*(?<title>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ItemKeywordRegex();

    [GeneratedRegex(
        @"^(?:[-*+]|\d+[.)])\s+(?:\[\s*\]\s+)?(?:\*\*)?(?:\[\s*(?<keyword>task|tarefa|bug|defeito)\s*\]\s*[:\-–—]?\s*|(?<keyword>task|tarefa|bug|defeito)(?:\s*#?\d+(?:\.\d+)*)?\s*(?:\*\*)?\s*[:\-–—]\s*)(?:\*\*)?\s*(?<title>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ListItemKeywordRegex();

    [GeneratedRegex(@"^(?:[-*+]|\d+[.)])\s+(?:\[[ xX]?\]\s+)?(?<text>.+)$")]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"^(?:(?<hashes>#{1,6})\s+)?(?:\*\*|__)?(?<label>[^:*#]{2,60}?)(?:\*\*|__)?\s*:?\s*(?:\*\*|__)?\s*:?\s*$")]
    private static partial Regex SectionHeaderRegex();

    [GeneratedRegex(@"^(?:[-*+]\s+)?(?:\*\*|__)?(?<label>[^:*]{2,40}?)(?:\*\*|__)?\s*:\s*(?:\*\*|__)?\s*(?<value>\S.*)$")]
    private static partial Regex LabeledLineRegex();

    [GeneratedRegex(@"\s*(?:[\(\[](?<estimate>[^\)\]]+)[\)\]]|\s[—–-]\s+(?<estimate>[^—–\-\(\)\[\]]+))\s*$")]
    private static partial Regex TrailingEstimateRegex();

    [GeneratedRegex(@"\*\*|__|`")]
    private static partial Regex BoldMarkerRegex();

    [GeneratedRegex(@"^(criterios? de aceit\w*|acceptance criteria|ac|definicao de pronto|dod)$")]
    private static partial Regex AcceptanceCriteriaLabelRegex();

    [GeneratedRegex(@"^(passos( para reproduzir| de reproducao)?|steps( to reproduce)?|repro steps|como reproduzir)$")]
    private static partial Regex ReproStepsLabelRegex();

    [GeneratedRegex(@"^(estimativas?|estimates?|estimation|estimacao|medidas?)$")]
    private static partial Regex EstimatesSectionLabelRegex();

    [GeneratedRegex(@"^(descricao|description|detalhes|details|contexto|context)$")]
    private static partial Regex DescriptionLabelRegex();

    [GeneratedRegex(@"^(story points?|sp|pontos?|pontuacao|pts|esforco|effort|horas?( estimadas?| restantes?| concluidas?)?|hours?|restantes?|remaining( work)?|completed( work)?|concluid[oa]s?|estimativa original|original estimate|tamanho|size|t-?shirt( size)?|duracao|duration|valor de negocio|business value|complexidade|complexity|pontos de funcao|function points|wsjf|criticidade|time criticality)$")]
    private static partial Regex EstimateLabelRegex();

    [GeneratedRegex(@"^(tags?|etiquetas?|labels?|rotulos?)$")]
    private static partial Regex TagsLabelRegex();

    [GeneratedRegex(@"^(prioridade|priority)$")]
    private static partial Regex PriorityLabelRegex();

    [GeneratedRegex(@"^(atividade|activity|disciplina)$")]
    private static partial Regex ActivityLabelRegex();

    [GeneratedRegex(@"^(resultado esperado|comportamento esperado|expected( result| behavior)?)$")]
    private static partial Regex ExpectedResultLabelRegex();

    [GeneratedRegex(@"^(resultado (atual|obtido)|comportamento atual|actual( result| behavior)?)$")]
    private static partial Regex ActualResultLabelRegex();

    [GeneratedRegex(@"^(como|eu como|enquanto|as an?)\s", RegexOptions.IgnoreCase)]
    private static partial Regex StatementStartRegex();

    [GeneratedRegex(@"^(eu quero|quero|desejo|preciso|i want|para que|para|a fim de|de modo que|so that|in order to)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StatementContinuationRegex();

    [GeneratedRegex(@"^(dado|quando|ent[ãa]o|e|mas|given|when|then|and|but)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GherkinContinuationRegex();
}

public sealed class BacklogParseException(int lineNumber, string message)
    : Exception($"Linha {lineNumber}: {message}")
{
    public int LineNumber { get; } = lineNumber;
}
