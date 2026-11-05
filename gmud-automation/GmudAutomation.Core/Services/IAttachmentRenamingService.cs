using GmudAutomation.Core.Models;

namespace GmudAutomation.Core.Services;

/// <summary>
/// US 3.2 (Task 206): baixa um anexo original, renomeia para o padrão US&lt;numero_us&gt;_SCRIPT_TASK&lt;numero_task&gt;
/// e o anexa ao Work Item de destino (a task filha da GMUD).
/// </summary>
public interface IAttachmentRenamingService
{
    Task<string> RenameAndAttachAsync(
        WorkItemAttachment attachment, int userStoryNumber, int taskNumber, int targetWorkItemId, CancellationToken cancellationToken = default);
}
