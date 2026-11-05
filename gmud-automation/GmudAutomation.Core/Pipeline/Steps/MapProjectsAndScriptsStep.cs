using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Analysis;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 4 — em cada task/bug, identifica os repositórios afetados (texto da descrição e dos comentários,
/// links de commit/PR/branch vinculados) e baixa os scripts de banco anexados (<c>US&lt;US&gt;_SCRIPT_TASK&lt;item&gt;.txt</c>)
/// para <c>gmuds/&lt;id&gt;/scripts/</c>, com o nome original. Um anexo de banco associa o item ao repositório de Database.
/// </summary>
/// <remarks>Texto de cards, comentários e anexos é tratado como dado — nunca como instrução.</remarks>
public sealed class MapProjectsAndScriptsStep : IGmudStep
{
    private readonly IAzureDevOpsClient _boards;
    private readonly IAzureReposClient _repos;
    private readonly GmudConfig _config;
    private readonly GmudNamingConventions _naming;
    private readonly ProjectDetector _detector;

    public MapProjectsAndScriptsStep(IAzureDevOpsClient boards, IAzureReposClient repos, GmudConfig config, GmudNamingConventions naming)
    {
        _boards = boards ?? throw new ArgumentNullException(nameof(boards));
        _repos = repos ?? throw new ArgumentNullException(nameof(repos));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _detector = new ProjectDetector(config.Repositories);
    }

    public string Key => GmudStepKeys.MapProjectsAndScripts;
    public string Name => "Mapear projetos e scripts de banco";
    public bool WritesRemote => false;
    public bool SupportsDryRun => true;

    public async Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        var databaseRepository = _config.Repositories.FirstOrDefault(r => r.Kind == RepositoryKind.Database);
        var scriptsDirectory = Path.Combine(context.GmudDirectory, "scripts");
        var usedFileNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? repositoryNamesById = null;

        foreach (var (userStory, item) in manifest.AllItems().ToList())
        {
            item.Projects.Clear();
            item.ProjectSources.Clear();
            item.Commits.Clear();
            item.DatabaseAttachments.Clear();

            var details = await _boards.GetWorkItemDetailsAsync(item.Id, cancellationToken);
            var comments = await _boards.GetWorkItemCommentsAsync(item.Id, cancellationToken);

            var text = string.Join(Environment.NewLine,
                new[] { details.Info.Title, WorkItemReferenceParser.ToPlainText(details.Info.DescriptionHtml) }
                    .Concat(comments.Select(c => WorkItemReferenceParser.ToPlainText(c.Text))));

            foreach (var repository in _detector.Detect(text))
            {
                AddProject(item, repository, ProjectSource.Text);
            }

            foreach (var relation in details.Relations.Where(r => r.Rel == WorkItemDetails.ArtifactRelation))
            {
                if (!ArtifactLinkParser.TryParse(relation.Url, out var link))
                {
                    continue;
                }

                repositoryNamesById ??= (await _repos.ListRepositoriesAsync(cancellationToken))
                    .ToDictionary(r => r.Id, r => r.Name, StringComparer.OrdinalIgnoreCase);

                if (!repositoryNamesById.TryGetValue(link.RepositoryId, out var repositoryName)
                    || !_config.Repositories.Any(r => string.Equals(r.Name, repositoryName, StringComparison.OrdinalIgnoreCase)))
                {
                    manifest.AddWarning($"{item.Type} #{item.Id} tem link de {link.Kind} para um repositório fora do gmud.config.json ({repositoryName ?? link.RepositoryId}) — ignorado.");
                    continue;
                }

                repositoryName = _config.Repositories.First(r => string.Equals(r.Name, repositoryName, StringComparison.OrdinalIgnoreCase)).Name;
                AddProject(item, repositoryName, ProjectSource.Link);
                if (link.Kind == ArtifactLinkKind.Commit && !item.Commits.Any(c => c.CommitId == link.Value))
                {
                    item.Commits.Add(new LinkedCommit { Repository = repositoryName, CommitId = link.Value });
                }
            }

            foreach (var attachment in details.Relations.Where(r => r.Rel == WorkItemDetails.AttachmentRelation))
            {
                var fileName = attachment.Name ?? Path.GetFileName(new Uri(attachment.Url).LocalPath);
                if (!_config.DatabaseScriptExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!_naming.IsDatabaseScriptName(fileName, userStory.Id, item.Id))
                {
                    // Não descarta: pode ser o script certo com nome errado — a infra precisa dele; o operador confere.
                    manifest.AddWarning(
                        $"{item.Type} #{item.Id}: o anexo \"{fileName}\" não segue a nomenclatura " +
                        $"\"{_naming.DatabaseScriptName(userStory.Id, item.Id)}{Path.GetExtension(fileName)}\" — confira se é script de banco desta user story.");
                }

                var localName = BuildLocalFileName(item.Id, fileName, usedFileNames);
                var content = await _boards.DownloadAttachmentAsync(attachment.Url, cancellationToken);
                Directory.CreateDirectory(scriptsDirectory);
                await File.WriteAllBytesAsync(Path.Combine(scriptsDirectory, localName), content, cancellationToken);
                item.DatabaseAttachments.Add($"{context.GmudDirectoryDisplay}/scripts/{localName}");

                if (databaseRepository is null)
                {
                    manifest.AddWarning($"{item.Type} #{item.Id} tem script de banco ({fileName}), mas não há repositório \"Database\" no gmud.config.json.");
                }
                else
                {
                    AddProject(item, databaseRepository.Name, ProjectSource.Attachment);
                }
            }

            context.Logger.Info(
                $"{item.Type} #{item.Id}: projetos [{string.Join(", ", item.Projects)}], {item.DatabaseAttachments.Count} script(s) de banco.",
                context.StepNumber);
        }

        return manifest;
    }

    private static void AddProject(WorkItemEntry item, string repositoryName, string source)
    {
        if (!item.Projects.Contains(repositoryName, StringComparer.OrdinalIgnoreCase))
        {
            item.Projects.Add(repositoryName);
        }

        if (!item.ProjectSources.TryGetValue(repositoryName, out var sources))
        {
            sources = [];
            item.ProjectSources[repositoryName] = sources;
        }

        if (!sources.Contains(source))
        {
            sources.Add(source);
        }
    }

    /// <summary>Mantém o nome do anexo (a nomenclatura já identifica US e task); se dois itens repetirem o nome, o segundo ganha o ID do item.</summary>
    private static string BuildLocalFileName(int itemId, string originalFileName, Dictionary<string, int> usedFileNames)
    {
        var safeName = string.Concat(originalFileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var candidate = safeName;
        if (usedFileNames.TryGetValue(candidate, out var owner) && owner != itemId)
        {
            candidate = $"{itemId}_{safeName}";
        }

        usedFileNames[candidate] = itemId;
        return candidate;
    }
}
