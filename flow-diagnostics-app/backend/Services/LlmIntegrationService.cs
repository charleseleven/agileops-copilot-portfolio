using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgilePredict.Models.Configuration;
using AgilePredict.Models.DTOs;
using AgilePredict.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace AgilePredict.Services
{
    /// <summary>
    /// Implementação do serviço de integração com API LLM
    /// </summary>
    public class LlmIntegrationService : ILlmIntegrationService
    {
        private readonly HttpClient _httpClient;
        private readonly LlmConfiguration _configuration;
        private readonly ILogger<LlmIntegrationService> _logger;

        public LlmIntegrationService(
            HttpClient httpClient,
            IOptions<LlmConfiguration> configuration,
            ILogger<LlmIntegrationService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Configurar headers de autenticação
            ConfigureHttpClient();
        }

        /// <summary>
        /// Configura o HttpClient com headers de autenticação e timeout
        /// </summary>
        private void ConfigureHttpClient()
        {
            // BaseAddress já configurado no Program.cs via DI
            // Apenas configurar headers adicionais se necessário
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _configuration.ApiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Envia um prompt simples para a LLM
        /// </summary>
        public async Task<LlmResponse> SendPromptAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _logger.LogWarning("Tentativa de enviar prompt vazio");
                return new LlmResponse
                {
                    Success = false,
                    ErrorMessage = "O prompt não pode ser vazio"
                };
            }

            var request = new LlmRequest
            {
                Prompt = prompt,
                Model = _configuration.DefaultModel
            };

            return await SendPromptWithOptionsAsync(request, cancellationToken);
        }

        /// <summary>
        /// Envia um prompt com opções avançadas para a LLM
        /// </summary>
        public async Task<LlmResponse> SendPromptWithOptionsAsync(
            LlmRequest request,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Enviando prompt para LLM API: {Model}", request.Model ?? _configuration.DefaultModel);

                // Construir payload para a API da LLM (formato OpenAI)
                var messages = new List<object>();
                if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
                {
                    messages.Add(new { role = "system", content = request.SystemPrompt });
                }
                messages.Add(new { role = "user", content = request.Prompt });

                object payload = request.Tools is { Count: > 0 }
                    ? new
                    {
                        model = request.Model ?? _configuration.DefaultModel,
                        messages,
                        temperature = request.Temperature,
                        max_tokens = request.MaxTokens,
                        tools = request.Tools
                    }
                    : new
                    {
                        model = request.Model ?? _configuration.DefaultModel,
                        messages,
                        temperature = request.Temperature,
                        max_tokens = request.MaxTokens
                    };

                var jsonContent = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Fazer requisição com retry logic
                HttpResponseMessage response = null!;
                Exception? lastException = null;

                for (int attempt = 1; attempt <= _configuration.MaxRetries; attempt++)
                {
                    try
                    {
                        response = await _httpClient.PostAsync("chat/completions", httpContent, cancellationToken);

                        if (response.IsSuccessStatusCode)
                            break;

                        _logger.LogWarning(
                            "Tentativa {Attempt}/{MaxRetries} falhou com status {StatusCode}",
                            attempt, _configuration.MaxRetries, response.StatusCode);
                    }
                    catch (Exception ex) when (attempt < _configuration.MaxRetries)
                    {
                        lastException = ex;
                        _logger.LogWarning(ex,
                            "Tentativa {Attempt}/{MaxRetries} falhou com exceção",
                            attempt, _configuration.MaxRetries);

                        // Aguardar antes de tentar novamente (exponential backoff)
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                    }
                }

                if (response == null || !response.IsSuccessStatusCode)
                {
                    var errorMessage = lastException?.Message ?? 
                        $"Falha após {_configuration.MaxRetries} tentativas. Status: {response?.StatusCode}";

                    _logger.LogError("Falha ao comunicar com LLM API: {Error}", errorMessage);

                    return new LlmResponse
                    {
                        Success = false,
                        ErrorMessage = errorMessage
                    };
                }

                // Processar resposta
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                var llmApiResponse = JsonSerializer.Deserialize<LlmApiResponse>(responseContent, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (llmApiResponse?.Choices == null || llmApiResponse.Choices.Length == 0)
                {
                    _logger.LogError("Resposta da LLM API inválida ou vazia");
                    return new LlmResponse
                    {
                        Success = false,
                        ErrorMessage = "Resposta da API inválida"
                    };
                }

                var message = llmApiResponse.Choices[0].Message;
                var content = message?.Content ?? string.Empty;

                var toolCalls = message?.ToolCalls?
                    .Where(tc => tc.Function != null)
                    .Select(tc => new LlmToolCall
                    {
                        Id = tc.Id ?? string.Empty,
                        Name = tc.Function!.Name ?? string.Empty,
                        ArgumentsJson = tc.Function!.Arguments ?? "{}"
                    })
                    .ToList();

                _logger.LogInformation(
                    "Resposta recebida da LLM com sucesso. Tokens: {Tokens}. ToolCalls: {ToolCallCount}",
                    llmApiResponse.Usage?.TotalTokens, toolCalls?.Count ?? 0);

                return new LlmResponse
                {
                    Success = true,
                    Content = content,
                    Model = llmApiResponse.Model,
                    TokensUsed = llmApiResponse.Usage?.TotalTokens,
                    ToolCalls = toolCalls is { Count: > 0 } ? toolCalls : null
                };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Timeout ao comunicar com LLM API");
                return new LlmResponse
                {
                    Success = false,
                    ErrorMessage = "Timeout na comunicação com a API"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro inesperado ao comunicar com LLM API");
                return new LlmResponse
                {
                    Success = false,
                    ErrorMessage = $"Erro inesperado: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Valida se a conexão com a LLM está funcional
        /// </summary>
        public async Task<bool> ValidateConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Validando conexão com LLM API");

                var response = await SendPromptAsync("Teste de conexão. Responda apenas: OK", cancellationToken);

                var isValid = response.Success && !string.IsNullOrWhiteSpace(response.Content);

                _logger.LogInformation("Validação de conexão: {Status}", isValid ? "Sucesso" : "Falha");

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao validar conexão com LLM API");
                return false;
            }
        }

        #region Classes auxiliares para deserialização da resposta da API

        private class LlmApiResponse
        {
            public string? Model { get; set; }
            public Choice[]? Choices { get; set; }
            public Usage? Usage { get; set; }
        }

        private class Choice
        {
            public Message? Message { get; set; }
        }

        private class Message
        {
            public string? Content { get; set; }

            [JsonPropertyName("tool_calls")]
            public ToolCall[]? ToolCalls { get; set; }
        }

        private class ToolCall
        {
            public string? Id { get; set; }
            public FunctionCall? Function { get; set; }
        }

        private class FunctionCall
        {
            public string? Name { get; set; }
            public string? Arguments { get; set; }
        }

        private class Usage
        {
            [JsonPropertyName("total_tokens")]
            public int TotalTokens { get; set; }
        }

        #endregion
    }
}
