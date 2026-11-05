using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BacklogAutomation.Core.Models;

/// <summary>Serialização do <see cref="BacklogPlan"/> — mesma configuração para o arquivo revisado pelo PO e para a resposta da IA.</summary>
public static class BacklogJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static BacklogPlan Deserialize(string json) =>
        JsonSerializer.Deserialize<BacklogPlan>(json, Options)
        ?? throw new JsonException("O JSON do backlog está vazio.");

    public static string Serialize(BacklogPlan plan) => JsonSerializer.Serialize(plan, Options);
}
