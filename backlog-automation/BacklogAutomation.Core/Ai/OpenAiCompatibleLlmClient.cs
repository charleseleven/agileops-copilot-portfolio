using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BacklogAutomation.Core.Ai;

/// <summary>
/// Abstrai a chamada à LLM para que a geração do backlog seja testável sem rede e para que o provedor
/// (Groq, OpenAI, Azure OpenAI, Ollama...) possa ser trocado só por configuração.
/// </summary>
public interface ILlmClient
{
    /// <summary>Envia o par system/user e retorna o conteúdo JSON gerado pela LLM.</summary>
    Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

public sealed class LlmClientOptions
{
    public const string DefaultApiUrl = "https://api.groq.com/openai/v1/";
    public const string DefaultModel = "openai/gpt-oss-120b";

    public string ApiUrl { get; init; } = DefaultApiUrl;

    /// <summary>Chave de API — deve vir de variável de ambiente (LLM_API_KEY/GROQ_API_KEY), nunca hardcoded.</summary>
    public required string ApiKey { get; init; }

    public string Model { get; init; } = DefaultModel;

    public int MaxTokens { get; init; } = 16384;

    /// <summary>Baixa por padrão: a tarefa é de extração/estruturação fiel ao documento, não de criatividade.</summary>
    public double Temperature { get; init; } = 0.2;
}

/// <summary>Cliente para qualquer API compatível com o endpoint <c>chat/completions</c> da OpenAI (padrão: Groq, igual ao Flow Diagnostics).</summary>
public sealed class OpenAiCompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly LlmClientOptions _options;

    public OpenAiCompatibleLlmClient(HttpClient httpClient, LlmClientOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        _httpClient.BaseAddress ??= new Uri(_options.ApiUrl.EndsWith('/') ? _options.ApiUrl : $"{_options.ApiUrl}/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        if (_httpClient.Timeout == TimeSpan.FromSeconds(100))
        {
            // Documentos grandes geram respostas longas — o timeout padrão do HttpClient (100s) é curto demais.
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }
    }

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = _options.Model,
            temperature = _options.Temperature,
            max_tokens = _options.MaxTokens,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await _httpClient.PostAsync("chat/completions", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmException($"A LLM respondeu HTTP {(int)response.StatusCode}: {Truncate(body, 500)}");
        }

        using var document = JsonDocument.Parse(body);
        var choice = document.RootElement.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var finishReason) && finishReason.GetString() == "length")
        {
            throw new LlmException(
                "A resposta da LLM foi truncada (limite de tokens). Divida o documento em partes menores ou aumente LLM_MAX_TOKENS.");
        }

        return choice.GetProperty("message").GetProperty("content").GetString()
            ?? throw new LlmException("A LLM retornou uma resposta vazia.");
    }

    private static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : $"{text[..maxLength]}...";
}

public sealed class LlmException(string message) : Exception(message);
