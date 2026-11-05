using System.Net;
using FluentAssertions;
using GmudAutomation.Core.Client;

namespace GmudAutomation.Tests.Client;

public class AzureDevOpsClientTests
{
    private static AzureDevOpsClient CreateSut(HttpResponseMessage response)
    {
        var handler = new StubHttpMessageHandler(response);
        var httpClient = new HttpClient(handler);
        return new AzureDevOpsClient(httpClient, new AzureDevOpsClientOptions
        {
            Organization = "eleven11C",
            BoardsProject = "Applied AI Engineering",
            PersonalAccessToken = "fake-pat-for-tests"
        });
    }

    [Fact]
    public async Task BranchExistsAsync_RepositorioInexistenteRetorna404_RetornaFalseSemLancarExcecao()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var sut = CreateSut(response);

        var result = await sut.BranchExistsAsync("Projeto.Que.Nao.Existe", "feature/us_192");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task BranchExistsAsync_RepositorioExistenteComBranch_RetornaTrue()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"count":1,"value":[{"name":"refs/heads/feature/us_192"}]}""")
        };
        var sut = CreateSut(response);

        var result = await sut.BranchExistsAsync("Contoso.PortalCliente.API", "feature/us_192");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task BranchExistsAsync_RepositorioExistenteSemABranch_RetornaFalse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"count":0,"value":[]}""")
        };
        var sut = CreateSut(response);

        var result = await sut.BranchExistsAsync("Contoso.PortalCliente.API", "feature/us_999");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task BranchExistsAsync_ErroDeAutenticacao_LancaHttpRequestException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var sut = CreateSut(response);

        var act = () => sut.BranchExistsAsync("Contoso.PortalCliente.API", "feature/us_192");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task MergeBranchesAsync_ApiRetornaConflito409_LancaMergeConflictExceptionComOsArquivosConflitantes()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(
                """{"detailedMergeStatus":{"conflicts":[{"path":"/src/Arquivo1.cs"},{"path":"/src/Arquivo2.cs"}]}}""")
        };
        var sut = CreateSut(response);

        var act = () => sut.MergeBranchesAsync("Contoso.PortalCliente.API", "feature/us_192", "release/GMUD_PROD_Contoso_10092026");

        (await act.Should().ThrowAsync<MergeConflictException>())
            .Which.ConflictingFiles.Should().BeEquivalentTo(["/src/Arquivo1.cs", "/src/Arquivo2.cs"]);
    }

    [Fact]
    public async Task MergeBranchesAsync_ApiRetornaSucesso_NaoLancaExcecao()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"succeeded"}""")
        };
        var sut = CreateSut(response);

        var act = () => sut.MergeBranchesAsync("Contoso.PortalCliente.API", "feature/us_192", "release/GMUD_PROD_Contoso_10092026");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetWorkItemAsync_UsaOBoardsProjectNaUrlMesmoQuandoReposProjectEDiferente()
    {
        var captured = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":1,"url":"u","fields":{"System.Title":"t","System.WorkItemType":"Task","System.State":"New"}}""")
        });
        var httpClient = new HttpClient(captured);
        var sut = new AzureDevOpsClient(httpClient, new AzureDevOpsClientOptions
        {
            Organization = "contoso",
            BoardsProject = "Contoso Projetos",
            ReposProject = "Contoso Repositorios",
            PersonalAccessToken = "fake-pat-for-tests"
        });

        await sut.GetWorkItemAsync(1);

        captured.LastRequestUri.Should().NotBeNull();
        captured.LastRequestUri!.ToString().Should().Contain("Contoso Projetos");
    }

    [Fact]
    public async Task BranchExistsAsync_UsaOReposProjectNaUrlMesmoQuandoDiferenteDoBoardsProject()
    {
        var captured = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"count":0,"value":[]}""")
        });
        var httpClient = new HttpClient(captured);
        var sut = new AzureDevOpsClient(httpClient, new AzureDevOpsClientOptions
        {
            Organization = "contoso",
            BoardsProject = "Contoso Projetos",
            ReposProject = "Contoso Repositorios",
            PersonalAccessToken = "fake-pat-for-tests"
        });

        await sut.BranchExistsAsync("Contoso.PortalCliente.API", "feature/us_192");

        captured.LastRequestUri.Should().NotBeNull();
        captured.LastRequestUri!.ToString().Should().Contain("Contoso Repositorios");
    }

    [Fact]
    public async Task GetChildWorkItemIdsAsync_RelacoesComHierarchyForward_RetornaApenasOsIdsDosFilhos()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "id": 19390,
                  "relations": [
                    { "rel": "System.LinkTypes.Hierarchy-Reverse", "url": "https://dev.azure.com/contoso/_apis/wit/workItems/24519" },
                    { "rel": "System.LinkTypes.Hierarchy-Forward", "url": "https://dev.azure.com/contoso/_apis/wit/workItems/19391" }
                  ]
                }
                """)
        };
        var sut = CreateSut(response);

        var result = await sut.GetChildWorkItemIdsAsync(19390);

        result.Should().ContainSingle().Which.Should().Be(19391);
    }

    [Fact]
    public async Task GetChildWorkItemIdsAsync_SemRelacoes_RetornaListaVazia()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":19390}""")
        };
        var sut = CreateSut(response);

        var result = await sut.GetChildWorkItemIdsAsync(19390);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestBuildDateForBranchAsync_BuildEncontrado_RetornaAQueueTime()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"count":1,"value":[{"id":25983,"queueTime":"2026-09-05T14:30:00Z"}]}""")
        };
        var sut = CreateSut(response);

        var result = await sut.GetLatestBuildDateForBranchAsync("release/GMUD_GMUD_amafresp_04092026");

        result.Should().Be(DateTimeOffset.Parse("2026-09-05T14:30:00Z"));
    }

    [Fact]
    public async Task GetLatestBuildDateForBranchAsync_NenhumBuildEncontrado_RetornaNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"count":0,"value":[]}""")
        };
        var sut = CreateSut(response);

        var result = await sut.GetLatestBuildDateForBranchAsync("release/GMUD_GMUD_amafresp_04092026");

        result.Should().BeNull();
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class CapturingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(response);
        }
    }
}
