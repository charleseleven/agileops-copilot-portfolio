using System.Text.Json.Serialization;

namespace BacklogAutomation.Core.Models;

/// <summary>
/// Estimativa de um item, exatamente como informada no documento/prompt. Story Points (ou Effort/Size,
/// conforme o processo da ferramenta de destino) e horas têm campos próprios; qualquer outra medida
/// (pontos de função, complexidade, valor de negócio...) vai para <see cref="OtherMeasures"/> e é
/// mapeada para um campo customizado (<c>--field-map</c>) ou registrada na descrição do card.
/// </summary>
public sealed class Estimate
{
    public const string SourceDocument = "document";
    public const string SourceAiSuggested = "ai-suggested";

    public decimal? StoryPoints { get; set; }

    public decimal? OriginalEstimateHours { get; set; }

    public decimal? RemainingHours { get; set; }

    public decimal? CompletedHours { get; set; }

    /// <summary>Tamanho camiseta (PP/P/M/G/GG ou XS/S/M/L/XL).</summary>
    public string? Size { get; set; }

    [JsonConverter(typeof(LenientStringDictionaryConverter))]
    public Dictionary<string, string> OtherMeasures { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Origem da estimativa: <see cref="SourceDocument"/> (padrão) ou <see cref="SourceAiSuggested"/>, quando
    /// o usuário pediu explicitamente que a IA sugerisse valores ausentes (<c>--suggest-estimates</c>).
    /// </summary>
    public string? Source { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        StoryPoints is null && OriginalEstimateHours is null && RemainingHours is null && CompletedHours is null
        && string.IsNullOrWhiteSpace(Size) && OtherMeasures.Count == 0;

    [JsonIgnore]
    public bool IsAiSuggested => string.Equals(Source, SourceAiSuggested, StringComparison.OrdinalIgnoreCase);

    /// <summary>Copia para esta estimativa os valores informados em <paramref name="other"/> (os de <paramref name="other"/> prevalecem).</summary>
    public void MergeFrom(Estimate other)
    {
        StoryPoints = other.StoryPoints ?? StoryPoints;
        OriginalEstimateHours = other.OriginalEstimateHours ?? OriginalEstimateHours;
        RemainingHours = other.RemainingHours ?? RemainingHours;
        CompletedHours = other.CompletedHours ?? CompletedHours;
        Size = other.Size ?? Size;
        Source = other.Source ?? Source;
        foreach (var (key, value) in other.OtherMeasures)
        {
            OtherMeasures[key] = value;
        }
    }
}
