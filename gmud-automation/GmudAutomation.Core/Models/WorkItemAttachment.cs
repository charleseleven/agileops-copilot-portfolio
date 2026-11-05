namespace GmudAutomation.Core.Models;

/// <summary>Referência a um anexo de Work Item no Azure Boards, antes de ser renomeado.</summary>
public sealed record WorkItemAttachment(string Url, string OriginalFileName);
