namespace BacklogAutomation.Core.Models;

/// <summary>
/// Pedido de criação de UM card, já desacoplado do plano: o provider recebe tudo que precisa (incluindo
/// o card pai já criado e o contexto de Epic/Feature, usado por ferramentas sem hierarquia nativa de 4
/// níveis — ex: Jira sem "Initiative" ou Trello — para rotular os cards).
/// </summary>
public sealed record WorkItemCreationRequest
{
    public required WorkItemKind Kind { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? Statement { get; init; }

    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = [];

    public IReadOnlyList<string> ReproSteps { get; init; } = [];

    public string? ExpectedResult { get; init; }

    public string? ActualResult { get; init; }

    public string? Activity { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    public int? Priority { get; init; }

    public Estimate Estimate { get; init; } = new();

    public CreatedWorkItem? Parent { get; init; }

    public string? EpicTitle { get; init; }

    public string? FeatureTitle { get; init; }
}

/// <summary>Card criado na ferramenta de destino (ou representação equivalente, ex: item de checklist no Trello).</summary>
public sealed record CreatedWorkItem(
    WorkItemKind Kind,
    string Title,
    string? Id,
    string? Url,
    string? ProviderType,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Verdadeiro quando o nível não existe como card na ferramenta (ex: Epic num Jira sem tipo acima de
    /// Epic) e foi representado de outra forma (label) — filhos não devem tentar se vincular a ele.
    /// </summary>
    public bool IsVirtual => Id is null;
}

public sealed record BacklogCreationResult(IReadOnlyList<CreatedWorkItem> CreatedItems);
