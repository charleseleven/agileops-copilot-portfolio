using System.Net;
using FluentAssertions;
using GmudAutomation.Core.Client;
using Moq;

namespace GmudAutomation.Tests.Client;

public class AppServiceClientTests
{
    private readonly Mock<IAzureAccessTokenProvider> _tokenProviderMock = new();

    private AppServiceClient CreateSut(SequencedHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new AppServiceClient(httpClient, new AppServiceClientOptions { SubscriptionId = "sub-1" }, _tokenProviderMock.Object);
    }

    private static HttpResponseMessage ResourceListResponse(string resourceGroup, string siteName) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $"{{\"value\":[{{\"id\":\"/subscriptions/sub-1/resourceGroups/{resourceGroup}/providers/Microsoft.Web/sites/{siteName}\"}}]}}")
    };

    private static HttpResponseMessage ConfigWebResponse(string linuxFxVersion) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{\"properties\":{{\"linuxFxVersion\":\"{linuxFxVersion}\"}}}}")
    };

    [Fact]
    public async Task GetCurrentTagAsync_LinuxFxVersionComTag_ExtraiApenasATag()
    {
        _tokenProviderMock.Setup(t => t.GetAccessTokenAsync("https://management.azure.com/", It.IsAny<CancellationToken>()))
            .ReturnsAsync("fake-arm-token");
        var handler = new SequencedHttpMessageHandler(
            ResourceListResponse("ContosoDemo-PRD-01", "contoso-portalcliente-api-prd-cli1"),
            ConfigWebResponse("DOCKER|contoso.azurecr.io/portalcliente-api:25450"));
        var sut = CreateSut(handler);

        var result = await sut.GetCurrentTagAsync("contoso-portalcliente-api-prd-cli1");

        result.Should().Be("25450");
    }

    [Fact]
    public async Task GetCurrentTagAsync_ResolveOResourceGroupAutomaticamentePeloNomeDoSite()
    {
        _tokenProviderMock.Setup(t => t.GetAccessTokenAsync("https://management.azure.com/", It.IsAny<CancellationToken>()))
            .ReturnsAsync("fake-arm-token");
        var handler = new SequencedHttpMessageHandler(
            ResourceListResponse("ContosoDemo-GMUD-01", "contoso-portalcliente-web-gmud-cli2"),
            ConfigWebResponse("DOCKER|repo/img:30001"));
        var sut = CreateSut(handler);

        await sut.GetCurrentTagAsync("contoso-portalcliente-web-gmud-cli2");

        handler.Requests[1].RequestUri!.ToString().Should().Contain("ContosoDemo-GMUD-01");
    }

    [Fact]
    public async Task GetCurrentTagAsync_SiteNaoEncontradoNaSubscription_LancaExcecaoClara()
    {
        _tokenProviderMock.Setup(t => t.GetAccessTokenAsync("https://management.azure.com/", It.IsAny<CancellationToken>()))
            .ReturnsAsync("fake-arm-token");
        var handler = new SequencedHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":[]}""")
        });
        var sut = CreateSut(handler);

        var act = () => sut.GetCurrentTagAsync("site-que-nao-existe");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*site-que-nao-existe*");
    }

    [Fact]
    public async Task GetCurrentTagAsync_UsaOTokenDoProviderInformadoComoBearer()
    {
        _tokenProviderMock.Setup(t => t.GetAccessTokenAsync("https://management.azure.com/", It.IsAny<CancellationToken>()))
            .ReturnsAsync("outro-token");
        var handler = new SequencedHttpMessageHandler(
            ResourceListResponse("rg-x", "qualquer-site"),
            ConfigWebResponse("DOCKER|repo/img:1"));
        var sut = CreateSut(handler);

        await sut.GetCurrentTagAsync("qualquer-site");

        handler.Requests.Should().OnlyContain(r => r.Headers.Authorization!.Scheme == "Bearer" && r.Headers.Authorization!.Parameter == "outro-token");
    }

    [Fact]
    public async Task GetContainerImageAsync_SiteExistente_RetornaAImagemSemOPrefixoDocker()
    {
        _tokenProviderMock.Setup(t => t.GetAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        var handler = new SequencedHttpMessageHandler(
            ResourceListResponse("rg-web", "contoso-web-prd-cli1"),
            ConfigWebResponse("DOCKER|registro.azurecr.io/contoso.web:30945-GMUD_PROD_CLI1_22102026-6618eda39fb8daf10b122ac0b76d9fcec8629a78"));
        var sut = CreateSut(handler);

        var image = await sut.GetContainerImageAsync("contoso-web-prd-cli1");

        image.Should().Be("registro.azurecr.io/contoso.web:30945-GMUD_PROD_CLI1_22102026-6618eda39fb8daf10b122ac0b76d9fcec8629a78");
    }

    [Fact]
    public async Task GetContainerImageAsync_SiteInexistente_RetornaNullParaOFluxoListarCandidatos()
    {
        _tokenProviderMock.Setup(t => t.GetAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        var sut = CreateSut(new SequencedHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"value":[]}""") }));

        (await sut.GetContainerImageAsync("nao-existe")).Should().BeNull();
    }

    [Fact]
    public async Task FindSitesAsync_FiltraPorFragmentoSemDiferenciarMaiusculas()
    {
        _tokenProviderMock.Setup(t => t.GetAccessTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("token");
        var handler = new SequencedHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"value":[{"name":"contoso-web-prd-cli1"},{"name":"contoso-api-gmud-cli1"},{"name":"outro"}]}""")
        });
        var sut = CreateSut(handler);

        var sites = await sut.FindSitesAsync("CLI1");

        sites.Should().Equal("contoso-api-gmud-cli1", "contoso-web-prd-cli1");
        Uri.UnescapeDataString(handler.Requests.Single().RequestUri!.Query).Should().Contain("substringof('cli1', name)");
    }

    private sealed class SequencedHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var response = responses[Math.Min(_index, responses.Length - 1)];
            _index++;
            return Task.FromResult(response);
        }
    }
}
