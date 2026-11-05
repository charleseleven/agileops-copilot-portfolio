using FluentAssertions;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Services;

public class AttachmentRenamingServiceTests
{
    private readonly Mock<IAzureDevOpsClient> _clientMock = new();
    private readonly Mock<IAttachmentFileNameBuilder> _fileNameBuilderMock = new();
    private readonly AttachmentRenamingService _sut;

    public AttachmentRenamingServiceTests()
    {
        _sut = new AttachmentRenamingService(_clientMock.Object, _fileNameBuilderMock.Object);
    }

    [Fact]
    public async Task RenameAndAttachAsync_AnexoGenerico_BaixaRenomeiaEEnviaOAnexoParaOWorkItemDeDestino()
    {
        var attachment = new WorkItemAttachment("https://dev.azure.com/.../attachments/abc", "script_original.txt");
        var originalBytes = new byte[] { 1, 2, 3 };

        _fileNameBuilderMock.Setup(b => b.Build(196, 197, "script_original.txt")).Returns("US196_SCRIPT_TASK197.txt");
        _clientMock.Setup(c => c.DownloadAttachmentAsync(attachment.Url, It.IsAny<CancellationToken>())).ReturnsAsync(originalBytes);
        _clientMock.Setup(c => c.UploadAttachmentAsync("US196_SCRIPT_TASK197.txt", originalBytes, It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://dev.azure.com/.../attachments/novo");

        var result = await _sut.RenameAndAttachAsync(attachment, 196, 197, targetWorkItemId: 198);

        result.Should().Be("US196_SCRIPT_TASK197.txt");
        _clientMock.Verify(c => c.AttachFileToWorkItemAsync(
            198, "https://dev.azure.com/.../attachments/novo", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
