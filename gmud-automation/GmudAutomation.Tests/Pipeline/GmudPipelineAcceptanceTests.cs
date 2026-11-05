using FluentAssertions;
using GmudAutomation.Core.Models;
using GmudAutomation.Core.Pipeline;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;
using GmudAutomation.Tests.Pipeline.Fakes;

namespace GmudAutomation.Tests.Pipeline;

/// <summary>
/// Cenários BDD 1 a 10 do fluxo automatizado de GMUD contra Boards/Repos/App Services em memória.
/// O caso base reproduz, com nomes fictícios, a GMUD de referência executada manualmente
/// (GMUD 25482 → user story 20611, alteração no WEB + script .txt anexo à task).
/// </summary>
public sealed class GmudPipelineAcceptanceTests : IDisposable
{
    private const int GmudId = 25482;
    private const int UserStoryId = 20611;
    private const int TaskId = 20612;
    private const string Web = "Contoso.PortalCliente.WEB";
    private const string Api = "Contoso.PortalCliente.API";
    private const string Database = "Contoso.Legado.Database";
    private const string ProdRelease = "GMUD_PROD_CONTOSO_22102026";
    private const string ScriptName = "US20611_SCRIPT_TASK20612.txt";

    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "gmud-tests-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryBoards _boards = new();
    private readonly InMemoryRepos _repos = new();
    private readonly InMemoryAppServices _appServices = new();
    private readonly string _prodCommit;
    private readonly string _featureCommit;

    private GmudConfig _config = new()
    {
        Organization = "contoso",
        BoardsProject = "Contoso Projetos",
        ReposProject = "Contoso Repositorios",
        Repositories =
        [
            new RepositoryConfig(Web, RepositoryKind.Web, ["WEB"], "contoso-portalcliente-web-{ambiente}-{cliente}", "web"),
            new RepositoryConfig(Api, RepositoryKind.Api, ["API"], "contoso-portalcliente-api-{ambiente}-{cliente}", "api"),
            new RepositoryConfig(Database, RepositoryKind.Database, ["Database"])
        ],
        Clients = [new ClientConfig("Contoso", "CONTOSO", "contoso")],
        Build = new BuildConfig { TimeoutMinutes = 0, PollIntervalSeconds = 0 }
    };

    public GmudPipelineAcceptanceTests()
    {
        // Boards: GMUD → user story (Descrição) → task com alteração no WEB e script de banco anexo.
        _boards.Add(GmudId, "User Story", "GMUD 22-10-2026 - Contoso - Implantação",
            $"<div>Aplicar <a href=\"https://dev.azure.com/contoso/Contoso%20Projetos/_workitems/edit/{UserStoryId}/\">#{UserStoryId}</a></div>");
        _boards.Add(UserStoryId, "User Story", "Nova tela de consulta", null, InMemoryBoards.Child(TaskId));
        var script = _boards.AddAttachment(ScriptName, "ALTER TABLE Consulta ADD Coluna INT;");
        _boards.Add(TaskId, "Task", "Implementar tela", "<p>Alterações (WEB) na tela de consulta.</p>", script);

        // Repos: produção do WEB em release/GMUD_PROD_CONTOSO_22102026; a feature da US sai dela.
        _repos.AddRepository(Web);
        _repos.AddRepository(Api);
        _repos.AddRepository(Database);
        var root = _repos.Commit(Web, null, "inicial", ("/src/Tela.cs", "v1"), ("/src/Outro.cs", "v1"));
        _prodCommit = _repos.Commit(Web, root, "release", ("/src/Outro.cs", "v2"));
        _repos.SetBranch(Web, "release/" + ProdRelease, _prodCommit);
        _featureCommit = _repos.Commit(Web, _prodCommit, $"Task #{TaskId}: nova tela", ("/src/Tela.cs", "v1-us"));
        _repos.SetBranch(Web, $"feature/us_{UserStoryId}", _featureCommit);
        _repos.SetBranch(Database, "main", _repos.Commit(Database, null, "inicial", ("/README.md", "db")));
        _repos.SetBranch(Api, "main", _repos.Commit(Api, null, "inicial", ("/Program.cs", "api")));

        // App Services do cliente: produção roda a release acima (build 30945).
        _appServices.Images["contoso-portalcliente-web-prd-contoso"] = $"contosoregistry.azurecr.io/contoso.portalcliente.web:30945-{ProdRelease}-{_prodCommit}";
        _appServices.Images["contoso-portalcliente-web-gmud-contoso"] = $"contosoregistry.azurecr.io/contoso.portalcliente.web:30950-GMUD_GMUD_CONTOSO_15102026-{_prodCommit}";
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private sealed class ScriptedConfirmation(params bool[] answers) : IOperatorConfirmation
    {
        private int _index;
        public List<int> AskedAtSteps { get; } = [];

        public Task<bool> ConfirmAsync(int stepNumber, string stepName, string summary, CancellationToken cancellationToken = default)
        {
            AskedAtSteps.Add(stepNumber);
            return Task.FromResult(_index < answers.Length ? answers[_index++] : true);
        }
    }

    private FileGmudManifestStore Store => new(Path.Combine(_workspace, "gmuds"));

    private Task<GmudRunResult> RunCoreAsync(bool dryRun, bool confirm, int? fromStep, string input, IOperatorConfirmation? confirmation)
    {
        var steps = GmudPipelineFactory.CreateSteps(_config, _boards, _repos, _appServices, (_, _) => Task.CompletedTask);
        var runner = new GmudPipelineRunner(steps, Store, confirmation ?? new FlagOnlyOperatorConfirmation(), "gmuds");
        var target = input.Length == 0 ? $"https://dev.azure.com/contoso/Contoso%20Projetos/_workitems/edit/{GmudId}/" : input;
        GmudPipelineRunner.TryParseInput(target, new GmudAutomation.Core.Services.AzureBoardsUrlParser(), out var id, out _);
        return runner.RunAsync(new GmudRunRequest(target, id, dryRun, fromStep, confirm), new NullLogger());
    }

    private sealed class NullLogger : IGmudLogger
    {
        public void Info(string message, int? step = null) { }
        public void Warn(string message, int? step = null) { }
        public void Error(string message, int? step = null) { }
        public void StepFinished(int step, string name, string result, TimeSpan duration, string? message) { }
    }

    [Fact]
    public async Task Cenario1_ExtracaoDeDadosDaGmud_ManifestoRegistraDataClienteEUserStory()
    {
        var result = await RunAsync(dryRun: true);

        result.Outcome.Should().Be(GmudRunOutcome.DryRunCompleted);
        result.Manifest.Gmud.Date.Should().Be("22102026");
        result.Manifest.Gmud.Client.Should().Be("CONTOSO");
        result.Manifest.UserStories.Select(us => us.Id).Should().Equal(UserStoryId);
        Store.Load(GmudId).Should().NotBeNull("o manifesto é gravado em gmuds/<id>/manifest.json");
    }

    [Fact]
    public async Task Cenario2_TituloForaDoPadrao_ParaNaEtapa1SemCriarNadaNosRepositorios()
    {
        _boards.Add(GmudId, "User Story", "GMUD Contoso - Implantação", "<p>#20611</p>");

        var result = await RunAsync(confirm: true);

        result.Outcome.Should().Be(GmudRunOutcome.Failed);
        result.StoppedAtStep.Should().Be(1);
        result.Message.Should().Contain("DD-MM-AAAA");
        result.Manifest.GetStepStatus(1).Should().Be(GmudStepStatus.Failed);
        _repos.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Cenario3_MapeamentoDeProjetosEScripts_ListaWebEDatabaseESalvaOAnexo()
    {
        var result = await RunAsync(dryRun: true);

        var item = result.Manifest.UserStories.Single().Items.Single();
        item.Id.Should().Be(TaskId);
        item.Projects.Should().BeEquivalentTo(Web, Database);
        item.DatabaseAttachments.Should().Equal($"gmuds/{GmudId}/scripts/{ScriptName}");
        File.ReadAllText(Path.Combine(_workspace, "gmuds", GmudId.ToString(), "scripts", ScriptName))
            .Should().Contain("ALTER TABLE");
    }

    [Fact]
    public async Task Cenario4_TagVigente_RegistraBuildReleaseECommitDeProducao()
    {
        var result = await RunAsync(dryRun: true);

        var prd = result.Manifest.Environments["prd"]["web"];
        prd.AppService.Should().Be("contoso-portalcliente-web-prd-contoso");
        prd.BuildId.Should().Be(30945);
        prd.Release.Should().Be(ProdRelease);
        prd.CommitSha.Should().Be(_prodCommit);
        result.Manifest.Repositories[Web].BaseBranch.Should().Be("release/" + ProdRelease);
    }

    [Fact]
    public async Task Cenario4_ProducaoEmMainDoCliente_BaseEAMainSemPrefixoRelease()
    {
        _repos.SetBranch(Web, "main_contoso", _prodCommit);
        _appServices.Images["contoso-portalcliente-web-prd-contoso"] = $"contosoregistry.azurecr.io/contoso.portalcliente.web:31229-main_contoso-{_prodCommit}";

        var result = await RunAsync(dryRun: true);

        result.Outcome.Should().Be(GmudRunOutcome.DryRunCompleted);
        result.Manifest.Environments["prd"]["web"].Release.Should().Be("main_contoso");
        result.Manifest.Repositories[Web].BaseBranch.Should().Be("main_contoso");
        result.Manifest.Repositories[Web].BaseCommit.Should().Be(_prodCommit);
    }

    [Fact]
    public async Task Cenario5_AppServiceForaDoPadrao_ListaCandidatosEAguardaOOperador()
    {
        var image = _appServices.Images["contoso-portalcliente-web-prd-contoso"];
        _appServices.Images.Remove("contoso-portalcliente-web-prd-contoso");
        _appServices.Images["contoso-web-producao-contoso-01"] = image;

        var result = await RunAsync(dryRun: true);

        result.Outcome.Should().Be(GmudRunOutcome.Blocked);
        result.StoppedAtStep.Should().Be(6);
        result.Message.Should().Contain("contoso-web-producao-contoso-01").And.Contain("appServiceOverrides");
        result.Manifest.Environments["prd"]["web"].Candidates.Should().Contain("contoso-web-producao-contoso-01");

        // Operador escolhe o candidato na configuração e retoma da etapa 6.
        _config = WithOverride(_config, "contoso-web-producao-contoso-01");
        var resumed = await RunAsync(dryRun: true, fromStep: 6);

        resumed.Outcome.Should().Be(GmudRunOutcome.DryRunCompleted);
        resumed.Manifest.Environments["prd"]["web"].BuildId.Should().Be(30945);
    }

    [Fact]
    public async Task DryRun_ReproduzOResultadoManualSemEscreverNoAzure()
    {
        var result = await RunAsync(dryRun: true);

        result.Outcome.Should().Be(GmudRunOutcome.DryRunCompleted);
        _repos.Writes.Should().BeEmpty();
        result.Manifest.Plan.Should().Contain(p => p.Contains($"criaria feature/us_{GmudId} a partir de release/{ProdRelease}"));
        result.Manifest.Repositories[Web].Merges.Single().Status.Should().Be(MergeStatus.SimulatedOk);
        result.Manifest.Repositories[Web].Merges.Single().UserStoryFiles.Should().Equal("/src/Tela.cs");
        result.Manifest.Repositories.Keys.Should().BeEquivalentTo([Web], "o Database não recebe branch da GMUD");
        result.Manifest.Database!.Repository.Should().Be(Database);
        result.Manifest.GetStepStatus(7).Should().Be(GmudStepStatus.Simulated);
        result.Manifest.GetStepStatus(10).Should().Be(GmudStepStatus.Pending);
    }

    [Fact]
    public async Task SemConfirmWrite_ParaAntesDaPrimeiraEscrita()
    {
        var result = await RunAsync();

        result.Outcome.Should().Be(GmudRunOutcome.ConfirmationRequired);
        result.StoppedAtStep.Should().Be(7);
        result.ExitCode.Should().Be(3);
        _repos.Writes.Should().BeEmpty();
        result.Manifest.GetStepStatus(6).Should().Be(GmudStepStatus.Completed);
    }

    [Fact]
    public async Task Cenario6e7_BranchCriadaDeProducaoEMergeSemConflito_DiffContemSoArquivosDaUserStory()
    {
        var result = await RunAsync(confirm: true);

        _repos.Writes.Should().Contain($"create {Web} feature/us_{GmudId} {_prodCommit}");
        _repos.Writes.Should().NotContain(w => w.StartsWith($"create {Api}"), "repositório sem alteração mapeada não recebe branch");

        var web = result.Manifest.Repositories[Web];
        web.Merges.Single().Status.Should().Be(MergeStatus.Ok);
        web.ChangedFiles.Should().Equal("/src/Tela.cs");
        _repos.FileAt(Web, _repos.BranchHead(Web, $"feature/us_{CurrentGmudId}")!, "/src/Tela.cs").Should().Be("v1-us");
        _repos.Writes.Should().Contain(w => w.StartsWith($"update {Web} feature/us_{GmudId}"));

        // Database: só anexo de script, sem branch da US → nada é escrito no repositório; o script fica para a infra.
        result.Manifest.Database!.Branches.Single().Status.Should().Be(DatabaseBranchStatus.ScriptsOnly);
        _repos.Writes.Should().NotContain(w => w.Contains(Database), "a automação não cria branch, merge nem release no Database");

        // A release de produção desta GMUD já existe (é a que está em produção) em outro commit: não é sobrescrita.
        result.Outcome.Should().Be(GmudRunOutcome.Blocked);
        result.StoppedAtStep.Should().Be(10);
        result.Message.Should().Contain("não é sobrescrita");
        _repos.BranchHead(Web, "release/" + ProdRelease).Should().Be(_prodCommit);
        _repos.Writes.Should().NotContain(w => w.Contains("release/GMUD_GMUD"), "nenhuma release é criada enquanto outra estiver em conflito");
    }

    [Fact]
    public async Task Cenario8_ConflitoAmbiguo_ParaGeraRelatorioENaoCriaRelease()
    {
        // A feature saiu de develop: traz um commit de outra user story no MESMO arquivo da US.
        var develop = _repos.Commit(Web, _prodCommit, "Task #99999: outra feature", ("/src/Tela.cs", "v1-develop"));
        var feature = _repos.Commit(Web, develop, $"Task #{TaskId}: nova tela", ("/src/Tela.cs", "v1-develop-us"));
        _repos.SetBranch(Web, $"feature/us_{UserStoryId}", feature);

        var result = await RunAsync(confirm: true);

        result.Outcome.Should().Be(GmudRunOutcome.Blocked);
        result.StoppedAtStep.Should().Be(9);
        var merge = result.Manifest.Repositories[Web].Merges.Single();
        merge.Status.Should().Be(MergeStatus.AwaitingApproval);
        merge.Conflicts.Should().ContainSingle(c => c.Path == "/src/Tela.cs" && c.Resolution == ConflictResolution.Ambiguous);
        File.Exists(Path.Combine(_workspace, "gmuds", GmudId.ToString(), "conciliacao", $"{Web}_us_{UserStoryId}.md")).Should().BeTrue();
        _repos.Writes.Should().NotContain(w => w.Contains(" release/"));
        _repos.FileAt(Web, _repos.BranchHead(Web, $"feature/us_{CurrentGmudId}")!, "/src/Tela.cs").Should().Be("v1", "nada da feature entra sem aprovação");
    }

    [Fact]
    public async Task Cenario8_DecisaoDoOperadorNoManifesto_RetomaEAplicaAVersaoAprovada()
    {
        var develop = _repos.Commit(Web, _prodCommit, "Task #99999: outra feature", ("/src/Develop.cs", "fora"), ("/src/Tela.cs", "v1-develop"));
        var feature = _repos.Commit(Web, develop, $"Task #{TaskId}: nova tela", ("/src/Tela.cs", "v1-develop-us"));
        _repos.SetBranch(Web, $"feature/us_{UserStoryId}", feature);
        UseGmudWithoutReleaseCollision();

        var blocked = await RunAsync(confirm: true);
        blocked.StoppedAtStep.Should().Be(9);

        var manifest = Store.Load(CurrentGmudId)!;
        manifest.Repositories[Web].Merges.Single().Conflicts.Single(c => c.Path == "/src/Tela.cs").Decision = "feature";
        Store.Save(manifest);

        var resumed = await RunAsync(confirm: true);

        resumed.Manifest.Repositories[Web].Merges.Single().Status.Should().Be(MergeStatus.Reconciled);
        var head = _repos.BranchHead(Web, $"feature/us_{CurrentGmudId}")!;
        _repos.FileAt(Web, head, "/src/Tela.cs").Should().Be("v1-develop-us");
        _repos.FileAt(Web, head, "/src/Develop.cs").Should().BeNull("arquivo só de commits fora do escopo não é levado");
    }

    [Fact]
    public async Task Cenario9_Reexecucao_RetomaDaEtapaPendenteSemDuplicarBranchesNemSobrescreverReleases()
    {
        UseGmudWithoutReleaseCollision();
        var first = await RunAsync(confirm: true, confirmation: new ScriptedConfirmation(true, false));
        first.Outcome.Should().Be(GmudRunOutcome.Cancelled);
        first.StoppedAtStep.Should().Be(10);

        // Manifesto com etapas 1 a 7 concluídas: a nova execução retoma da 8.
        var manifest = Store.Load(CurrentGmudId)!;
        manifest.SetStepStatus(8, GmudStepStatus.Pending);
        manifest.SetStepStatus(9, GmudStepStatus.Pending);
        Store.Save(manifest);
        var writesBefore = _repos.Writes.Count;

        var second = await RunAsync(confirm: true);

        second.Manifest.Executions.Where(e => e.DryRun == false).Select(e => e.Step).Should().StartWith([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        second.Manifest.Executions.Skip(first.Manifest.Executions.Count).First().Step.Should().Be(8);
        _repos.Writes.Skip(writesBefore).Should().NotContain(w => w.Contains($"feature/us_{CurrentGmudId}"), "branch e merges já existentes não são refeitos");
        _repos.Writes.Skip(writesBefore).Count(w => w.StartsWith("create") && w.Contains("release/")).Should().Be(2, "duas releases no WEB; o Database não recebe release");

        var releasesBefore = _repos.Writes.Count;
        var third = await RunAsync(confirm: true, fromStep: 10);

        third.Manifest.GetStepStatus(10).Should().Be(GmudStepStatus.Completed);
        _repos.Writes.Skip(releasesBefore).Should().BeEmpty("releases existentes no mesmo commit são reaproveitadas");
    }

    [Fact]
    public async Task Cenario10_EntregaParaAInfra_GeraInfraMdComTagAtualNovaTagEScripts()
    {
        UseGmudWithoutReleaseCollision();
        var cancelled = await RunAsync(confirm: true, confirmation: new ScriptedConfirmation(true, true));
        cancelled.StoppedAtStep.Should().Be(11, "sem builds o acompanhamento fica pendente");
        cancelled.Outcome.Should().Be(GmudRunOutcome.Blocked);

        var head = _repos.BranchHead(Web, "release/GMUD_PROD_CONTOSO_04112026")!;
        _repos.AddBuild("release/GMUD_PROD_CONTOSO_04112026", new BuildInfo(31002, "31002", "completed", "succeeded", "web-ci", "refs/heads/release/GMUD_PROD_CONTOSO_04112026", head, null));
        _repos.AddBuild("release/GMUD_GMUD_CONTOSO_04112026", new BuildInfo(31003, "31003", "completed", "succeeded", "web-ci", "refs/heads/release/GMUD_GMUD_CONTOSO_04112026", head, null));

        var result = await RunAsync(confirm: true);

        result.Outcome.Should().Be(GmudRunOutcome.Completed);
        var build = result.Manifest.Repositories[Web].Builds["prd"];
        build.BuildId.Should().Be(31002);
        build.NewTag.Should().Be($"31002-GMUD_PROD_CONTOSO_04112026-{head}");
        build.Image.Should().Be($"contosoregistry.azurecr.io/contoso.portalcliente.web:31002-GMUD_PROD_CONTOSO_04112026-{head}");

        var infra = File.ReadAllText(Path.Combine(_workspace, "gmuds", "25949", "infra.md"));
        infra.Should().Contain("contoso-portalcliente-web-prd-contoso")
            .And.Contain($"30945-{ProdRelease}-{_prodCommit}")
            .And.Contain($"31002-GMUD_PROD_CONTOSO_04112026-{head}")
            .And.Contain($"31003-GMUD_GMUD_CONTOSO_04112026-{head}")
            .And.Contain($"1. `gmuds/25949/scripts/{ScriptName}`");
    }

    [Fact]
    public async Task UserStorySemBranchNoRepositorioDeCodigo_BloqueiaORepositorio()
    {
        _boards.AddComment(TaskId, "Também altera a API.");
        _appServices.Images["contoso-portalcliente-api-prd-contoso"] = $"contosoregistry.azurecr.io/contoso.portalcliente.api:30946-{ProdRelease}-{_repos.BranchHead(Api, "main")}";
        _repos.SetBranch(Api, "release/" + ProdRelease, _repos.BranchHead(Api, "main")!);

        var result = await RunAsync(confirm: true);

        result.Outcome.Should().Be(GmudRunOutcome.Blocked);
        result.StoppedAtStep.Should().Be(8);
        result.Manifest.Repositories[Api].Merges.Single().Status.Should().Be(MergeStatus.MissingBranch);
        result.Message.Should().Contain($"feature/us_{UserStoryId} não existe em {Api}");
    }

    [Fact]
    public async Task Database_AlteracaoDeProcedureNoCard_UsaSoABranchDoDesenvolvedorSemEscreverNoRepositorio()
    {
        _boards.AddComment(TaskId, "Alteração da procedure de consulta (Database).");
        var developerCommit = _repos.Commit(Database, _repos.BranchHead(Database, "main"), $"Task #{TaskId}: procedure", ("/Procedures/Consulta.sql", "v2"));
        _repos.SetBranch(Database, $"feature/us_{UserStoryId}", developerCommit);
        UseGmudWithoutReleaseCollision();

        var result = await RunAsync(confirm: true, confirmation: new ScriptedConfirmation(true, true));

        var branch = result.Manifest.Database!.Branches.Single();
        branch.Required.Should().BeTrue();
        branch.Status.Should().Be(DatabaseBranchStatus.Changed);
        branch.CommitsAhead.Should().Be(1);
        branch.Commit.Should().Be(developerCommit);
        result.Manifest.Repositories.Should().NotContainKey(Database);
        _repos.Writes.Should().NotContain(w => w.Contains(Database));
        File.ReadAllText(Path.Combine(_workspace, "gmuds", "25949", "infra.md")).Should().Contain($"[feature/us_{UserStoryId}]({DatabaseBranchUrl}) | {developerCommit[..8]} | 1 | alterada");
    }

    private const string DatabaseBranchUrl =
        "https://dev.azure.com/contoso/Contoso%20Repositorios/_git/Contoso.Legado.Database?version=GBfeature%2Fus_20611";

    [Fact]
    public async Task Database_FeatureComCommitSemCitacaoNoCard_EDetectadaPelaBranchComLink()
    {
        var developerCommit = _repos.Commit(Database, _repos.BranchHead(Database, "main"), "procedure de consulta", ("/Procedures/Consulta.sql", "v2"));
        _repos.SetBranch(Database, $"feature/us_{UserStoryId}", developerCommit);

        var result = await RunAsync(dryRun: true);

        var branch = result.Manifest.Database!.Branches.Single();
        branch.Required.Should().BeFalse("o card não cita o Database — a alteração vem da branch");
        branch.Status.Should().Be(DatabaseBranchStatus.Changed);
        branch.Url.Should().Be(DatabaseBranchUrl);
        branch.BaseBranch.Should().Be("main");
        branch.CommitsAhead.Should().Be(1);
        _repos.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Database_FeatureSemCommit_RegistraAviso()
    {
        _repos.SetBranch(Database, $"feature/us_{UserStoryId}", _repos.BranchHead(Database, "main")!);

        var result = await RunAsync(dryRun: true);

        result.Manifest.Database!.Branches.Single().Status.Should().Be(DatabaseBranchStatus.NoCommits);
        result.Manifest.Warnings.Should().Contain(w => w.Contains($"feature/us_{UserStoryId} existe, mas não tem commit à frente de main"));
    }

    [Fact]
    public async Task Database_UserStorySemBranchNemScript_NaoAlteraOBanco()
    {
        _boards.Add(TaskId, "Task", "Implementar tela", "<p>Alterações (WEB) na tela de consulta.</p>");

        var result = await RunAsync(dryRun: true);

        result.Manifest.Database!.Branches.Single().Status.Should().Be(DatabaseBranchStatus.NotChanged);
        File.Exists(Path.Combine(_workspace, "gmuds", GmudId.ToString(), "scripts", ScriptName)).Should().BeFalse();
    }

    [Fact]
    public async Task Database_GmudSoDeBanco_SemRepositorioDeCodigo_SeguePelaBranchDoDesenvolvedor()
    {
        _boards.Add(TaskId, "Task", "Ajustar procedure", "<p>Ajuste de procedure.</p>");
        var developerCommit = _repos.Commit(Database, _repos.BranchHead(Database, "main"), "procedure", ("/Procedures/Consulta.sql", "v2"));
        _repos.SetBranch(Database, $"feature/us_{UserStoryId}", developerCommit);

        var result = await RunAsync(dryRun: true);

        result.Outcome.Should().Be(GmudRunOutcome.DryRunCompleted);
        result.Manifest.Repositories.Should().BeEmpty();
        result.Manifest.Database!.Branches.Single().Status.Should().Be(DatabaseBranchStatus.Changed);
    }

    [Fact]
    public async Task SemCodigoESemAlteracaoNoBanco_BloqueiaNaEtapa6()
    {
        _boards.Add(TaskId, "Task", "Ajustar texto", "<p>Sem projeto citado.</p>");

        var result = await RunAsync(dryRun: true);

        result.Outcome.Should().Be(GmudRunOutcome.Blocked);
        result.StoppedAtStep.Should().Be(6);
        result.Message.Should().Contain("Nenhuma alteração encontrada");
    }

    [Fact]
    public async Task Database_AlteracaoDeProcedureSemBranch_BloqueiaNaEtapa6AntesDeQualquerEscrita()
    {
        _boards.AddComment(TaskId, "Alteração da procedure de consulta (Database).");

        var result = await RunAsync(confirm: true);

        result.Outcome.Should().Be(GmudRunOutcome.Blocked);
        result.StoppedAtStep.Should().Be(6);
        result.Message.Should().Contain($"feature/us_{UserStoryId} não existe");
        result.Manifest.Database!.Branches.Single().Status.Should().Be(DatabaseBranchStatus.Missing);
        _repos.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task ScriptForaDaNomenclatura_EBaixadoComAviso()
    {
        var script = _boards.AddAttachment("script_final.txt", "ALTER TABLE X;");
        _boards.Add(TaskId, "Task", "Implementar tela", "<p>Alterações (WEB) na tela de consulta.</p>", script);

        var result = await RunAsync(dryRun: true);

        result.Manifest.UserStories.Single().Items.Single().DatabaseAttachments.Should().Equal($"gmuds/{GmudId}/scripts/script_final.txt");
        result.Manifest.Warnings.Should().Contain(w => w.Contains("script_final.txt") && w.Contains($"US{UserStoryId}_SCRIPT_TASK{TaskId}.txt"));
    }

    [Fact]
    public async Task ConfirmacaoInterativa_PerguntaAntesDaPrimeiraEscritaEAntesDasReleases()
    {
        UseGmudWithoutReleaseCollision();
        var confirmation = new ScriptedConfirmation(true, true);

        await RunAsync(confirm: true, confirmation: confirmation);

        confirmation.AskedAtSteps.Should().Equal(7, 10);
    }

    /// <summary>Uma GMUD de outra data (25949, 04-11-2026), cuja release ainda não existe — caminho sem colisão de nomes.</summary>
    private void UseGmudWithoutReleaseCollision()
    {
        _boards.Add(25949, "User Story", "GMUD 04-11-2026 - Contoso - Implantação", $"<p>Aplicar #{UserStoryId}</p>");
        _inputOverride = "25949";
    }

    private string _inputOverride = string.Empty;

    private int CurrentGmudId => _inputOverride.Length == 0 ? GmudId : int.Parse(_inputOverride);

    private Task<GmudRunResult> RunAsync(bool dryRun = false, bool confirm = false, int? fromStep = null, IOperatorConfirmation? confirmation = null) =>
        RunCoreAsync(dryRun, confirm, fromStep, _inputOverride, confirmation);

    private static GmudConfig WithOverride(GmudConfig config, string siteName) => new()
    {
        Organization = config.Organization,
        BoardsProject = config.BoardsProject,
        ReposProject = config.ReposProject,
        Repositories = config.Repositories,
        Build = config.Build,
        Clients =
        [
            new ClientConfig("Contoso", "CONTOSO", "contoso", new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [Web] = new Dictionary<string, string> { ["prd"] = siteName }
            })
        ]
    };
}
