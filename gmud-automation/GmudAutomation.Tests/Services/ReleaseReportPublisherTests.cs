using FluentAssertions;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Services;

public class ReleaseReportPublisherTests
{
    private readonly Mock<IReleaseReportFormatter> _formatterMock = new();
    private readonly Mock<IAzureDevOpsClient> _clientMock = new();
    private readonly ReleaseReportPublisher _sut;

    public ReleaseReportPublisherTests()
    {
        _sut = new ReleaseReportPublisher(_formatterMock.Object, _clientMock.Object);
    }

    [Fact]
    public async Task PublishAsync_DadosDeRelease_PublicaOTextoFormatadoComoComentarioNaTaskPrincipal()
    {
        var data = new ReleaseReportData("release/x", "main", null, null, [], null, null, null, null);
        _formatterMock.Setup(f => f.Format(data)).Returns("Relatório formatado");

        await _sut.PublishAsync(208, data);

        _clientMock.Verify(c => c.AddWorkItemCommentAsync(208, "Relatório formatado", It.IsAny<CancellationToken>()), Times.Once);
    }
}
