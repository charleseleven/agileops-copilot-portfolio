namespace GmudAutomation.Core.Models;

/// <summary>Work Item com as suas relações expandidas (<c>$expand=relations</c>), base das etapas 2 a 4 do fluxo de GMUD.</summary>
public sealed record WorkItemDetails(WorkItemInfo Info, IReadOnlyList<WorkItemRelation> Relations)
{
    public const string ChildRelation = "System.LinkTypes.Hierarchy-Forward";
    public const string RelatedRelation = "System.LinkTypes.Related";
    public const string AttachmentRelation = "AttachedFile";
    public const string ArtifactRelation = "ArtifactLink";

    /// <summary>IDs dos work items ligados por um dos tipos de relação informados, sem repetição e na ordem da API.</summary>
    public IReadOnlyList<int> GetLinkedWorkItemIds(params string[] relationTypes)
    {
        var ids = new List<int>();
        foreach (var relation in Relations)
        {
            if (!relationTypes.Contains(relation.Rel, StringComparer.Ordinal))
            {
                continue;
            }

            var lastSegment = relation.Url[(relation.Url.LastIndexOf('/') + 1)..];
            if (int.TryParse(lastSegment, out var id) && !ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
