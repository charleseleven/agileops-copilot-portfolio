using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>Publica o relatório de release consolidado como comentário na Task principal da GMUD (US 3.2, Task 208).</summary>
public interface IReleaseReportPublisher
{
    Task PublishAsync(int workItemId, ReleaseReportData data, CancellationToken cancellationToken = default);
}
