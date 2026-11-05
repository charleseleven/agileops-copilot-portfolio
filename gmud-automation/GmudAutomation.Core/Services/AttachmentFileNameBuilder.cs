using System.IO;

namespace GmudAutomation.Core.Services;

/// <inheritdoc cref="IAttachmentFileNameBuilder"/>
public sealed class AttachmentFileNameBuilder : IAttachmentFileNameBuilder
{
    public string Build(int userStoryNumber, int taskNumber, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName);
        return $"US{userStoryNumber}_SCRIPT_TASK{taskNumber}{extension}";
    }
}
