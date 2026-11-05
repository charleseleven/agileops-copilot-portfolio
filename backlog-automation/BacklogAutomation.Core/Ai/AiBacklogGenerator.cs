using System.Globalization;
using System.Text.Json;
using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Ai;

/// <summary>
/// Converte um documento de requisitos em texto livre (ou uma solicitação via prompt) num
/// <see cref="BacklogPlan"/> usando a LLM, com um system prompt que codifica as boas práticas de mercado
/// (INVEST, narrativa "Como/Quero/Para que", critérios de aceite em Gherkin, tasks de até 1 dia) e a regra
/// central do pedido: as estimativas do documento são copiadas fielmente — nunca inventadas, a menos que o
/// usuário peça sugestões explicitamente (<see cref="AiGenerationOptions.SuggestMissingEstimates"/>).
/// </summary>
public sealed class AiBacklogGenerator : IAiBacklogGenerator
{
    // ~100k caracteres ≈ 25-30k tokens de entrada — folga confortável para o contexto do gpt-oss-120b,
    // deixando espaço para a resposta (o backlog em JSON costuma ter 2-4x o tamanho do documento).
    public const int MaxSourceCharacters = 100_000;

    private readonly ILlmClient _llmClient;

    public AiBacklogGenerator(ILlmClient llmClient)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
    }

    public async Task<BacklogPlan> GenerateAsync(string sourceText, AiGenerationOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            throw new ArgumentException("O documento/prompt está vazio.", nameof(sourceText));
        }

        if (sourceText.Length > MaxSourceCharacters)
        {
            throw new ArgumentException(
                $"O documento tem {sourceText.Length:N0} caracteres (limite: {MaxSourceCharacters:N0}). Divida-o por Epic/módulo e gere em partes.",
                nameof(sourceText));
        }

        var systemPrompt = BuildSystemPrompt(options);
        var userPrompt = BuildUserPrompt(sourceText, options);

        var json = await _llmClient.CompleteJsonAsync(systemPrompt, userPrompt, cancellationToken);
        var plan = ParsePlan(json);

        if (options.SuggestMissingEstimates)
        {
            foreach (var item in plan.EnumerateAll().Where(item => !item.Estimate.IsEmpty && item.Estimate.Source is null))
            {
                item.Estimate.Source = Estimate.SourceDocument;
            }
        }
        else
        {
            foreach (var item in plan.EnumerateAll())
            {
                item.Estimate.Source = item.Estimate.IsEmpty ? null : Estimate.SourceDocument;
            }
        }

        return plan;
    }

    internal static BacklogPlan ParsePlan(string json)
    {
        var cleaned = json.Trim();

        // Alguns modelos ignoram o response_format e embrulham o JSON em ```json ... ``` ou em texto.
        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new LlmException("A LLM não retornou um JSON de backlog válido.");
        }

        cleaned = cleaned[start..(end + 1)];

        try
        {
            return BacklogJson.Deserialize(cleaned);
        }
        catch (JsonException ex)
        {
            throw new LlmException($"O JSON retornado pela LLM não segue o schema do backlog: {ex.Message}");
        }
    }

    internal static string BuildSystemPrompt(AiGenerationOptions options)
    {
        var hoursPerDay = options.HoursPerDay.ToString(CultureInfo.InvariantCulture);
        var estimateRule = options.SuggestMissingEstimates
            ? $"""
              - Quando o documento NÃO informar a estimativa de um item, SUGIRA uma (story points na escala de
                Fibonacci para User Stories; horas para Tasks/Bugs) e marque "source": "ai-suggested" nesse item.
                Estimativas informadas no documento têm "source": "document" e NUNCA são alteradas.
              """
            : """
              - NUNCA invente estimativas. Se o documento não informar a estimativa de um item, deixe os campos
                de "estimate" ausentes/null para esse item.
              """;

        return $$"""
            Você é um Product Owner / Agile Coach sênior especialista em refinamento de backlog (Scrum, Kanban,
            SAFe) e em Azure DevOps, Jira e Trello. Sua tarefa é transformar o documento ou a solicitação do
            usuário em um backlog hierárquico pronto para o time de desenvolvimento:
            Epic → Feature → User Story → Task / Bug.

            BOAS PRÁTICAS OBRIGATÓRIAS
            - Epic: objetivo de negócio amplo (pode durar vários sprints/releases). Descreva o valor de negócio.
            - Feature: capacidade entregável que agrega valor ao usuário, cabível em uma release/PI, com
              critérios de aceite de alto nível quando o documento permitir.
            - User Story: siga INVEST (Independente, Negociável, Valiosa, Estimável, Pequena, Testável) e cabível
              em uma sprint. "statement" no formato "Como <persona>, quero <ação>, para que <benefício>".
              Critérios de aceite verificáveis, preferencialmente em Gherkin ("Dado ..., quando ..., então ...").
            - Task e Bug: crie SOMENTE os que o documento/solicitação listar explicitamente. NUNCA decomponha
              User Stories em Tasks por conta própria nem invente Bugs — se o documento não trouxer Tasks/Bugs,
              deixe "tasks" e "bugs" vazios. Horas por disciplina dentro de uma User Story (ex: "BE 11 · FE 6")
              NÃO são Tasks: são a estimativa da própria história.
            - Task (quando informada): título iniciando com verbo no infinitivo.
              "activity" ∈ Development | Testing | Design | Documentation | Deployment | Requirements.
            - Bug (quando informado): passos para reproduzir, resultado esperado e resultado atual.
            - Títulos concisos (até 120 caracteres), sem prefixos como "US:" ou "Task:", no idioma do documento.
            - Toda User Story deve estar dentro de uma Feature, e toda Feature dentro de um Epic. Se o documento
              não nomear Epics/Features, agrupe as histórias de forma coerente e dê nomes de negócio a eles.
            - Não invente escopo que não esteja no documento. Lacunas ou dúvidas relevantes devem ir na
              "description" do item como "Pendência: ...".
            - Prioridade (quando informada ou claramente inferível de MoSCoW/criticidade): 1 = crítica/Must,
              2 = alta/Should, 3 = média/Could, 4 = baixa/Won't.

            ESTIMATIVAS (REGRA CRÍTICA)
            - Copie EXATAMENTE as estimativas informadas no documento para o item correspondente:
              story points/pontos/esforço → "storyPoints"; horas (de qualquer nível: Epic, Feature, User Story,
              Task ou Bug) → "originalEstimateHours"; quando o documento trouxer várias horas para o mesmo item
              (ex: com/sem IA, com/sem contingência), siga as instruções adicionais do usuário para escolher;
              horas restantes → "remainingHours"; horas concluídas → "completedHours";
              dias → converta para horas usando {{hoursPerDay}}h por dia; tamanho camiseta (PP/P/M/G/GG,
              XS/S/M/L/XL) → "size"; QUALQUER outra medida (pontos de função, complexidade, valor de negócio,
              WSJF, risco...) → "otherMeasures" com o rótulo original como chave.
            {{estimateRule}}

            FORMATO DE SAÍDA
            Responda SOMENTE com um objeto JSON válido (sem markdown, sem comentários) neste schema:
            {
              "epics": [{
                "title": "string", "description": "string|null", "tags": ["string"], "priority": 1,
                "estimate": { "storyPoints": null, "size": null, "otherMeasures": {} },
                "features": [{
                  "title": "string", "description": "string|null", "tags": [], "priority": null,
                  "acceptanceCriteria": ["string"],
                  "estimate": { "storyPoints": null },
                  "userStories": [{
                    "title": "string",
                    "statement": "Como <persona>, quero <ação>, para que <benefício>",
                    "description": "string|null", "tags": [], "priority": null,
                    "acceptanceCriteria": ["Dado ..., quando ..., então ..."],
                    "estimate": { "storyPoints": 5, "source": "document" },
                    "tasks": [{
                      "title": "string", "description": "string|null", "activity": "Development",
                      "estimate": { "originalEstimateHours": 4, "remainingHours": 4 }
                    }],
                    "bugs": [{
                      "title": "string", "description": "string|null",
                      "reproSteps": ["string"], "expectedResult": "string", "actualResult": "string",
                      "estimate": { "originalEstimateHours": 2 }
                    }]
                  }]
                }]
              }]
            }
            """;
    }

    internal static string BuildUserPrompt(string sourceText, AiGenerationOptions options)
    {
        var context = string.IsNullOrWhiteSpace(options.AdditionalInstructions)
            ? string.Empty
            : $"\nInstruções adicionais do usuário: {options.AdditionalInstructions}\n";

        var origin = options.SourceName is null ? "solicitação do usuário" : $"documento \"{options.SourceName}\"";

        return $"""
            Gere o backlog a partir do {origin} abaixo.{context}
            <<<INICIO
            {sourceText}
            FIM>>>
            """;
    }
}

public interface IAiBacklogGenerator
{
    Task<BacklogPlan> GenerateAsync(string sourceText, AiGenerationOptions options, CancellationToken cancellationToken = default);
}

public sealed record AiGenerationOptions
{
    public decimal HoursPerDay { get; init; } = 8m;

    public bool SuggestMissingEstimates { get; init; }

    /// <summary>Nome do arquivo de origem (null quando a entrada é um prompt).</summary>
    public string? SourceName { get; init; }

    public string? AdditionalInstructions { get; init; }
}
