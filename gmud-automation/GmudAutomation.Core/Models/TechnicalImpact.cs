namespace GmudAutomation.Core.Models;

/// <summary>Impacto técnico identificado para um projeto: a branch de origem esperada e se ela existe no repositório.</summary>
public sealed record TechnicalImpact(
    string ProjectName,
    string BranchName,
    bool BranchExists);
