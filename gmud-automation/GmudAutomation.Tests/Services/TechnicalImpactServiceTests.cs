using FluentAssertions;
using GmudAutomation.Core.Client;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Services;

public class TechnicalImpactServiceTests
{
    private readonly Mock<IAzureDevOpsClient> _clientMock = new();
    private readonly Mock<IProjectTagExtractor> _tagExtractorMock = new();
    private readonly TechnicalImpactService _sut;

    public TechnicalImpactServiceTests()
    {
        _sut = new TechnicalImpactService(_clientMock.Object, _tagExtractorMock.Object);
    }

    [Fact]
    public async Task IdentifyTechnicalImpactAsync_ProjetoComBranchExistente_RetornaImpactoComBranchExistsTrue()
    {
        var comments = new[] { new WorkItemComment(1, "Alterado Contoso.PortalCliente.API", "Dev", DateTimeOffset.UtcNow) };
        _clientMock.Setup(c => c.GetWorkItemCommentsAsync(192, It.IsAny<CancellationToken>()))
            .ReturnsAsync(comments);
        _tagExtractorMock.Setup(t => t.ExtractProjectTags(comments[0].Text))
            .Returns(["Contoso.PortalCliente.API"]);
        _clientMock.Setup(c => c.BranchExistsAsync("Contoso.PortalCliente.API", "feature/us_192", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.IdentifyTechnicalImpactAsync(192);

        result.Should().ContainSingle();
        result[0].ProjectName.Should().Be("Contoso.PortalCliente.API");
        result[0].BranchName.Should().Be("feature/us_192");
        result[0].BranchExists.Should().BeTrue();
    }

    [Fact]
    public async Task IdentifyTechnicalImpactAsync_MesmoProjetoCitadoEmDoisComentarios_RetornaApenasUmImpactoDistinto()
    {
        var comments = new[]
        {
            new WorkItemComment(1, "Alterado Contoso.PortalCliente.API", "Dev", DateTimeOffset.UtcNow),
            new WorkItemComment(2, "Reforçando: Contoso.PortalCliente.API também mudou", "Dev", DateTimeOffset.UtcNow)
        };
        _clientMock.Setup(c => c.GetWorkItemCommentsAsync(192, It.IsAny<CancellationToken>()))
            .ReturnsAsync(comments);
        _tagExtractorMock.Setup(t => t.ExtractProjectTags(It.IsAny<string>()))
            .Returns(["Contoso.PortalCliente.API"]);
        _clientMock.Setup(c => c.BranchExistsAsync("Contoso.PortalCliente.API", "feature/us_192", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.IdentifyTechnicalImpactAsync(192);

        result.Should().ContainSingle();
        _clientMock.Verify(c => c.BranchExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IdentifyTechnicalImpactAsync_BranchNaoEncontradaNoRepositorio_RetornaImpactoComBranchExistsFalse()
    {
        var comments = new[] { new WorkItemComment(1, "Alterado Contoso.PortalCliente.WEB", "Dev", DateTimeOffset.UtcNow) };
        _clientMock.Setup(c => c.GetWorkItemCommentsAsync(194, It.IsAny<CancellationToken>()))
            .ReturnsAsync(comments);
        _tagExtractorMock.Setup(t => t.ExtractProjectTags(comments[0].Text))
            .Returns(["Contoso.PortalCliente.WEB"]);
        _clientMock.Setup(c => c.BranchExistsAsync("Contoso.PortalCliente.WEB", "feature/us_194", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _sut.IdentifyTechnicalImpactAsync(194);

        result[0].BranchExists.Should().BeFalse();
    }

    [Fact]
    public async Task IdentifyTechnicalImpactAsync_ComentariosSemTagsDeProjetoNemNosFilhos_RetornaListaVaziaSemConsultarBranch()
    {
        var comments = new[] { new WorkItemComment(1, "Apenas um comentário informativo.", "Dev", DateTimeOffset.UtcNow) };
        _clientMock.Setup(c => c.GetWorkItemCommentsAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(comments);
        _tagExtractorMock.Setup(t => t.ExtractProjectTags(comments[0].Text))
            .Returns([]);
        _clientMock.Setup(c => c.GetChildWorkItemIdsAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<int>());

        var result = await _sut.IdentifyTechnicalImpactAsync(200);

        result.Should().BeEmpty();
        _clientMock.Verify(c => c.BranchExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IdentifyTechnicalImpactAsync_TagJaEncontradaNosComentariosDaUserStory_NaoConsultaItensFilhos()
    {
        var comments = new[] { new WorkItemComment(1, "Alterado Contoso.PortalCliente.API", "Dev", DateTimeOffset.UtcNow) };
        _clientMock.Setup(c => c.GetWorkItemCommentsAsync(192, It.IsAny<CancellationToken>()))
            .ReturnsAsync(comments);
        _tagExtractorMock.Setup(t => t.ExtractProjectTags(comments[0].Text))
            .Returns(["Contoso.PortalCliente.API"]);
        _clientMock.Setup(c => c.BranchExistsAsync("Contoso.PortalCliente.API", "feature/us_192", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.IdentifyTechnicalImpactAsync(192);

        _clientMock.Verify(c => c.GetChildWorkItemIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IdentifyTechnicalImpactAsync_SemTagNaUserStoryMasComTagEmComentarioDeTaskFilha_BuscaNoFilhoERetornaImpactoComBranchDaUserStory()
    {
        var parentComments = new[] { new WorkItemComment(1, "Comentário sem tag de projeto.", "Dev", DateTimeOffset.UtcNow) };
        var childComments = new[] { new WorkItemComment(2, "Alterado Contoso.PortalCliente.WEB", "Dev", DateTimeOffset.UtcNow) };

        _clientMock.Setup(c => c.GetWorkItemCommentsAsync(19390, It.IsAny<CancellationToken>()))
            .ReturnsAsync(parentComments);
        _tagExtractorMock.Setup(t => t.ExtractProjectTags(parentComments[0].Text))
            .Returns([]);
        _clientMock.Setup(c => c.GetChildWorkItemIdsAsync(19390, It.IsAny<CancellationToken>()))
            .ReturnsAsync([19391]);
        _clientMock.Setup(c => c.GetWorkItemCommentsAsync(19391, It.IsAny<CancellationToken>()))
            .ReturnsAsync(childComments);
        _tagExtractorMock.Setup(t => t.ExtractProjectTags(childComments[0].Text))
            .Returns(["Contoso.PortalCliente.WEB"]);
        _clientMock.Setup(c => c.BranchExistsAsync("Contoso.PortalCliente.WEB", "feature/us_19390", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _sut.IdentifyTechnicalImpactAsync(19390);

        result.Should().ContainSingle();
        result[0].ProjectName.Should().Be("Contoso.PortalCliente.WEB");
        result[0].BranchName.Should().Be("feature/us_19390");
        result[0].BranchExists.Should().BeTrue();
    }
}
