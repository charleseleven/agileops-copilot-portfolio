using FluentAssertions;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline.Analysis;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;

namespace GmudAutomation.Tests.Pipeline;

public class WorkItemReferenceParserTests
{
    [Fact]
    public void Extract_LinksEMencoes_RetornaNaOrdemDeAparicaoSemRepetir()
    {
        const string html = """
            <div>Aplicar <a href="https://dev.azure.com/contoso/Projetos/_workitems/edit/20611/" data-vss-mention="version:1.0">#20611</a>
            e também #20700.</div><p>Repetido: #20611 e https://dev.azure.com/contoso/Projetos/_workitems/edit/20800</p>
            """;

        WorkItemReferenceParser.Extract(html).Should().Equal(20611, 20700, 20800);
    }

    [Theory]
    [InlineData("<p style=\"color:#fff\">cor</p>")]
    [InlineData("<a href=\"https://site/page#123\">âncora</a>")]
    [InlineData("<p>abc#123</p>")]
    [InlineData("")]
    [InlineData(null)]
    public void Extract_SemReferencias_RetornaVazio(string? html)
    {
        WorkItemReferenceParser.Extract(html).Should().BeEmpty();
    }

    [Fact]
    public void ToPlainText_IncluiTextoEUrlsDosLinks()
    {
        var text = WorkItemReferenceParser.ToPlainText("<p>Alteração <b>WEB</b> &amp; <a href=\"https://x/_git/Repo.API\">PR</a></p>");

        text.Should().Contain("Alteração WEB & PR").And.Contain("https://x/_git/Repo.API");
    }
}

public class ArtifactLinkParserTests
{
    [Fact]
    public void TryParse_Commit_RetornaRepositorioECommit()
    {
        ArtifactLinkParser.TryParse("vstfs:///Git/Commit/proj-guid%2Frepo-guid%2F6618EDA3", out var link).Should().BeTrue();

        link.Should().Be(new ArtifactLink(ArtifactLinkKind.Commit, "proj-guid", "repo-guid", "6618eda3"));
    }

    [Fact]
    public void TryParse_Branch_RemovePrefixoGBEDecodificaONome()
    {
        ArtifactLinkParser.TryParse("vstfs:///Git/Ref/proj-guid%2Frepo-guid%2FGBfeature%2Fus_20611", out var link).Should().BeTrue();

        link.Kind.Should().Be(ArtifactLinkKind.Branch);
        link.Value.Should().Be("feature/us_20611");
    }

    [Fact]
    public void TryParse_PullRequest_RetornaOId()
    {
        ArtifactLinkParser.TryParse("vstfs:///Git/PullRequestId/proj-guid%2Frepo-guid%2F321", out var link).Should().BeTrue();

        link.Kind.Should().Be(ArtifactLinkKind.PullRequest);
        link.Value.Should().Be("321");
    }

    [Theory]
    [InlineData("vstfs:///Git/Ref/proj%2Frepo%2FGTv1.0")]
    [InlineData("vstfs:///Build/Build/123")]
    [InlineData("https://dev.azure.com/x")]
    [InlineData(null)]
    public void TryParse_ForaDoEscopo_RetornaFalse(string? uri)
    {
        ArtifactLinkParser.TryParse(uri, out _).Should().BeFalse();
    }
}

public class ProjectDetectorTests
{
    private readonly ProjectDetector _sut = new(
    [
        new RepositoryConfig("Contoso.PortalCliente.WEB", RepositoryKind.Web, ["WEB", "Portal Web"]),
        new RepositoryConfig("Contoso.PortalCliente.API", RepositoryKind.Api, ["API"]),
        new RepositoryConfig("Contoso.Legado.Database", RepositoryKind.Database, ["Database", "Banco de dados"])
    ]);

    [Theory]
    [InlineData("Alterações (WEB)", "Contoso.PortalCliente.WEB")]
    [InlineData("ajuste no contoso.portalcliente.api", "Contoso.PortalCliente.API")]
    [InlineData("mudança no  portal   web", "Contoso.PortalCliente.WEB")]
    [InlineData("script de Banco de dados anexo", "Contoso.Legado.Database")]
    public void Detect_NomeOuAlias_RetornaORepositorio(string text, string expected)
    {
        _sut.Detect(text).Should().Equal(expected);
    }

    [Theory]
    [InlineData("WEBSITE institucional")]
    [InlineData("Outro.Produto.API foi alterado")]
    [InlineData("")]
    public void Detect_SemCitacaoReal_RetornaVazio(string text)
    {
        _sut.Detect(text).Should().BeEmpty();
    }

    [Fact]
    public void Detect_VariosProjetos_RetornaTodosNaOrdemDaConfiguracao()
    {
        _sut.Detect("API: novo endpoint; WEB: nova tela").Should().Equal("Contoso.PortalCliente.WEB", "Contoso.PortalCliente.API");
    }
}

public class UserStoryCommitClassifierTests
{
    private static readonly IReadOnlySet<string> NoLinks = new HashSet<string>();

    [Theory]
    [InlineData("Task #20612: ajuste na tela")]
    [InlineData("AB#20611 corrige validação")]
    [InlineData("US_20611 ajuste")]
    [InlineData("feature/us_20611 - ajustes finais")]
    [InlineData("Bug 20612 corrigido")]
    public void BelongsToUserStory_MensagemCitaAUserStoryOuItem_RetornaTrue(string message)
    {
        var commit = new GitCommitInfo("abc", message, ["p1"]);

        UserStoryCommitClassifier.BelongsToUserStory(commit, [20611, 20612], NoLinks).Should().BeTrue();
    }

    [Theory]
    [InlineData("Task #206123 outra coisa")]
    [InlineData("ajuste geral de develop")]
    public void BelongsToUserStory_MensagemNaoCita_RetornaFalse(string message)
    {
        var commit = new GitCommitInfo("abc", message, ["p1"]);

        UserStoryCommitClassifier.BelongsToUserStory(commit, [20611, 20612], NoLinks).Should().BeFalse();
    }

    [Fact]
    public void BelongsToUserStory_CommitVinculadoAoCard_RetornaTrueMesmoSemCitacao()
    {
        var commit = new GitCommitInfo("ABC123", "wip", ["p1"]);

        UserStoryCommitClassifier.BelongsToUserStory(commit, [20611], new HashSet<string> { "abc123" }).Should().BeTrue();
    }

    [Fact]
    public void Split_CommitsDeMergeSaoNeutros()
    {
        var commits = new[]
        {
            new GitCommitInfo("m1", "Merge branch 'develop' into feature/us_20611", ["a", "b"]),
            new GitCommitInfo("c1", "#20611 tela", ["a"]),
            new GitCommitInfo("c2", "outra feature", ["a"])
        };

        var (userStory, outOfScope) = UserStoryCommitClassifier.Split(commits, [20611], NoLinks);

        userStory.Select(c => c.CommitId).Should().Equal("c1");
        outOfScope.Select(c => c.CommitId).Should().Equal("c2");
    }
}

public class ReconciliationPlannerTests
{
    private static IReadOnlySet<string> Set(params string[] paths) => paths.ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Plan_ArquivoSoDaUserStoryEIntocadoNaProducao_AplicaVersaoDaFeature()
    {
        var plans = ReconciliationPlanner.Plan(Set("/a.cs"), Set(), Set(), new Dictionary<string, string> { ["/a.cs"] = "edit" });

        plans.Should().ContainSingle().Which.Should().Be(new FilePlan("/a.cs", ConflictResolution.TakeFeature, plans[0].Reason, "edit"));
    }

    [Fact]
    public void Plan_ArquivoTambemDeCommitForaDoEscopo_EhAmbiguo()
    {
        var plans = ReconciliationPlanner.Plan(Set("/a.cs"), Set("/a.cs"), Set(), new Dictionary<string, string> { ["/a.cs"] = "edit" });

        plans.Single().Resolution.Should().Be(ConflictResolution.Ambiguous);
        plans.Single().PushChangeType.Should().BeNull();
    }

    [Fact]
    public void Plan_ArquivoAlteradoNaProducaoENaUserStory_EhAmbiguo()
    {
        var plans = ReconciliationPlanner.Plan(Set("/a.cs"), Set(), Set("/a.cs"), new Dictionary<string, string> { ["/a.cs"] = "edit" });

        plans.Single().Resolution.Should().Be(ConflictResolution.Ambiguous);
    }

    [Fact]
    public void Plan_ArquivoSoForaDoEscopo_EhDescartado()
    {
        var plans = ReconciliationPlanner.Plan(Set(), Set("/develop.cs"), Set(), new Dictionary<string, string> { ["/develop.cs"] = "add" });

        plans.Single().Resolution.Should().Be(ConflictResolution.Discarded);
        plans.Single().PushChangeType.Should().BeNull();
    }

    [Theory]
    [InlineData("add", "add")]
    [InlineData("delete", "delete")]
    [InlineData("edit", "edit")]
    public void Plan_TipoDaAlteracaoLiquidaDefineOPush(string netChange, string expected)
    {
        var plans = ReconciliationPlanner.Plan(Set("/a.cs"), Set(), Set(), new Dictionary<string, string> { ["/a.cs"] = netChange });

        plans.Single().PushChangeType.Should().Be(expected);
    }

    [Fact]
    public void Plan_RenomeacaoNaFeature_EhAmbigua()
    {
        var plans = ReconciliationPlanner.Plan(Set("/novo.cs"), Set(), Set(), new Dictionary<string, string> { ["/novo.cs"] = "rename" });

        plans.Single().Resolution.Should().Be(ConflictResolution.Ambiguous);
    }

    [Fact]
    public void Plan_AlteradoERevertidoNaUserStory_NaoGeraPlano()
    {
        ReconciliationPlanner.Plan(Set("/a.cs"), Set(), Set(), new Dictionary<string, string>()).Should().BeEmpty();
    }
}
