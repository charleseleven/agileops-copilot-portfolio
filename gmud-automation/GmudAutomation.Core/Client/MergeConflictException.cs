namespace GmudAutomation.Core.Client;

/// <summary>
/// Lançada quando o merge de branches (US 2.1) retorna conflito (HTTP 409), contendo o log
/// legível dos arquivos em conflito para resolução manual do Tech Lead.
/// </summary>
public sealed class MergeConflictException : Exception
{
    public string SourceBranch { get; }
    public string TargetBranch { get; }
    public IReadOnlyList<string> ConflictingFiles { get; }

    public MergeConflictException(string sourceBranch, string targetBranch, IReadOnlyList<string> conflictingFiles)
        : base(BuildMessage(sourceBranch, targetBranch, conflictingFiles))
    {
        SourceBranch = sourceBranch;
        TargetBranch = targetBranch;
        ConflictingFiles = conflictingFiles;
    }

    private static string BuildMessage(string sourceBranch, string targetBranch, IReadOnlyList<string> conflictingFiles)
    {
        var files = conflictingFiles.Count == 0
            ? "  (nenhum arquivo informado pela API)"
            : string.Join(Environment.NewLine, conflictingFiles.Select(f => $"  - {f}"));

        return $"Conflito ao mesclar \"{sourceBranch}\" em \"{targetBranch}\". Arquivos em conflito:{Environment.NewLine}{files}";
    }
}
