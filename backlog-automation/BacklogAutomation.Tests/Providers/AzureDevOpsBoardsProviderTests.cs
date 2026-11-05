using System.Net;
using System.Text.Json;
using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Providers.AzureDevOps;
using FluentAssertions;

namespace BacklogAutomation.Tests.Providers;

public class AzureDevOpsBoardsProviderTests
{
    private const string AgileTypes = """{"value":[{"name":"Epic"},{"name":"Feature"},{"name":"User Story"},{"name":"Task"},{"name":"Bug"}]}""";
    private const string ScrumTypes = """{"value":[{"name":"Epic"},{"name":"Feature"},{"name":"Product Backlog Item"},{"name":"Task"},{"name":"Bug"}]}""";
    private const string BasicTypes = """{"value":[{"name":"Epic"},{"name":"Issue"},{"name":"Task"}]}""";

    private readonly RoutingHttpMessageHandler _handler = new();
    private int _nextId = 500;

    public AzureDevOpsBoardsProviderTests()
    {
        _handler.On(HttpMethod.Post, "_apis/wit/workitems/$", _ =>
        {
            var id = _nextId++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id":{{id}},"_links":{"html":{"href":"https://dev.azure.com/org/Proj/_workitems/edit/{{id}}"} } }""")
            };
        });
    }

    private AzureDevOpsBoardsProvider CreateSut(string workItemTypes, AzureDevOpsProcess? process = null, Dictionary<string, string>? fieldMap = null)
    {
        _handler.On(HttpMethod.Get, "_apis/wit/workitemtypes?", HttpStatusCode.OK, workItemTypes);
        return new AzureDevOpsBoardsProvider(new HttpClient(_handler), new AzureDevOpsBoardsOptions
        {
            Organization = "org",
            Project = "Meu Projeto",
            PersonalAccessToken = "fake-pat-for-tests",
            Process = process,
            IterationPath = "Meu Projeto\\Sprint 1",
            CustomFieldMap = fieldMap ?? new Dictionary<string, string>()
        });
    }

    private void FieldsOf(string type, params string[] referenceNames) =>
        _handler.On(HttpMethod.Get, $"workitemtypes/{type}/fields", HttpStatusCode.OK,
            JsonSerializer.Serialize(new { value = referenceNames.Select(name => new { referenceName = name }) }));

    private static Dictionary<string, JsonElement> PatchFields(RecordedRequest request) =>
        request.Json.EnumerateArray()
            .Where(op => op.GetProperty("path").GetString()!.StartsWith("/fields/"))
            .ToDictionary(op => op.GetProperty("path").GetString()!["/fields/".Length..], op => op.GetProperty("value"));

    [Fact]
    public async Task CreateAsync_ProcessoAgile_CriaUserStoryComStoryPointsCriteriosEVinculoAoPai()
    {
        var sut = CreateSut(AgileTypes);
        var parent = new CreatedWorkItem(WorkItemKind.Feature, "Carrinho", "42", "https://x/42", "Feature", []);

        var created = await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.UserStory,
            Title = "Adicionar item",
            Statement = "Como cliente, quero adicionar itens, para que eu compre depois",
            AcceptanceCriteria = ["Dado X, quando Y, então Z"],
            Estimate = new Estimate { StoryPoints = 5 },
            Priority = 2,
            Tags = ["mvp", "backlog-automation"],
            Parent = parent
        });

        created.Id.Should().Be("500");
        created.ProviderType.Should().Be("User Story");
        created.Url.Should().EndWith("/_workitems/edit/500");

        var post = _handler.PostsTo("_apis/wit/workitems/$User Story").Single();
        post.ContentType.Should().Be("application/json-patch+json");
        var fields = PatchFields(post);
        fields["System.Title"].GetString().Should().Be("Adicionar item");
        fields["Microsoft.VSTS.Scheduling.StoryPoints"].GetDecimal().Should().Be(5);
        fields["Microsoft.VSTS.Common.AcceptanceCriteria"].GetString().Should().Be("<ul><li>Dado X, quando Y, então Z</li></ul>");
        fields["System.Description"].GetString().Should().Contain("Como cliente, quero adicionar itens");
        fields["Microsoft.VSTS.Common.Priority"].GetInt32().Should().Be(2);
        fields["System.Tags"].GetString().Should().Be("mvp; backlog-automation");
        fields["System.IterationPath"].GetString().Should().Be("Meu Projeto\\Sprint 1");

        var relation = post.Json.EnumerateArray().Single(op => op.GetProperty("path").GetString() == "/relations/-").GetProperty("value");
        relation.GetProperty("rel").GetString().Should().Be("System.LinkTypes.Hierarchy-Reverse");
        relation.GetProperty("url").GetString().Should().Be("https://dev.azure.com/org/_apis/wit/workItems/42");
    }

    [Fact]
    public async Task CreateAsync_ProcessoScrumDetectado_CriaProductBacklogItemComEffort()
    {
        var sut = CreateSut(ScrumTypes);

        await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.UserStory,
            Title = "Agendar consulta",
            Estimate = new Estimate { StoryPoints = 8 }
        });

        sut.Template!.Process.Should().Be(AzureDevOpsProcess.Scrum);
        var fields = PatchFields(_handler.PostsTo("$Product Backlog Item").Single());
        fields["Microsoft.VSTS.Scheduling.Effort"].GetDecimal().Should().Be(8);
        fields.Should().NotContainKey("Microsoft.VSTS.Scheduling.StoryPoints");
    }

    [Fact]
    public async Task CreateAsync_TaskComHoras_PreencheOriginalRemainingECompletedWork()
    {
        var sut = CreateSut(AgileTypes);

        await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.Task,
            Title = "Criar endpoint",
            Activity = "Desenvolvimento",
            Estimate = new Estimate { OriginalEstimateHours = 6, RemainingHours = 4, CompletedHours = 2 }
        });

        var fields = PatchFields(_handler.PostsTo("$Task").Single());
        fields["Microsoft.VSTS.Scheduling.OriginalEstimate"].GetDecimal().Should().Be(6);
        fields["Microsoft.VSTS.Scheduling.RemainingWork"].GetDecimal().Should().Be(4);
        fields["Microsoft.VSTS.Scheduling.CompletedWork"].GetDecimal().Should().Be(2);
        fields["Microsoft.VSTS.Common.Activity"].GetString().Should().Be("Development");
    }

    [Fact]
    public async Task CreateAsync_TaskSemOriginalEstimate_UsaRemainingWorkSemAvisoESemDescricao()
    {
        var sut = CreateSut(ScrumTypes);
        // Processo herdado em que a Task não tem Original Estimate (só Remaining Work).
        FieldsOf("Task", "System.Title", "System.Description", "System.Tags", "System.IterationPath", "Microsoft.VSTS.Scheduling.RemainingWork");

        var created = await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.Task,
            Title = "Criar tela",
            Estimate = new Estimate { OriginalEstimateHours = 8, RemainingHours = 8 }
        });

        var fields = PatchFields(_handler.PostsTo("$Task").Single());
        fields.Should().NotContainKey("Microsoft.VSTS.Scheduling.OriginalEstimate");
        fields["Microsoft.VSTS.Scheduling.RemainingWork"].GetDecimal().Should().Be(8);
        fields.Should().NotContainKey("System.Description");
        created.Warnings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(WorkItemKind.Epic, "$Epic")]
    [InlineData(WorkItemKind.Feature, "$Feature")]
    [InlineData(WorkItemKind.UserStory, "$Product Backlog Item")]
    [InlineData(WorkItemKind.Bug, "$Bug")]
    public async Task CreateAsync_HorasEmCardSemOriginalEstimate_VaoParaEffortENuncaParaADescricao(WorkItemKind kind, string typeInUrl)
    {
        var sut = CreateSut(ScrumTypes);
        var typeName = typeInUrl.TrimStart('$');
        FieldsOf(typeName, "System.Title", "System.Description", "System.Tags", "System.IterationPath",
            "Microsoft.VSTS.Scheduling.Effort", "Microsoft.VSTS.Scheduling.RemainingWork");

        var created = await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = kind,
            Title = "Item",
            Description = "Contexto do item.",
            Estimate = new Estimate { OriginalEstimateHours = 25 }
        });

        var fields = PatchFields(_handler.PostsTo(typeInUrl).Single());
        fields["Microsoft.VSTS.Scheduling.Effort"].GetDecimal().Should().Be(25);
        fields["System.Description"].GetString().Should().Be("<div>Contexto do item.</div>");
        created.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_CardComOriginalEstimate_PrefereOriginalEstimateAoEffort()
    {
        var sut = CreateSut(ScrumTypes);
        FieldsOf("Product Backlog Item", "System.Title", "Microsoft.VSTS.Scheduling.Effort", "Microsoft.VSTS.Scheduling.OriginalEstimate");

        await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.UserStory,
            Title = "US",
            Estimate = new Estimate { StoryPoints = 5, OriginalEstimateHours = 12 }
        });

        var fields = PatchFields(_handler.PostsTo("$Product Backlog Item").Single());
        fields["Microsoft.VSTS.Scheduling.Effort"].GetDecimal().Should().Be(5, "story points ocupam o Effort no Scrum");
        fields["Microsoft.VSTS.Scheduling.OriginalEstimate"].GetDecimal().Should().Be(12);
    }

    [Fact]
    public async Task CreateAsync_MedidaSemCampo_GeraAvisoSemEscreverNaDescricao()
    {
        var sut = CreateSut(ScrumTypes);
        FieldsOf("Product Backlog Item", "System.Title", "System.Description", "Microsoft.VSTS.Scheduling.Effort");
        var estimate = new Estimate { StoryPoints = 5, OriginalEstimateHours = 12 };

        var created = await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.UserStory, Title = "US", Estimate = estimate });

        var fields = PatchFields(_handler.PostsTo("$Product Backlog Item").Single());
        fields["Microsoft.VSTS.Scheduling.Effort"].GetDecimal().Should().Be(5);
        fields.Should().NotContainKey("System.Description");
        created.Warnings.Should().ContainSingle().Which.Should().Contain("Horas estimadas").And.Contain("não registrado");
    }

    [Fact]
    public async Task CreateAsync_MedidaLivreMapeada_VaiParaOCampoCustomizado()
    {
        var sut = CreateSut(AgileTypes, fieldMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Pontos de função"] = "Custom.PontosDeFuncao" });
        var estimate = new Estimate();
        estimate.OtherMeasures["Pontos de função"] = "12";
        estimate.OtherMeasures["Complexidade"] = "Alta";

        await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.UserStory, Title = "US", Estimate = estimate });

        var fields = PatchFields(_handler.PostsTo("$User Story").Single());
        fields["Custom.PontosDeFuncao"].GetDecimal().Should().Be(12);
        fields.Should().NotContainKey("System.Description");
    }

    [Fact]
    public async Task CreateAsync_ValorDeNegocio_UsaCampoNativoBusinessValue()
    {
        var sut = CreateSut(AgileTypes);
        var estimate = new Estimate();
        estimate.OtherMeasures["Valor de negócio"] = "80";

        await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Epic, Title = "Epic", Estimate = estimate });

        PatchFields(_handler.PostsTo("$Epic").Single())["Microsoft.VSTS.Common.BusinessValue"].GetDecimal().Should().Be(80);
    }

    [Fact]
    public async Task CreateAsync_BugNoProcessoAgile_UsaReproStepsEStoryPoints()
    {
        var sut = CreateSut(AgileTypes);

        await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.Bug,
            Title = "Total errado",
            ReproSteps = ["Adicionar dois itens", "Remover um"],
            ExpectedResult = "Total recalculado",
            Estimate = new Estimate { StoryPoints = 1, OriginalEstimateHours = 2 }
        });

        var fields = PatchFields(_handler.PostsTo("$Bug").Single());
        fields["Microsoft.VSTS.TCM.ReproSteps"].GetString().Should().Contain("<ol><li>Adicionar dois itens</li><li>Remover um</li></ol>").And.Contain("Total recalculado");
        fields["Microsoft.VSTS.Scheduling.StoryPoints"].GetDecimal().Should().Be(1);
        fields["Microsoft.VSTS.Scheduling.OriginalEstimate"].GetDecimal().Should().Be(2);
    }

    [Fact]
    public async Task CreateAsync_ProcessoBasic_FeatureViraEpicComTagEBugViraTaskComTag()
    {
        var sut = CreateSut(BasicTypes);

        await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Feature, Title = "Feature", Tags = ["x"] });
        await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.UserStory, Title = "US", Estimate = new Estimate { StoryPoints = 3 } });
        await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Bug, Title = "Bug" });

        PatchFields(_handler.PostsTo("$Epic").Single())["System.Tags"].GetString().Should().Be("x; Feature");
        PatchFields(_handler.PostsTo("$Issue").Single())["Microsoft.VSTS.Scheduling.Effort"].GetDecimal().Should().Be(3);
        PatchFields(_handler.PostsTo("$Task").Single())["System.Tags"].GetString().Should().Be("Bug");
    }

    [Fact]
    public async Task PrepareAsync_ProcessoInformado_NaoConsultaOsTiposDoProjeto()
    {
        var sut = CreateSut(AgileTypes, AzureDevOpsProcess.Cmmi);

        await sut.PrepareAsync();

        sut.Template!.StoryType.Should().Be("Requirement");
        _handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ErroDaApi_LancaHttpRequestExceptionComAMensagemDoAzure()
    {
        var sut = CreateSut(AgileTypes);
        _handler.On(HttpMethod.Post, "$Epic", HttpStatusCode.BadRequest, """{"message":"TF401320: Rule Error for field Area Path."}""");

        var act = () => sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Epic, Title = "Epic" });

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("*TF401320*");
    }
}
