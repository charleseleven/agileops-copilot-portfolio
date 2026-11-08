using System.Text.Json;
using AgilePredict.Models.DTOs.Flow;
using AgilePredict.Models.DTOs.SprintRisk;
using AgilePredict.Services.Interfaces;

namespace AgilePredict.Services
{
    /// <summary>
    /// Envia a análise de risco à LLM com o System Prompt dinâmico (<see cref="ISprintRiskPromptBuilder"/>)
    /// e devolve o parecer estruturado (resumo + recomendações).
    /// </summary>
    public sealed class SprintRiskInsightGenerator : ISprintRiskInsightGenerator
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly ILlmIntegrationService _llm;
        private readonly ISprintRiskPromptBuilder _promptBuilder;
        private readonly ILogger<SprintRiskInsightGenerator> _logger;

        public SprintRiskInsightGenerator(
            ILlmIntegrationService llm,
            ISprintRiskPromptBuilder promptBuilder,
            ILogger<SprintRiskInsightGenerator> logger)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<FlowInsightResult> GenerateAsync(SprintRiskAnalysis analysis, CancellationToken cancellationToken = default)
        {
            var response = await _llm.SendPromptWithOptionsAsync(_promptBuilder.Build(analysis), cancellationToken);
            if (!response.Success || string.IsNullOrWhiteSpace(response.Content))
            {
                _logger.LogWarning("LLM não gerou o parecer de risco da sprint: {Error}", response.ErrorMessage);
                return new FlowInsightResult { Success = false, ErrorMessage = response.ErrorMessage ?? "A LLM não retornou conteúdo" };
            }

            var content = response.Content.Trim();
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<InsightPayload>(content[start..(end + 1)], JsonOptions);
                    if (!string.IsNullOrWhiteSpace(parsed?.Summary))
                    {
                        return new FlowInsightResult
                        {
                            Success = true,
                            Summary = parsed.Summary.Trim(),
                            Recommendations = parsed.Recommendations?.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList() ?? new()
                        };
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Parecer de risco fora do formato JSON; usando o texto bruto");
                }
            }

            // A LLM não seguiu o formato: usa o texto como resumo, sem recomendações estruturadas.
            return new FlowInsightResult { Success = true, Summary = content, Recommendations = new() };
        }

        private sealed class InsightPayload
        {
            public string? Summary { get; set; }
            public List<string>? Recommendations { get; set; }
        }
    }
}
