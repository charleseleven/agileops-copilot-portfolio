using FluentAssertions;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Services;

public class GmudTechnicalImpactPublisherTests
{
    private readonly Mock<ITechnicalImpactService> _technicalImpactServiceMock = new();
    private readonly Mock<ITechnicalImpactReportFormatter> _formatterMock = new();
    private readonly Mock<IAzureDevOpsClient> _clientMock = new();
    private readonly GmudTechnicalImpactPublisher _sut;

    public GmudTechnicalImpactPublisherTests()
    {
        _sut = new GmudTechnicalImpactPublisher(_technicalImpactServiceMock.Object, _formatterMock.Object, _clientMock.Object);
    }

    [Fact]
    public async Task AnalyzeAndPublishAsync_ImpactoIdentificado_PublicaComentarioFormatadoNoCardDoWorkItem()
    {
        TechnicalImpact[] impacts = [new("Contoso.PortalCliente.API", "feature/us_192", BranchExists: true)];
        const string comentarioFormatado = "Análise automática de impacto técnico (GMUD):\n- Contoso.PortalCliente.API: branch \"feature/us_192\" encontrada";

        _technicalImpactServiceMock.Setup(s => s.IdentifyTechnicalImpactAsync(192, It.IsAny<CancellationToken>()))
            .ReturnsAsync(impacts);
        _formatterMock.Setup(f => f.FormatComment(impacts)).Returns(comentarioFormatado);

        var result = await _sut.AnalyzeAndPublishAsync(192);

        result.Should().BeSameAs(impacts);
        _clientMock.Verify(c => c.AddWorkItemCommentAsync(192, comentarioFormatado, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeAndPublishAsync_NenhumImpactoIdentificado_AindaAssimPublicaComentarioNoCard()
    {
        _technicalImpactServiceMock.Setup(s => s.IdentifyTechnicalImpactAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _formatterMock.Setup(f => f.FormatComment(It.IsAny<IReadOnlyList<TechnicalImpact>>()))
            .Returns("Análise automática de impacto técnico (GMUD): nenhum projeto identificado nos comentários.");

        await _sut.AnalyzeAndPublishAsync(200);

        _clientMock.Verify(c => c.AddWorkItemCommentAsync(200, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
