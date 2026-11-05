namespace GmudAutomation.Core.Pipeline.Analysis;

public enum ArtifactLinkKind
{
    Commit,
    PullRequest,
    Branch
}

/// <summary>Link de desenvolvimento de um card, já decodificado. O repositório vem como GUID.</summary>
public sealed record ArtifactLink(ArtifactLinkKind Kind, string ProjectId, string RepositoryId, string Value);

/// <summary>
/// Decodifica os links de desenvolvimento (<c>ArtifactLink</c>) dos cards:
/// <c>vstfs:///Git/Commit/{projeto}%2F{repo}%2F{sha}</c>, <c>vstfs:///Git/PullRequestId/{projeto}%2F{repo}%2F{id}</c> e
/// <c>vstfs:///Git/Ref/{projeto}%2F{repo}%2FGB{branch}</c>.
/// </summary>
public static class ArtifactLinkParser
{
    public static bool TryParse(string? uri, out ArtifactLink link)
    {
        link = null!;
        if (string.IsNullOrWhiteSpace(uri) || !uri.StartsWith("vstfs:///Git/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = uri["vstfs:///Git/".Length..];
        var slash = remainder.IndexOf('/');
        if (slash <= 0)
        {
            return false;
        }

        var type = remainder[..slash];
        var parts = Uri.UnescapeDataString(remainder[(slash + 1)..]).Split('/', 3);
        if (parts.Length < 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        ArtifactLinkKind? kind = type.ToLowerInvariant() switch
        {
            "commit" => ArtifactLinkKind.Commit,
            "pullrequestid" => ArtifactLinkKind.PullRequest,
            "ref" => ArtifactLinkKind.Branch,
            _ => null
        };

        if (kind is null)
        {
            return false;
        }

        var value = parts[2];
        if (kind == ArtifactLinkKind.Branch)
        {
            // "GB" = Git Branch (GT = tag, GC = commit); só branches interessam.
            if (!value.StartsWith("GB", StringComparison.Ordinal))
            {
                return false;
            }

            value = value[2..];
        }

        link = new ArtifactLink(kind.Value, parts[0], parts[1], kind == ArtifactLinkKind.Commit ? value.ToLowerInvariant() : value);
        return true;
    }
}
