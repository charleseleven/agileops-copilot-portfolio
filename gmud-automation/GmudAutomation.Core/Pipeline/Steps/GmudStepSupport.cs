using System.Globalization;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>Consultas ao manifesto e à configuração repetidas entre as etapas.</summary>
internal static class GmudStepSupport
{
    public static RepositoryConfig GetRepository(GmudConfig config, string repositoryName) =>
        config.Repositories.FirstOrDefault(r => string.Equals(r.Name, repositoryName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"O repositório \"{repositoryName}\" está no manifesto mas não no gmud.config.json.");

    public static GmudClient GetClient(GmudManifest manifest) => new(
        manifest.Gmud.ClientTitleName ?? manifest.Gmud.Client,
        manifest.Gmud.Client,
        manifest.Gmud.ClientAppServiceName ?? manifest.Gmud.Client.ToLowerInvariant(),
        IsConfigured: true);

    public static DateOnly GetDate(GmudManifest manifest) =>
        DateOnly.TryParseExact(manifest.Gmud.Date, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new InvalidOperationException($"Data \"{manifest.Gmud.Date}\" inválida no manifesto — rode novamente a etapa 1.");

    public static void RequireResolvedGmud(GmudManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Gmud.Title) || string.IsNullOrWhiteSpace(manifest.Gmud.Client))
        {
            throw new InvalidOperationException("O manifesto ainda não tem os dados da GMUD — rode a etapa 1 (--from-step 1).");
        }
    }

    /// <summary>User stories que alteram o repositório, na ordem da Descrição (ordem dos merges).</summary>
    public static IReadOnlyList<UserStoryEntry> UserStoriesAffecting(GmudManifest manifest, string repositoryName) =>
        manifest.UserStories
            .Where(us => us.Items.Any(i => i.Projects.Contains(repositoryName, StringComparer.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>IDs que, citados numa mensagem de commit, identificam a user story (ela e seus itens).</summary>
    public static IReadOnlyCollection<int> WorkItemIds(UserStoryEntry userStory) =>
        userStory.Items.Select(i => i.Id).Append(userStory.Id).Distinct().ToList();

    public static IReadOnlySet<string> LinkedCommits(UserStoryEntry userStory, string repositoryName) =>
        userStory.Items
            .SelectMany(i => i.Commits)
            .Where(c => string.Equals(c.Repository, repositoryName, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.CommitId.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>O repositório foi associado à user story só por anexo de banco (não há código a mesclar).</summary>
    public static bool DetectedOnlyByAttachment(UserStoryEntry userStory, string repositoryName) =>
        userStory.Items
            .Where(i => i.ProjectSources.ContainsKey(repositoryName))
            .SelectMany(i => i.ProjectSources[repositoryName])
            .All(source => source == ProjectSource.Attachment);

    public static string Short(string? commitId) => commitId is null ? "?" : commitId[..Math.Min(8, commitId.Length)];

    public static string LocalScriptPath(GmudStepContext context, string displayPath) =>
        Path.Combine(context.GmudDirectory, "scripts", Path.GetFileName(displayPath));
}

/// <summary>Origem da associação item → repositório (etapa 4).</summary>
public static class ProjectSource
{
    public const string Text = "texto";
    public const string Link = "link";
    public const string Attachment = "anexo";
}
