namespace GmudAutomation.Core.Models;

/// <summary>Comentário registrado na aba de discussão de um Work Item do Azure Boards.</summary>
public sealed record WorkItemComment(
    int Id,
    string Text,
    string CreatedBy,
    DateTimeOffset CreatedDate);
