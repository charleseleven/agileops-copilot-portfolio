using System.Net;
using AgilePredict.Controllers;
using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Models.Flow;
using AgilePredict.Services;
using AgilePredict.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace AgilePredict.Tests.Controllers
{
    public class SprintRiskControllerTests
    {
        private readonly Mock<ISprintRiskBot> _bot = new();

        private SprintRiskController Controller(AzureDevOpsConfiguration? defaults = null)
        {
            var resolver = new AzureDevOpsConnectionResolver(
                new AzureDevOpsBoardsUrlParser(),
                Options.Create(defaults ?? new AzureDevOpsConfiguration()));
            return new SprintRiskController(_bot.Object, resolver, Mock.Of<ILogger<SprintRiskController>>());
        }

        private static SprintRiskRequest Request(string? pat = "pat-req") => new()
        {
            BoardsUrl = "https://dev.azure.com/contoso/Meu%20Projeto/_boards/board",
            PersonalAccessToken = pat
        };

        [Fact]
        public async Task GetReport_ComUrlEPat_RetornaRelatorioDoBot()
        {
            var report = new SprintRiskReport();
            _bot.Setup(b => b.GenerateReportAsync(new AzureDevOpsConnection("contoso", "Meu Projeto", "pat-req"), null, null, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(report);

            var result = await Controller().GetReport(Request(), CancellationToken.None);

            Assert.Same(report, Assert.IsType<OkObjectResult>(result.Result).Value);
        }

        [Fact]
        public async Task GetReport_SemPatNaRequisicaoNemNoServidor_Retorna400()
        {
            var result = await Controller().GetReport(Request(pat: null), CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            _bot.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetReport_SemPatNaRequisicao_UsaOPatDaOrganizacaoNoServidor()
        {
            var defaults = new AzureDevOpsConfiguration();
            defaults.OrganizationTokens["Contoso"] = "pat-servidor";
            _bot.Setup(b => b.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), null, null, true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SprintRiskReport());

            await Controller(defaults).GetReport(Request(pat: null), CancellationToken.None);

            _bot.Verify(b => b.GenerateReportAsync(
                It.Is<AzureDevOpsConnection>(c => c.PersonalAccessToken == "pat-servidor"), null, null, true, It.IsAny<CancellationToken>()));
        }

        [Fact]
        public async Task GetReport_UrlDaSprintNoCampoDeSprint_ExtraiOIterationPath()
        {
            var request = Request();
            request.IterationPath = "https://dev.azure.com/contoso/Meu%20Projeto/_sprints/taskboard/Meu%20Projeto%20Team/Meu%20Projeto/Sprint%208";
            _bot.Setup(b => b.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), null, It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SprintRiskReport());

            await Controller().GetReport(request, CancellationToken.None);

            _bot.Verify(b => b.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), null, "Meu Projeto\\Sprint 8", true, It.IsAny<CancellationToken>()));
        }

        [Fact]
        public async Task GetReport_SprintNaoEncontrada_Retorna404()
        {
            _bot.Setup(b => b.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), null, null, true, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SprintNotFoundException("sem sprint atual"));

            var result = await Controller().GetReport(Request(), CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetReport_PatInvalido_Retorna502SemExporDetalhes()
        {
            _bot.Setup(b => b.GenerateReportAsync(It.IsAny<AzureDevOpsConnection>(), null, null, true, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("401", null, HttpStatusCode.Unauthorized));

            var result = await Controller().GetReport(Request(), CancellationToken.None);

            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, objectResult.StatusCode);
            Assert.Contains("inválido ou expirado", objectResult.Value!.ToString());
        }
    }
}
