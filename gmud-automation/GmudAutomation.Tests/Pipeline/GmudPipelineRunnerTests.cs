using FluentAssertions;
using GmudAutomation.Core.Pipeline;
using GmudAutomation.Core.Pipeline.Configuration;
using GmudAutomation.Core.Pipeline.Manifest;
using GmudAutomation.Core.Services;
using Moq;

namespace GmudAutomation.Tests.Pipeline;

public sealed class GmudPipelineRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gmud-runner-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IGmudLogger> _logger = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class FakeStep(string key, bool writes, bool supportsDryRun = true, Exception? error = null) : IGmudStep
    {
        public int Calls { get; private set; }
        public bool? LastDryRun { get; private set; }
        public string Key => key;
        public string Name => key;
        public bool WritesRemote => writes;
        public bool SupportsDryRun => supportsDryRun;

        public Task<GmudManifest> ExecuteAsync(GmudManifest manifest, GmudStepContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastDryRun = context.DryRun;
            return error is null ? Task.FromResult(manifest) : throw error;
        }
    }

    private GmudPipelineRunner CreateSut(params IGmudStep[] steps) => new(steps, new FileGmudManifestStore(_root), new FlagOnlyOperatorConfirmation(), "gmuds");

    private static GmudRunRequest Request(bool dryRun = false, int? fromStep = null, bool confirm = false) => new("1", 1, dryRun, fromStep, confirm);

    [Fact]
    public async Task RunAsync_DryRun_EtapasDeEscritaFicamSimuladasEAsSemSuporteSaoPuladas()
    {
        var read = new FakeStep("ler", writes: false);
        var write = new FakeStep("escrever", writes: true);
        var release = new FakeStep("release", writes: true, supportsDryRun: false);

        var result = await CreateSut(read, write, release).RunAsync(Request(dryRun: true), _logger.Object);

        result.Outcome.Should().Be(GmudRunOutcome.DryRunCompleted);
        result.Manifest.GetStepStatus(1).Should().Be(GmudStepStatus.Completed);
        result.Manifest.GetStepStatus(2).Should().Be(GmudStepStatus.Simulated);
        result.Manifest.GetStepStatus(3).Should().Be(GmudStepStatus.Pending);
        write.LastDryRun.Should().BeTrue();
        release.Calls.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_SemConfirmacao_NaoExecutaEtapaDeEscrita()
    {
        var write = new FakeStep("escrever", writes: true);

        var result = await CreateSut(new FakeStep("ler", false), write).RunAsync(Request(), _logger.Object);

        result.Outcome.Should().Be(GmudRunOutcome.ConfirmationRequired);
        result.ExitCode.Should().Be(3);
        write.Calls.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_EtapaDeEscritaAnteriorSoSimulada_RecusaPularDireto()
    {
        var sut = CreateSut(new FakeStep("a", true), new FakeStep("b", true));
        await sut.RunAsync(Request(dryRun: true), _logger.Object);

        var result = await sut.RunAsync(Request(fromStep: 2, confirm: true), _logger.Object);

        result.Outcome.Should().Be(GmudRunOutcome.Failed);
        result.Message.Should().Contain("--from-step 1");
    }

    [Fact]
    public async Task RunAsync_EtapaBloqueada_GravaStatusEParaComCodigo2()
    {
        var after = new FakeStep("depois", false);
        var sut = CreateSut(new FakeStep("bloqueia", false, error: new GmudBlockedException("escolha o App Service")), after);

        var result = await sut.RunAsync(Request(), _logger.Object);

        result.Outcome.Should().Be(GmudRunOutcome.Blocked);
        result.ExitCode.Should().Be(2);
        result.Manifest.GetStepStatus(1).Should().Be(GmudStepStatus.Blocked);
        result.Manifest.Executions.Single().Message.Should().Be("escolha o App Service");
        after.Calls.Should().Be(0);
        new FileGmudManifestStore(_root).Load(1)!.GetStepStatus(1).Should().Be(GmudStepStatus.Blocked);
    }

    [Fact]
    public async Task RunAsync_RetomaDaPrimeiraEtapaNaoConcluida()
    {
        var first = new FakeStep("a", false);
        var second = new FakeStep("b", false, error: new InvalidOperationException("falha"));
        var sut = CreateSut(first, second);
        await sut.RunAsync(Request(), _logger.Object);

        var retry = new FakeStep("b", false);
        var resumed = await CreateSut(first, retry).RunAsync(Request(), _logger.Object);

        resumed.Outcome.Should().Be(GmudRunOutcome.Completed);
        first.Calls.Should().Be(1, "a etapa 1 já estava concluída");
        retry.Calls.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_FromStepForaDoIntervalo_Falha()
    {
        var result = await CreateSut(new FakeStep("a", false)).RunAsync(Request(fromStep: 5), _logger.Object);

        result.Outcome.Should().Be(GmudRunOutcome.Failed);
    }

    [Fact]
    public void TryParseInput_AceitaIdLinkEHashtag()
    {
        var parser = new AzureBoardsUrlParser();

        GmudPipelineRunner.TryParseInput("25482", parser, out var id1, out _).Should().BeTrue();
        GmudPipelineRunner.TryParseInput("#25482", parser, out var id2, out _).Should().BeTrue();
        GmudPipelineRunner.TryParseInput("https://dev.azure.com/contoso/Projetos/_workitems/edit/25482/", parser, out var id3, out var reference).Should().BeTrue();
        GmudPipelineRunner.TryParseInput("abc", parser, out _, out _).Should().BeFalse();

        new[] { id1, id2, id3 }.Should().AllBeEquivalentTo(25482);
        reference!.Organization.Should().Be("contoso");
    }

    [Fact]
    public void Registry_EtapaDesconhecidaNaConfiguracao_FalhaComMensagemClara()
    {
        var registry = new GmudStepRegistry([new FakeStep("a", false)]);

        var act = () => registry.Resolve(["a", "inexistente"]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*inexistente*");
    }

    [Fact]
    public void Registry_PermiteReordenarERemoverEtapasPorConfiguracao()
    {
        var registry = new GmudStepRegistry([new FakeStep("a", false), new FakeStep("b", false), new FakeStep("c", false)]);

        registry.Resolve(["c", "a"]).Select(s => s.Key).Should().Equal("c", "a");
    }

    [Fact]
    public void ManifestStore_RoundTrip_PreservaChavesDaEspecificacaoEDicionariosSemDiferenciarMaiusculas()
    {
        var store = new FileGmudManifestStore(_root);
        var manifest = new GmudManifest { Gmud = { Id = 7, Title = "GMUD 22-10-2026 - X - Y", Date = "22102026", Client = "X" } };
        manifest.Repositories["Contoso.Web"] = new RepositoryEntry { Kind = "Web" };
        manifest.SetStepStatus(1, GmudStepStatus.Completed);
        store.Save(manifest);

        var json = File.ReadAllText(Path.Combine(_root, "7", "manifest.json"));
        json.Should().Contain("\"gmud\"").And.Contain("\"titulo\"").And.Contain("\"repositorios\"").And.Contain("\"etapas\"").And.Contain("\"avisos\"");

        var loaded = store.Load(7)!;
        loaded.Repositories.ContainsKey("CONTOSO.WEB").Should().BeTrue();
        loaded.GetStepStatus(1).Should().Be(GmudStepStatus.Completed);
    }

    [Fact]
    public void GmudConfig_ExemploVersionado_CarregaEResolveAs11Etapas()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "gmud.config.example.json")))
        {
            directory = directory.Parent;
        }

        var config = GmudConfig.LoadFromFile(Path.Combine(directory!.FullName, "gmud.config.example.json"));
        var steps = GmudPipelineFactory.CreateSteps(config, Mock.Of<Core.Client.IAzureDevOpsClient>(), Mock.Of<Core.Client.IAzureReposClient>(), Mock.Of<Core.Client.IAppServiceClient>());

        steps.Select(s => s.Key).Should().Equal(GmudStepKeys.Default);
        config.Repositories.Should().HaveCount(4);
        config.Clients[1].AppServiceOverrides!["Contoso.PortalCliente.WEB"]["gmud"].Should().Be("contoso-web-homolog-cli2");
    }

    [Fact]
    public void GmudConfig_Validate_RepositorioDuplicado_Falha()
    {
        var config = new GmudConfig
        {
            Organization = "contoso",
            BoardsProject = "P",
            Repositories = [new RepositoryConfig("A", Core.Models.RepositoryKind.Web), new RepositoryConfig("a", Core.Models.RepositoryKind.Api)]
        };

        var act = config.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*\"A\"*mais de uma vez*");
    }
}
