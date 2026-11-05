using System.Net;
using System.Text.Json;

namespace BacklogAutomation.Tests;

/// <summary>
/// HttpMessageHandler de teste: responde por (método, trecho da URL) e grava cada requisição com o corpo,
/// para os testes conferirem exatamente o que seria enviado às APIs do Azure DevOps/Jira/Trello.
/// </summary>
internal sealed class RoutingHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(HttpMethod Method, string UrlFragment, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _routes = [];

    public List<RecordedRequest> Requests { get; } = [];

    public RoutingHttpMessageHandler On(HttpMethod method, string urlFragment, HttpStatusCode status, string body) =>
        On(method, urlFragment, _ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    public RoutingHttpMessageHandler On(HttpMethod method, string urlFragment, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes.Add((method, urlFragment, respond));
        return this;
    }

    public IEnumerable<RecordedRequest> PostsTo(string urlFragment) =>
        Requests.Where(request => request.Method == HttpMethod.Post && request.Url.Contains(urlFragment, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
        Requests.Add(new RecordedRequest(request.Method, url, body, request.Content?.Headers.ContentType?.MediaType));

        // A rota registrada por último vence — permite sobrescrever um padrão genérico num teste específico.
        for (var i = _routes.Count - 1; i >= 0; i--)
        {
            var route = _routes[i];
            if (route.Method == request.Method && url.Contains(route.UrlFragment, StringComparison.Ordinal))
            {
                return route.Respond(request);
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"Rota não configurada no teste: {request.Method} {url}") };
    }
}

internal sealed record RecordedRequest(HttpMethod Method, string Url, string? Body, string? ContentType)
{
    public JsonElement Json => JsonDocument.Parse(Body!).RootElement;
}
