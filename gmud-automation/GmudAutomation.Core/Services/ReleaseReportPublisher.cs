using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IReleaseReportPublisher"/>
public sealed class ReleaseReportPublisher : IReleaseReportPublisher
{
    private readonly IReleaseReportFormatter _formatter;
    private readonly IAzureDevOpsClient _client;

    public ReleaseReportPublisher(IReleaseReportFormatter formatter, IAzureDevOpsClient client)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task PublishAsync(int workItemId, ReleaseReportData data, CancellationToken cancellationToken = default)
    {
        var report = _formatter.Format(data);
        await _client.AddWorkItemCommentAsync(workItemId, report, cancellationToken);
    }
}
