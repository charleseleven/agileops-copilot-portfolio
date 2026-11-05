using System.Text.Json;
using AgilePredict.Models.DTOs;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Services.Interfaces;

namespace AgilePredict.Services
{
    /// <summary>
    /// Implementação da Task #214: constrói o prompt de sistema com as regras de negócio ágeis,
    /// envia o payload estatístico (Task #213) para a LLM já configurada (<see cref="ILlmIntegrationService"/>)
    /// e traduz a resposta em um diagnóstico estruturado (resumo + sugestões de mitigação).
    /// </summary>
    public class FlowInsightGenerator : IFlowInsightGenerator
    {
        private const string SystemPrompt = """
            Você é um assistente especializado em Scrum e métricas de fluxo (Kanban) que atua como
            consultor de um Scrum Master. Você recebe um payload JSON com o Cycle Time, Lead Time e
            vazão semanal de uma sprint, além de alertas de gargalo por status e responsável.

            Regras:
            - Analise os cards com "isCurrentStatusBottleneck": true e "isCycleTimeAboveBaseline": true como prioridade.
            - Seja específico: cite o título do card e o responsável (assignedTo) quando recomendar uma ação.
            - Sugira ações corretivas concretas e realistas (ex.: redistribuição de carga, pareamento, quebra de tarefa, revisão de prioridade).
            - Nunca invente dados que não estejam no payload.

            Responda ESTRITAMENTE em JSON, sem markdown e sem texto fora do JSON, no formato:
            {"summary": "<diagnóstico geral em até 3 frases>", "recommendations": ["<sugestão 1>", "<sugestão 2>"]}
            """;

        private readonly ILlmIntegrationService _llmService;
        private readonly ILogger<FlowInsightGenerator> _logger;

        public FlowInsightGenerator(ILlmIntegrationService llmService, ILogger<FlowInsightGenerator> logger)
        {
            _llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<FlowInsightResult> GenerateAsync(FlowMetricsPayload payload, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(payload);

            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var request = new LlmRequest
            {
                Prompt = payloadJson,
                SystemPrompt = SystemPrompt,
                Temperature = 0.3,
                MaxTokens = 700
            };

            var response = await _llmService.SendPromptWithOptionsAsync(request, cancellationToken);

            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning("Falha ao gerar insight de fluxo via LLM: {Error}", response.ErrorMessage);
                return new FlowInsightResult
                {
                    Success = false,
                    ErrorMessage = response.ErrorMessage ?? "A LLM não retornou conteúdo"
                };
            }

            return ParseInsight(response.Content);
        }

        private FlowInsightResult ParseInsight(string content)
        {
            var jsonPayload = ExtractJsonObject(content);

            try
            {
                var parsed = JsonSerializer.Deserialize<LlmInsightPayload>(
                    jsonPayload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Summary))
                {
                    return new FlowInsightResult
                    {
                        Success = true,
                        Summary = parsed.Summary,
                        Recommendations = parsed.Recommendations ?? new List<string>()
                    };
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Resposta da LLM não estava no formato JSON esperado; usando fallback textual");
            }

            // Fallback: a LLM não seguiu o formato JSON solicitado. Trata o texto bruto como resumo
            // e tenta extrair recomendações de linhas em formato de lista.
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var recommendations = lines
                .Where(l => l.StartsWith('-') || l.StartsWith('*') || System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+[\.\)]"))
                .Select(l => l.TrimStart('-', '*', ' ').Trim())
                .Where(l => l.Length > 0)
                .ToList();

            return new FlowInsightResult
            {
                Success = true,
                Summary = content.Trim(),
                Recommendations = recommendations
            };
        }

        private static string ExtractJsonObject(string content)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            return start >= 0 && end > start ? content[start..(end + 1)] : content;
        }

        private sealed class LlmInsightPayload
        {
            public string? Summary { get; set; }
            public List<string>? Recommendations { get; set; }
        }
    }
}
