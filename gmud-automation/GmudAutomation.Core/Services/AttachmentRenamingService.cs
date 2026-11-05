using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IAttachmentRenamingService"/>
public sealed class AttachmentRenamingService : IAttachmentRenamingService
{
    private readonly IAzureDevOpsClient _client;
    private readonly IAttachmentFileNameBuilder _fileNameBuilder;

    public AttachmentRenamingService(IAzureDevOpsClient client, IAttachmentFileNameBuilder fileNameBuilder)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _fileNameBuilder = fileNameBuilder ?? throw new ArgumentNullException(nameof(fileNameBuilder));
    }

    public async Task<string> RenameAndAttachAsync(
        WorkItemAttachment attachment, int userStoryNumber, int taskNumber, int targetWorkItemId, CancellationToken cancellationToken = default)
    {
        var newFileName = _fileNameBuilder.Build(userStoryNumber, taskNumber, attachment.OriginalFileName);

        var content = await _client.DownloadAttachmentAsync(attachment.Url, cancellationToken);
        var uploadedUrl = await _client.UploadAttachmentAsync(newFileName, content, cancellationToken);
        await _client.AttachFileToWorkItemAsync(
            targetWorkItemId, uploadedUrl, $"Anexo renomeado automaticamente de \"{attachment.OriginalFileName}\" para \"{newFileName}\".", cancellationToken);

        return newFileName;
    }
}
