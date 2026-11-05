using System.Net;
using BacklogAutomation.Core.Models;
using BacklogAutomation.Core.Providers.Trello;
using FluentAssertions;

namespace BacklogAutomation.Tests.Providers;

public class TrelloProviderTests
{
    private readonly RoutingHttpMessageHandler _handler = new();
    private int _nextId = 1;

    public TrelloProviderTests()
    {
        _handler
            .On(HttpMethod.Get, "boards/AbCd1234?", HttpStatusCode.OK, """{"id":"board-1","name":"Loja"}""")
            .On(HttpMethod.Get, "boards/board-1/lists", HttpStatusCode.OK, """[{"id":"list-backlog","name":"Backlog"},{"id":"list-doing","name":"Doing"}]""")
            .On(HttpMethod.Get, "boards/board-1/labels", HttpStatusCode.OK, """[{"id":"label-epic","name":"Epic"}]""")
            .On(HttpMethod.Post, "labels", _ => Created($$"""{"id":"label-{{_nextId++}}"}"""))
            .On(HttpMethod.Post, "cards", _ =>
            {
                var id = $"card-{_nextId++}";
                return Created($$"""{"id":"{{id}}","shortUrl":"https://trello.com/c/{{id}}"}""");
            })
            .On(HttpMethod.Post, "/attachments", _ => Created("""{"id":"att"}"""))
            .On(HttpMethod.Post, "checklists", _ => Created($$"""{"id":"checklist-{{_nextId++}}"}"""))
            .On(HttpMethod.Post, "/checkItems", _ => Created($$"""{"id":"item-{{_nextId++}}"}"""));
    }

    private static HttpResponseMessage Created(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private TrelloProvider CreateSut() => new(new HttpClient(_handler), new TrelloOptions
    {
        ApiKey = "fake-key",
        Token = "fake-token",
        BoardId = "AbCd1234"
    });

    [Fact]
    public async Task CreateAsync_UserStory_CriaCardNaListaComPontosNoNomeELinkParaOPai()
    {
        var sut = CreateSut();
        var feature = new CreatedWorkItem(WorkItemKind.Feature, "Carrinho", "card-feature", "https://trello.com/c/feature", "card", []);

        var created = await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.UserStory,
            Title = "Adicionar item",
            Statement = "Como cliente, quero adicionar itens, para que eu compre depois",
            AcceptanceCriteria = ["Dado X, quando Y, então Z"],
            Estimate = new Estimate { StoryPoints = 5 },
            Parent = feature,
            EpicTitle = "Pedidos",
            FeatureTitle = "Carrinho"
        });

        created.Url.Should().StartWith("https://trello.com/c/card-");
        var card = _handler.PostsTo("cards").First(r => !r.Url.Contains("/attachments")).Json;
        card.GetProperty("idList").GetString().Should().Be("list-backlog");
        card.GetProperty("name").GetString().Should().Be("(5) Adicionar item");
        card.GetProperty("desc").GetString().Should().Contain("**Critérios de aceite**").And.Contain("- Dado X, quando Y, então Z");

        _handler.PostsTo("/attachments").Single().Json.GetProperty("url").GetString().Should().Be("https://trello.com/c/feature");
        _handler.PostsTo("checklists").First().Json.GetProperty("idCard").GetString().Should().Be("card-feature");
        _handler.PostsTo("checklists").First().Json.GetProperty("name").GetString().Should().Be("User Stories");
        _handler.PostsTo("labels").Select(r => r.Json.GetProperty("name").GetString())
            .Should().BeEquivalentTo("User Story", "Epic: Pedidos", "Feature: Carrinho");
    }

    [Fact]
    public async Task CreateAsync_TaskEBug_ViramItensDeChecklistNoCardDaUserStoryComAsHoras()
    {
        var sut = CreateSut();
        var story = new CreatedWorkItem(WorkItemKind.UserStory, "US", "card-us", "https://trello.com/c/us", "card", []);

        await sut.CreateAsync(new WorkItemCreationRequest
        {
            Kind = WorkItemKind.Task, Title = "Criar endpoint", Parent = story,
            Estimate = new Estimate { OriginalEstimateHours = 6, RemainingHours = 6 }
        });
        await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Task, Title = "Criar tela", Parent = story });
        var bug = await sut.CreateAsync(new WorkItemCreationRequest { Kind = WorkItemKind.Bug, Title = "Total errado", Parent = story });

        var checklists = _handler.PostsTo("checklists").Where(r => !r.Url.Contains("checkItems")).Select(r => r.Json.GetProperty("name").GetString());
        checklists.Should().Equal("Tasks", "Bugs");
        _handler.PostsTo("/checkItems").Select(r => r.Json.GetProperty("name").GetString())
            .Should().Equal("Criar endpoint (Horas estimadas: 6h, Horas restantes: 6h)", "Criar tela", "Total errado");
        bug.Url.Should().Be("https://trello.com/c/us");
        _handler.PostsTo("cards").Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_ListaInexistente_CriaALista()
    {
        _handler.On(HttpMethod.Get, "boards/board-1/lists", HttpStatusCode.OK, """[{"id":"list-doing","name":"Doing"}]""");
        _handler.On(HttpMethod.Post, "lists", _ => Created("""{"id":"list-new"}"""));

        await CreateSut().PrepareAsync();

        _handler.PostsTo("lists").Single().Json.GetProperty("name").GetString().Should().Be("Backlog");
    }

    [Fact]
    public async Task Requisicoes_AutenticamPorHeaderSemExporCredenciaisNaUrl()
    {
        await CreateSut().PrepareAsync();

        _handler.Requests.Should().NotContain(r => r.Url.Contains("fake-token") || r.Url.Contains("fake-key"));
    }
}
