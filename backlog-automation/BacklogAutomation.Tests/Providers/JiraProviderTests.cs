using System.Net;
using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Providers.Jira;
using FluentAssertions;

namespace BacklogAutomation.Tests.Providers;

public class JiraProviderTests
{
    private const string ProjectWithInitiative = """
        {"id":"10000","key":"LOJA","issueTypes":[
          {"id":"1","name":"Initiative","hierarchyLevel":2},
          {"id":"2","name":"Epic","hierarchyLevel":1},
          {"id":"3","name":"Story","hierarchyLevel":0},
          {"id":"4","name":"Bug","hierarchyLevel":0},
          {"id":"5","name":"Task","hierarchyLevel":0},
          {"id":"6","name":"Sub-task","hierarchyLevel":-1,"subtask":true}
        ]}
        """;

    private const string ProjectInPortugueseWithoutInitiative = """
        {"id":"10000","key":"LOJA","issueTypes":[
          {"id":"10","name":"Tarefa","hierarchyLevel":0},
          {"id":"11","name":"Épico","hierarchyLevel":1},
          {"id":"12","name":"História","hierarchyLevel":0},
          {"id":"13","name":"Subtarefa","hierarchyLevel":-1,"subtask":true}
        ]}
        """;

    private readonly RoutingHttpMessageHandler _handler = new();
    private int _nextKey = 1;

    public JiraProviderTests()
    {
        _handler.On(HttpMethod.Post, "rest/api/2/issue", _ =>
            new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent($$"""{"id":"9","key":"LOJA-{{_nextKey++}}"}""") });
    }

    private JiraProvider CreateSut(string project)
    {
        _handler.On(HttpMethod.Get, "rest/api/2/project/LOJA", HttpStatusCode.OK, project);
        return new JiraProvider(new HttpClient(_handler), new JiraOptions
        {
            BaseUrl = "https://empresa.atlassian.net",
            Email = "po@empresa.com",
            ApiToken = "fake-token",
            ProjectKey = "LOJA"
        });
    }

    private JsonFields LastIssueFields() => new(_handler.PostsTo("rest/api/2/issue").Last().Json.GetProperty("fields"));

    private sealed record JsonFields(System.Text.Json.JsonElement Root)
    {
        public System.Text.Json.JsonElement this[string name] => Root.GetProperty(name);

        public bool Has(string name) => Root.TryGetProperty(name, out _);
    }

    [Fact]
    public async Task CreateAsync_HierarquiaComInitiative_MapeiaCadaNivelPeloHierarchyLevel()
    {
        var sut = CreateSut(ProjectWithInitiative);

        var epic = await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Epic, Title = "Gestão de pedidos" });
        LastIssueFields()["issuetype"].GetProperty("id").GetString().Should().Be("1");

        var feature = await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Feature, Title = "Carrinho", Parent = epic });
        LastIssueFields()["issuetype"].GetProperty("id").GetString().Should().Be("2");
        LastIssueFields()["parent"].GetProperty("key").GetString().Should().Be("LOJA-1");

        var story = await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.UserStory,
            Title = "Adicionar item",
            Estimate = new Estimate { StoryPoints = 5 },
            Priority = 2,
            Parent = feature
        });
        var storyFields = LastIssueFields();
        storyFields["issuetype"].GetProperty("id").GetString().Should().Be("3");
        storyFields["customfield_10016"].GetDecimal().Should().Be(5);
        storyFields["priority"].GetProperty("name").GetString().Should().Be("High");
        story.Url.Should().Be("https://empresa.atlassian.net/browse/LOJA-3");

        await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Bug, Title = "Total errado", Parent = story });
        var bugFields = LastIssueFields();
        bugFields["issuetype"].GetProperty("id").GetString().Should().Be("6", "Bug filho de Story precisa ser sub-task no Jira");
        bugFields["labels"].EnumerateArray().Select(l => l.GetString()).Should().Contain("bug");
    }

    [Fact]
    public async Task CreateAsync_TaskComHoras_PreencheTimeTrackingNoFormatoDoJira()
    {
        var sut = CreateSut(ProjectWithInitiative);
        var story = new CreatedWorkItem(WorkItemKind.UserStory, "US", "LOJA-3", null, "Story", []);

        await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.Task,
            Title = "Criar endpoint",
            Estimate = new Estimate { OriginalEstimateHours = 1.5m, RemainingHours = 1.5m },
            Parent = story
        });

        var timeTracking = LastIssueFields()["timetracking"];
        timeTracking.GetProperty("originalEstimate").GetString().Should().Be("1h 30m");
        timeTracking.GetProperty("remainingEstimate").GetString().Should().Be("1h 30m");
    }

    [Fact]
    public async Task CreateAsync_ProjetoSemNivelAcimaDeEpic_EpicViraLabelNosDescendentes()
    {
        var sut = CreateSut(ProjectInPortugueseWithoutInitiative);

        var epic = await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Epic, Title = "Gestão de Pedidos" });

        epic.IsVirtual.Should().BeTrue();
        _handler.PostsTo("rest/api/2/issue").Should().BeEmpty();

        await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.Feature,
            Title = "Carrinho",
            Parent = epic,
            EpicTitle = "Gestão de Pedidos",
            Tags = ["backlog-automation"]
        });

        var fields = LastIssueFields();
        fields["issuetype"].GetProperty("id").GetString().Should().Be("11");
        fields.Has("parent").Should().BeFalse();
        fields["labels"].EnumerateArray().Select(l => l.GetString()).Should().Equal("backlog-automation", "epic-gestao-de-pedidos");

        await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.UserStory, Title = "US" });
        LastIssueFields()["issuetype"].GetProperty("id").GetString().Should().Be("12", "\"História\" é preferida a \"Tarefa\" no nível 0");
    }

    [Fact]
    public async Task CreateAsync_CampoForaDaTelaDeCriacao_RegistraNaDescricao()
    {
        var sut = CreateSut(ProjectWithInitiative);
        _handler.On(HttpMethod.Get, "createmeta/LOJA/issuetypes/3", HttpStatusCode.OK,
            """{"fields":[{"fieldId":"summary"},{"fieldId":"description"},{"fieldId":"labels"},{"fieldId":"parent"}]}""");

        var created = await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.UserStory,
            Title = "US",
            Estimate = new Estimate { StoryPoints = 8 }
        });

        var fields = LastIssueFields();
        fields.Has("customfield_10016").Should().BeFalse();
        fields["description"].GetString().Should().Contain("*Estimativas*").And.Contain("Story Points: 8");
        created.Warnings.Should().ContainSingle();
    }

    [Theory]
    [InlineData(4, "4h")]
    [InlineData(0.5, "30m")]
    [InlineData(2.25, "2h 15m")]
    [InlineData(0, "0m")]
    public void ToJiraDuration_ConverteHorasDecimais(double hours, string expected)
    {
        JiraProvider.ToJiraDuration((decimal)hours).Should().Be(expected);
    }
}
