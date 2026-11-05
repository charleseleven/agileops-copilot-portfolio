using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>Formata o relatório consolidado de release (branches, PRs e tags) da GMUD (US 3.2, Task 207).</summary>
public interface IReleaseReportFormatter
{
    string Format(ReleaseReportData data);
}
