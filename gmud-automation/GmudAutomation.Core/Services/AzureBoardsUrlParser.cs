namespace GmudAutomation.Core.Services;

/// <summary>
/// Reconhece URLs de Work Item do Azure Boards no formato
/// https://dev.azure.com/{organizacao}/{projeto}/_workitems/edit/{id}, extraindo a organização
/// para decidir se o processo real da GMUD (organização "contoso") deve ser executado.
/// </summary>
public sealed class AzureBoardsUrlParser : IAzureBoardsUrlParser
{
    public bool TryParse(string url, out AzureBoardsWorkItemReference reference)
    {
        reference = null!;

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var editIndex = Array.IndexOf(segments, "edit");
        if (editIndex <= 0 || editIndex + 1 >= segments.Length)
        {
            return false;
        }

        if (!int.TryParse(segments[editIndex + 1], out var workItemId))
        {
            return false;
        }

        // Segmentos esperados: [organização, projeto, "_workitems", "edit", id]
        if (editIndex < 3)
        {
            return false;
        }

        var organization = Uri.UnescapeDataString(segments[0]);
        var project = Uri.UnescapeDataString(segments[1]);

        reference = new AzureBoardsWorkItemReference(organization, project, workItemId);
        return true;
    }
}
