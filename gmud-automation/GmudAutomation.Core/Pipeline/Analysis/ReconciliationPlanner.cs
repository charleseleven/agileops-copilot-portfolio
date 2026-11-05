using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Core.Pipeline.Analysis;

/// <summary>Decisão para um arquivo na conciliação, com a alteração a aplicar na branch da GMUD (se houver).</summary>
/// <param name="PushChangeType">"add", "edit" ou "delete" quando a versão da feature deve entrar; null quando nada é aplicado.</param>
public sealed record FilePlan(string Path, string Resolution, string Reason, string? PushChangeType);

/// <summary>
/// Etapa 9 — regra de conciliação: a base é a release de produção; da feature entram apenas as alterações
/// feitas nos commits da user story. Um arquivo só é resolvido automaticamente quando é inequívoco:
/// tocado apenas por commits da user story e intocado na produção desde que a feature saiu da base.
/// Qualquer outro caso com alteração da user story é ambíguo e vai para aprovação humana.
/// </summary>
public static class ReconciliationPlanner
{
    public static IReadOnlyList<FilePlan> Plan(
        IReadOnlySet<string> userStoryFiles,
        IReadOnlySet<string> outOfScopeFiles,
        IReadOnlySet<string> changedInProductionSinceBase,
        IReadOnlyDictionary<string, string> featureNetChanges)
    {
        var plans = new List<FilePlan>();

        foreach (var path in userStoryFiles.Order(StringComparer.Ordinal))
        {
            featureNetChanges.TryGetValue(path, out var netChange);

            if (outOfScopeFiles.Contains(path))
            {
                plans.Add(new FilePlan(path, ConflictResolution.Ambiguous,
                    "arquivo alterado pela user story e também por commits fora do escopo — a versão da feature levaria código que não pertence à user story", null));
            }
            else if (changedInProductionSinceBase.Contains(path))
            {
                plans.Add(new FilePlan(path, ConflictResolution.Ambiguous,
                    "arquivo alterado na produção depois que a feature saiu da base e também pela user story — exige merge linha a linha", null));
            }
            else if (netChange is not null && netChange.Contains("rename", StringComparison.OrdinalIgnoreCase))
            {
                plans.Add(new FilePlan(path, ConflictResolution.Ambiguous, "arquivo renomeado na feature — confirmar origem e destino", null));
            }
            else if (netChange is null)
            {
                // Alterado e revertido dentro da própria user story: não há diferença líquida a aplicar.
                continue;
            }
            else
            {
                plans.Add(new FilePlan(path, ConflictResolution.TakeFeature,
                    "alterado somente pelos commits da user story e intocado na produção", ToPushChangeType(netChange)));
            }
        }

        foreach (var path in outOfScopeFiles.Where(p => !userStoryFiles.Contains(p)).Order(StringComparer.Ordinal))
        {
            plans.Add(new FilePlan(path, ConflictResolution.Discarded, "alterado apenas por commits fora da user story — não é levado", null));
        }

        return plans;
    }

    public static string ToPushChangeType(string apiChangeType) =>
        apiChangeType.Contains("delete", StringComparison.OrdinalIgnoreCase) ? "delete"
        : apiChangeType.Contains("add", StringComparison.OrdinalIgnoreCase) ? "add"
        : "edit";
}
