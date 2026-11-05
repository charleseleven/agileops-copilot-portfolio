namespace GmudAutomation.Core.Services;

/// <summary>Identifica nomes de projetos (ex: Contoso.PortalCliente.API) mencionados em um texto de comentário.</summary>
public interface IProjectTagExtractor
{
    IReadOnlyList<string> ExtractProjectTags(string? commentText);
}
