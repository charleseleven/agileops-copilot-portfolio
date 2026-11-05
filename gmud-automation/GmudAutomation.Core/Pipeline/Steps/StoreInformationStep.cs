using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Steps;

/// <summary>
/// Etapa 5 — consolida o que foi mapeado: abre no manifesto uma entrada por repositório de código afetado (com a
/// branch da GMUD que será usada), separa o Database (só branches das user stories e scripts) e garante que há escopo. O runner grava o manifesto em <c>gmuds/&lt;id&gt;/</c> após cada etapa.
/// </summary>
public sealed class StoreInformationStep : IGmudStep
{
    private readonly GmudConfig _config;
    private readonly GmudNamingConventions _naming;

    public StoreInformationStep(GmudConfig config, GmudNamingConventions naming)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
    }

    public string Key => GmudStepKeys.StoreInformation;
    public string Name => "Armazenar as informações";
    public bool WritesRemote => false;
    public bool SupportsDryRun => true;

    public Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
    {
        // Database não entra no fluxo de branches: em toda user story procura-se a feature/us_<US> do desenvolvedor
        // (etapa 6) — a alteração no banco é detectada pela branch com commit, não só pelo texto do card.
        var database = _config.Repositories.FirstOrDefault(r => r.Kind == RepositoryKind.Database);
        manifest.Database = database is null ? null : BuildDatabaseEntry(manifest, database.Name);

        var affected = _config.Repositories
            .Where(r => r.Kind != RepositoryKind.Database)
            .Where(r => manifest.AllItems().Any(x => x.Item.Projects.Contains(r.Name, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (affected.Count == 0 && database is null)
        {
            throw new GmudBlockedException(
                "Nenhum repositório foi identificado nas tasks/bugs. Cite o repositório (nome ou alias do gmud.config.json) no card " +
                $"ou vincule o commit/branch ao card, e rode --from-step {context.StepNumber - 1}.");
        }

        foreach (var stale in manifest.Repositories.Keys.Where(k => !affected.Any(r => string.Equals(r.Name, k, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            if (manifest.Repositories[stale].GmudCommit is null)
            {
                manifest.Repositories.Remove(stale);
            }
            else
            {
                manifest.AddWarning($"[{stale}] deixou de ser citado nos cards, mas já tem branch da GMUD — mantido no manifesto para revisão.");
            }
        }

        foreach (var repository in affected)
        {
            if (!manifest.Repositories.TryGetValue(repository.Name, out var entry))
            {
                entry = new RepositoryEntry();
                manifest.Repositories[repository.Name] = entry;
            }

            entry.Kind = repository.Kind.ToString();
            entry.GmudBranch = _naming.GmudBranch(manifest.Gmud.Id);
        }

        var scripts = manifest.AllItems().Sum(x => x.Item.DatabaseAttachments.Count);
        context.Logger.Info(
            $"Repositórios de código afetados: {(affected.Count == 0 ? "nenhum" : string.Join(", ", affected.Select(r => r.Name)))}" +
            $"{(database is null ? "" : $"; {database.Name}: verificar {manifest.Database!.Branches.Count} branch(es) de user story")}" +
            $"; {scripts} script(s) de banco em {context.GmudDirectoryDisplay}/scripts.",
            context.StepNumber);
        return Task.FromResult(manifest);
    }

    private DatabaseEntry BuildDatabaseEntry(GmudManifest manifest, string repositoryName) => new()
    {
        Repository = repositoryName,
        Branches = manifest.UserStories
            .Select(us =>
            {
                var items = us.Items.Where(i => i.ProjectSources.ContainsKey(repositoryName)).ToList();
                return new DatabaseBranchEntry
                {
                    UserStoryId = us.Id,
                    Branch = us.Branch ?? _naming.ItemBranch(us.Id),
                    Required = items.Any(i => i.ProjectSources[repositoryName].Any(s => s != ProjectSource.Attachment)),
                    HasScripts = us.Items.Any(i => i.DatabaseAttachments.Count > 0)
                };
            })
            .ToList()
    };
}
