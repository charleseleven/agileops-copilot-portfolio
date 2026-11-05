using System.Text.Json;
using System.Text.Json.Serialization;

namespace BacklogAutomation.Core.Models;

/// <summary>
/// Lê um objeto JSON como dicionário de strings aceitando valores numéricos/booleanos — a IA costuma
/// devolver <c>{"Pontos de função": 12}</c> em vez de <c>{"Pontos de função": "12"}</c>.
/// </summary>
internal sealed class LenientStringDictionaryConverter : JsonConverter<Dictionary<string, string>>
{
    public override Dictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (reader.TokenType == JsonTokenType.Null)
        {
            return result;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => property.Value.GetRawText()
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                result[property.Name] = value;
            }
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, item) in value)
        {
            writer.WriteString(key, item);
        }

        writer.WriteEndObject();
    }
}
