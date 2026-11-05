namespace GmudAutomation.Core.Pipeline;

/// <summary>Chaves das etapas padrão, na ordem da especificação (1 a 11).</summary>
public static class GmudStepKeys
{
    public const string ResolveGmud = "resolver-gmud";
    public const string MapDescriptionItems = "mapear-itens-descricao";
    public const string MapRelatedItems = "mapear-tasks-bugs";
    public const string MapProjectsAndScripts = "mapear-projetos-scripts";
    public const string StoreInformation = "armazenar-informacoes";
    public const string FetchCurrentTags = "buscar-tag-vigente";
    public const string CreateGmudBranch = "criar-branch-gmud";
    public const string MergeUserStories = "merge-user-stories";
    public const string ReconcileConflicts = "corrigir-conflitos";
    public const string CreateReleaseBranches = "criar-releases";
    public const string TrackBuilds = "acompanhar-build";

    public static readonly IReadOnlyList<string> Default =
    [
        ResolveGmud, MapDescriptionItems, MapRelatedItems, MapProjectsAndScripts, StoreInformation, FetchCurrentTags,
        CreateGmudBranch, MergeUserStories, ReconcileConflicts, CreateReleaseBranches, TrackBuilds
    ];
}

/// <summary>
/// Monta o pipeline a partir das chaves configuradas: uma nova etapa é só mais uma implementação de
/// <see cref="IGmudStep"/> registrada aqui — as demais não mudam.
/// </summary>
public sealed class GmudStepRegistry
{
    private readonly Dictionary<string, IGmudStep> _steps = new(StringComparer.OrdinalIgnoreCase);

    public GmudStepRegistry(IEnumerable<IGmudStep> steps)
    {
        foreach (var step in steps)
        {
            if (!_steps.TryAdd(step.Key, step))
            {
                throw new InvalidOperationException($"Etapa \"{step.Key}\" registrada mais de uma vez.");
            }
        }
    }

    public IReadOnlyList<IGmudStep> Resolve(IReadOnlyList<string> keys)
    {
        var unknown = keys.Where(k => !_steps.ContainsKey(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                $"Etapa(s) desconhecida(s) em \"steps\" do gmud.config.json: {string.Join(", ", unknown)}. Disponíveis: {string.Join(", ", _steps.Keys)}.");
        }

        return keys.Select(k => _steps[k]).ToList();
    }
}
