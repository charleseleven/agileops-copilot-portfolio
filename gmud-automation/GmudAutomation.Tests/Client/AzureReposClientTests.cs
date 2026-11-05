using System.Net;
using FluentAssertions;
using GmudAutomation.Core.Client;

namespace GmudAutomation.Tests.Client;

public class AzureReposClientTests
{
    private static AzureReposClient CreateSut(QueueHandler handler) => new(new HttpClient(handler), new AzureDevOpsClientOptions
    {
        Organization = "contoso",
        BoardsProject = "Contoso Projetos",
        ReposProject = "Contoso Repositorios",
        PersonalAccessToken = "fake-pat-for-tests"
    }, mergePollInterval: TimeSpan.Zero);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task GetBranchAsync_FiltroDaApiEhPorPrefixo_RetornaSoONomeExato()
    {
        var handler = new QueueHandler(Json("""
            {"value":[{"name":"refs/heads/feature/us_10","objectId":"a"},{"name":"refs/heads/feature/us_1","objectId":"b"}]}
            """));

        var branch = await CreateSut(handler).GetBranchAsync("Repo", "feature/us_1");

        branch!.ObjectId.Should().Be("b");
        handler.Requests.Single().RequestUri!.AbsolutePath.Should().Contain("Contoso%20Repositorios/_apis/git/repositories/Repo/refs");
    }

    [Fact]
    public async Task ListRepositoriesAsync_LeABranchPadraoSemRefsHeads()
    {
        var handler = new QueueHandler(Json("""
            {"value":[{"id":"1","name":"Contoso.Legado.Database","defaultBranch":"refs/heads/main"},{"id":"2","name":"Vazio"}]}
            """));

        var repositories = await CreateSut(handler).ListRepositoriesAsync();

        repositories[0].DefaultBranch.Should().Be("main");
        repositories[1].DefaultBranch.Should().BeNull("repositório vazio não tem branch padrão");
    }

    [Fact]
    public async Task GetBranchAsync_RepositorioInexistente_RetornaNull()
    {
        (await CreateSut(new QueueHandler(new HttpResponseMessage(HttpStatusCode.NotFound))).GetBranchAsync("Nao.Existe", "main")).Should().BeNull();
    }

    [Fact]
    public async Task CreateBranchFromCommitAsync_ApiRecusaCom200_LancaExcecaoSemSobrescrever()
    {
        var handler = new QueueHandler(Json("""{"value":[{"success":false,"updateStatus":"failedToUpdateRef"}]}"""));

        var act = () => CreateSut(handler).CreateBranchFromCommitAsync("Repo", "release/GMUD_PROD_CLI1_22102026", "abc");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*recusada*failedToUpdateRef*");
        (await handler.Requests.Single().Content!.ReadAsStringAsync()).Should().Contain("\"oldObjectId\":\"0000000000000000000000000000000000000000\"");
    }

    [Fact]
    public async Task UpdateBranchAsync_EnviaOCommitAntigoEsperado()
    {
        var handler = new QueueHandler(Json("""{"value":[{"success":true}]}"""));

        await CreateSut(handler).UpdateBranchAsync("Repo", "feature/us_25482", "old", "new");

        var body = await handler.Requests.Single().Content!.ReadAsStringAsync();
        body.Should().Contain("\"oldObjectId\":\"old\"").And.Contain("\"newObjectId\":\"new\"").And.Contain("refs/heads/feature/us_25482");
    }

    [Fact]
    public async Task MergeCommitsAsync_OperacaoAssincrona_ConsultaAteConcluirERetornaOCommit()
    {
        var handler = new QueueHandler(
            Json("""{"mergeOperationId":7,"status":"queued"}"""),
            Json("""{"mergeOperationId":7,"status":"inProgress"}"""),
            Json("""{"mergeOperationId":7,"status":"completed","detailedStatus":{"mergeCommitId":"m1"}}"""));

        var result = await CreateSut(handler).MergeCommitsAsync("Repo", "target", "source", "GMUD 1: merge feature/us_2");

        result.MergeCommitId.Should().Be("m1");
        result.HasConflicts.Should().BeFalse();
        (await handler.Requests[0].Content!.ReadAsStringAsync()).Should().Contain("\"parents\":[\"target\",\"source\"]");
    }

    [Fact]
    public async Task MergeCommitsAsync_Conflito_RetornaArquivosSemMoverBranch()
    {
        var handler = new QueueHandler(Json("""{"mergeOperationId":7,"status":"conflicts","detailedStatus":{"conflicts":[{"path":"/src/a.cs"}]}}"""));

        var result = await CreateSut(handler).MergeCommitsAsync("Repo", "target", "source", "msg");

        result.HasConflicts.Should().BeTrue();
        result.ConflictingFiles.Should().Equal("/src/a.cs");
        handler.Requests.Should().ContainSingle("nenhuma atualização de ref é feita no conflito");
    }

    [Fact]
    public async Task GetDiffAsync_IgnoraPastasERetornaTipoDaAlteracao()
    {
        var handler = new QueueHandler(Json("""
            {"allChangesIncluded":true,"changes":[{"item":{"path":"/src","isFolder":true},"changeType":"edit"},{"item":{"path":"/src/a.cs"},"changeType":"edit"}]}
            """));

        var changes = await CreateSut(handler).GetDiffAsync("Repo", "base", "target");

        changes.Should().ContainSingle().Which.Path.Should().Be("/src/a.cs");
    }

    [Fact]
    public async Task PushChangesAsync_EnviaConteudoEmBase64EORefEsperado()
    {
        var handler = new QueueHandler(Json("""{"commits":[{"commitId":"c9"}]}"""));

        var commit = await CreateSut(handler).PushChangesAsync("Repo", "feature/us_1", "old", "msg",
            [new("/scripts/a.sql", "add", "SELECT 1"u8.ToArray()), new("/b.cs", "delete", null)]);

        commit.Should().Be("c9");
        var body = await handler.Requests.Single().Content!.ReadAsStringAsync();
        body.Should().Contain("\"oldObjectId\":\"old\"").And.Contain(Convert.ToBase64String("SELECT 1"u8.ToArray())).And.Contain("\"changeType\":\"delete\"");
    }

    [Fact]
    public async Task ListBuildsForBranchAsync_LeIdStatusResultadoECommit()
    {
        var handler = new QueueHandler(Json("""
            {"value":[{"id":31002,"buildNumber":"31002","status":"completed","result":"succeeded","definition":{"name":"web-ci"},
            "sourceBranch":"refs/heads/release/X","sourceVersion":"abc","queueTime":"2026-11-04T10:00:00Z"}]}
            """));

        var build = (await CreateSut(handler).ListBuildsForBranchAsync("release/X")).Single();

        build.Id.Should().Be(31002);
        build.Result.Should().Be("succeeded");
        build.SourceVersion.Should().Be("abc");
        build.DefinitionName.Should().Be("web-ci");
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Copia o corpo antes de o cliente descartar a requisição.
            if (request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                request.Content = new StringContent(body);
            }

            Requests.Add(request);
            return responses[Math.Min(_index++, responses.Length - 1)];
        }
    }
}
