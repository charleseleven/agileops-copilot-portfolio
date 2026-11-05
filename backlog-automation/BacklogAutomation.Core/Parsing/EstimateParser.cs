using System.Globalization;
using System.Text.RegularExpressions;
using BacklogAutomation.Core.Models;

namespace BacklogAutomation.Core.Parsing;

/// <summary>
/// Interpreta estimativas escritas em linguagem natural (pt-BR/en): "5 SP", "8 pontos", "Story Points: 5",
/// "4h", "2 horas e 30 min", "1,5 dia", "Restante: 3h", "Tamanho: M" e medidas livres no formato
/// "Rótulo: valor" (ex: "Pontos de função: 12") — estas últimas vão para <see cref="Estimate.OtherMeasures"/>.
/// </summary>
public sealed partial class EstimateParser
{
    private readonly decimal _hoursPerDay;

    public EstimateParser(decimal hoursPerDay = 8m)
    {
        if (hoursPerDay <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hoursPerDay), "A jornada (horas por dia) deve ser maior que zero.");
        }

        _hoursPerDay = hoursPerDay;
    }

    /// <summary>
    /// Interpreta um texto com uma ou mais medidas separadas por vírgula, ponto e vírgula ou "|".
    /// Retorna uma estimativa vazia (<see cref="Estimate.IsEmpty"/>) quando nada for reconhecido.
    /// </summary>
    public Estimate Parse(string? text)
    {
        var estimate = new Estimate();
        if (string.IsNullOrWhiteSpace(text))
        {
            return estimate;
        }

        foreach (var rawSegment in SegmentSeparatorRegex().Split(text))
        {
            var segment = rawSegment.Trim().Trim('*', '_', '`').Trim();
            if (segment.Length > 0)
            {
                ParseSegment(segment, estimate);
            }
        }

        return estimate;
    }

    /// <summary>
    /// Tenta interpretar SOMENTE medidas com unidade explícita (ex: "5 SP", "8h") — usado em trechos de
    /// título como "(5 SP)", onde um número solto ou um rótulo qualquer não deve ser tratado como estimativa.
    /// </summary>
    public bool TryParseInline(string text, out Estimate estimate)
    {
        estimate = new Estimate();
        foreach (var rawSegment in SegmentSeparatorRegex().Split(text))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                continue;
            }

            var partial = new Estimate();
            if (!TryParseUnitValues(segment, partial, HourBucket.Original) || !IsFullyConsumedByUnits(segment))
            {
                estimate = new Estimate();
                return false;
            }

            estimate.MergeFrom(partial);
        }

        return !estimate.IsEmpty;
    }

    private void ParseSegment(string segment, Estimate estimate)
    {
        var labeled = LabeledValueRegex().Match(segment);
        if (!labeled.Success)
        {
            TryParseUnitValues(segment, estimate, HourBucket.Original);
            return;
        }

        var label = labeled.Groups["label"].Value.Trim().Trim('*', '_').Trim();
        var value = labeled.Groups["value"].Value.Trim().Trim('*', '_').Trim();
        var normalizedLabel = Normalize(label);

        if (PointsLabelRegex().IsMatch(normalizedLabel))
        {
            if (!TryParseUnitValues(value, estimate, HourBucket.Original) && ParseNumber(value) is { } points)
            {
                estimate.StoryPoints = points;
            }
        }
        else if (RemainingLabelRegex().IsMatch(normalizedLabel))
        {
            AssignHours(value, estimate, HourBucket.Remaining);
        }
        else if (CompletedLabelRegex().IsMatch(normalizedLabel))
        {
            AssignHours(value, estimate, HourBucket.Completed);
        }
        else if (HoursLabelRegex().IsMatch(normalizedLabel))
        {
            AssignHours(value, estimate, HourBucket.Original);
        }
        else if (SizeLabelRegex().IsMatch(normalizedLabel) && TShirtRegex().Match(value) is { Success: true } size)
        {
            estimate.Size = size.Value.ToUpperInvariant();
        }
        else if (GenericEstimateLabelRegex().IsMatch(normalizedLabel))
        {
            // "Estimativa: 5 SP" / "Estimate: 8h" — o rótulo é genérico, a unidade decide o campo.
            if (!TryParseUnitValues(value, estimate, HourBucket.Original))
            {
                estimate.OtherMeasures[label] = value;
            }
        }
        else if (value.Length > 0)
        {
            estimate.OtherMeasures[label] = value;
        }
    }

    private void AssignHours(string value, Estimate estimate, HourBucket bucket)
    {
        if (!TryParseUnitValues(value, estimate, bucket) && ParseNumber(value) is { } hours)
        {
            SetHours(estimate, bucket, hours);
        }
    }

    private bool TryParseUnitValues(string text, Estimate estimate, HourBucket bucket)
    {
        var found = false;

        foreach (Match match in PointsValueRegex().Matches(text))
        {
            estimate.StoryPoints = ParseNumber(match.Groups["n"].Value);
            found = true;
        }

        decimal? hours = null;
        foreach (Match match in HoursValueRegex().Matches(text))
        {
            hours = (hours ?? 0) + ParseNumber(match.Groups["n"].Value)!.Value;
        }

        foreach (Match match in MinutesValueRegex().Matches(text))
        {
            hours = (hours ?? 0) + Math.Round(ParseNumber(match.Groups["n"].Value)!.Value / 60m, 2);
        }

        foreach (Match match in DaysValueRegex().Matches(text))
        {
            hours = (hours ?? 0) + ParseNumber(match.Groups["n"].Value)!.Value * _hoursPerDay;
        }

        if (hours is not null)
        {
            SetHours(estimate, bucket, hours.Value);
            found = true;
        }

        if (TShirtWithLabelRegex().Match(text) is { Success: true } size)
        {
            estimate.Size = size.Groups["size"].Value.ToUpperInvariant();
            found = true;
        }

        return found;
    }

    private static bool IsFullyConsumedByUnits(string segment)
    {
        var remainder = PointsValueRegex().Replace(segment, string.Empty);
        remainder = HoursValueRegex().Replace(remainder, string.Empty);
        remainder = MinutesValueRegex().Replace(remainder, string.Empty);
        remainder = DaysValueRegex().Replace(remainder, string.Empty);
        remainder = TShirtWithLabelRegex().Replace(remainder, string.Empty);
        remainder = Regex.Replace(remainder, @"\b(e|and)\b|[+\s~≈]", string.Empty, RegexOptions.IgnoreCase);
        return remainder.Length == 0;
    }

    private static void SetHours(Estimate estimate, HourBucket bucket, decimal hours)
    {
        switch (bucket)
        {
            case HourBucket.Remaining:
                estimate.RemainingHours = hours;
                break;
            case HourBucket.Completed:
                estimate.CompletedHours = hours;
                break;
            default:
                estimate.OriginalEstimateHours = hours;
                break;
        }
    }

    internal static decimal? ParseNumber(string text)
    {
        var match = NumberRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var normalized = match.Value.Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars);
    }

    private enum HourBucket
    {
        Original,
        Remaining,
        Completed
    }

    [GeneratedRegex(@"[;,|](?!\d)")]
    private static partial Regex SegmentSeparatorRegex();

    [GeneratedRegex(@"^(?<label>[^:=]{2,60}?)\s*[:=]\s*(?<value>.+)$")]
    private static partial Regex LabeledValueRegex();

    [GeneratedRegex(@"^(story\s*points?|sp|pontos?|pontuacao|pts?|esforco|effort|size points?)$")]
    private static partial Regex PointsLabelRegex();

    [GeneratedRegex(@"^(horas?\s+restantes?|restantes?|remaining(\s+work)?|trabalho\s+restante)$")]
    private static partial Regex RemainingLabelRegex();

    [GeneratedRegex(@"^(horas?\s+conclu[a-z]*|conclu[a-z]*|completed(\s+work)?|trabalho\s+conclu[a-z]*|realizad[oa]s?)$")]
    private static partial Regex CompletedLabelRegex();

    [GeneratedRegex(@"^(horas?(\s+estimadas?)?|hours?|estimativa\s+original|original\s+estimate|estimativa\s+em\s+horas|duracao|duration|tempo)$")]
    private static partial Regex HoursLabelRegex();

    [GeneratedRegex(@"^(tamanho|size|t-?shirt(\s+size)?|camiseta)$")]
    private static partial Regex SizeLabelRegex();

    [GeneratedRegex(@"^(estimativas?|estimates?|estimado|estimated)$")]
    private static partial Regex GenericEstimateLabelRegex();

    [GeneratedRegex(@"(?<!\w)(?<n>\d+(?:[.,]\d+)?)\s*(?:sp|story\s*points?|pts?|pontos?|points?)(?!\w)(?!\s+de\s)", RegexOptions.IgnoreCase)]
    private static partial Regex PointsValueRegex();

    [GeneratedRegex(@"(?<!\w)(?<n>\d+(?:[.,]\d+)?)\s*(?:h|hrs?|horas?|hours?)(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex HoursValueRegex();

    [GeneratedRegex(@"(?<!\w)(?<n>\d+(?:[.,]\d+)?)\s*(?:min|mins|minutos?|minutes?)(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex MinutesValueRegex();

    [GeneratedRegex(@"(?<!\w)(?<n>\d+(?:[.,]\d+)?)\s*(?:d|dias?|days?)(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex DaysValueRegex();

    [GeneratedRegex(@"\b(PP|XS|XXS|P|S|M|G|L|GG|XL|XXL|XG)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TShirtRegex();

    [GeneratedRegex(@"(?:tamanho|size|t-?shirt)\s*[:=]?\s*(?<size>PP|XXS|XS|P|S|M|GG|G|XXL|XL|L|XG)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TShirtWithLabelRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberRegex();
}
