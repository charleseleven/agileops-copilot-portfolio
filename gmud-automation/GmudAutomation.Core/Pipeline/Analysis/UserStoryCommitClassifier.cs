using System.Text.RegularExpressions;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Pipeline.Analysis;

/// <summary>
/// Separa, entre os commits que a branch da user story traz, os que pertencem à user story dos que vieram de
/// outra origem (ex: a feature foi criada a partir de develop). Um commit é da user story quando está vinculado
/// a um dos cards (links de desenvolvimento) ou quando a mensagem cita a user story ou um de seus itens.
/// </summary>
public static class UserStoryCommitClassifier
{
    public static bool BelongsToUserStory(GitCommitInfo commit, IReadOnlyCollection<int> workItemIds, IReadOnlySet<string> linkedCommitIds)
    {
        if (linkedCommitIds.Contains(commit.CommitId.ToLowerInvariant()))
        {
            return true;
        }

        foreach (var id in workItemIds)
        {
            // "#20611", "AB#20611", "us_20611", "US 20611", "Task 20612", "Bug #20613", "feature/us_20611".
            var pattern = $@"(?:#|\bus[_\s-]?|\b(?:user\s*story|task|bug)\s*#?\s*){id}(?!\d)";
            if (Regex.IsMatch(commit.Comment, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Divide os commits em (da user story, fora do escopo). Commits de merge são neutros: o conteúdo que eles
    /// trazem aparece nos próprios commits mesclados, que são classificados individualmente — e um merge de
    /// "develop" na feature costuma citar "us_&lt;id&gt;" só por causa do nome da branch.
    /// </summary>
    public static (IReadOnlyList<GitCommitInfo> UserStory, IReadOnlyList<GitCommitInfo> OutOfScope) Split(
        IReadOnlyList<GitCommitInfo> commits, IReadOnlyCollection<int> workItemIds, IReadOnlySet<string> linkedCommitIds)
    {
        var userStory = new List<GitCommitInfo>();
        var outOfScope = new List<GitCommitInfo>();

        foreach (var commit in commits.Where(c => !c.IsMergeCommit))
        {
            (BelongsToUserStory(commit, workItemIds, linkedCommitIds) ? userStory : outOfScope).Add(commit);
        }

        return (userStory, outOfScope);
    }
}
