using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>Formata o resultado da análise de impacto técnico em um texto legível para publicação como comentário.</summary>
public interface ITechnicalImpactReportFormatter
{
    string FormatComment(IReadOnlyList<TechnicalImpact> impacts);
}
