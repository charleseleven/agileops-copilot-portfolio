using System.Text;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>Monta o texto do comentário de análise automática de impacto técnico (projetos + status da branch de origem).</summary>
public sealed class TechnicalImpactReportFormatter : ITechnicalImpactReportFormatter
{
    private const string Header = "Análise automática de impacto técnico (GMUD):";
    private const string NoImpactMessage = "Análise automática de impacto técnico (GMUD): nenhum projeto identificado nos comentários.";

    public string FormatComment(IReadOnlyList<TechnicalImpact> impacts)
    {
        if (impacts.Count == 0)
        {
            return NoImpactMessage;
        }

        var builder = new StringBuilder(Header);
        foreach (var impact in impacts)
        {
            var status = impact.BranchExists ? "encontrada" : "NÃO encontrada — verificar antes do merge";
            builder.Append(Environment.NewLine).Append($"- {impact.ProjectName}: branch \"{impact.BranchName}\" {status}");
        }

        return builder.ToString();
    }
}
