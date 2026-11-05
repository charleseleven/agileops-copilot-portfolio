namespace GmudAutomation.Core.Services;

/// <summary>Gera o nome padronizado de anexo US&lt;numero_us&gt;_SCRIPT_TASK&lt;numero_task&gt;.ext (US 3.2, Task 206).</summary>
public interface IAttachmentFileNameBuilder
{
    string Build(int userStoryNumber, int taskNumber, string originalFileName);
}
