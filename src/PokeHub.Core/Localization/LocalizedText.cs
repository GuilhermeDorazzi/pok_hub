using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeHub.Core.Localization;

/// <summary>
/// Texto de manifesto que pode ser uma string simples ou um objeto por idioma: {"pt-BR": "...", "en-US": "..."}.
/// <see cref="Value"/> devolve o texto no idioma atual, com fallback para pt-BR e para a primeira variante.
/// </summary>
[JsonConverter(typeof(LocalizedTextConverter))]
public sealed class LocalizedText
{
    private readonly Dictionary<string, string> _byLanguage = new(StringComparer.OrdinalIgnoreCase);

    public LocalizedText() { }
    public LocalizedText(string text) => _byLanguage["*"] = text;

    public void Set(string language, string text) => _byLanguage[language] = text;

    public string Value => Get(Loc.Instance.Language);

    public string Get(string language)
    {
        if (_byLanguage.TryGetValue(language, out var s)) return s;
        if (_byLanguage.TryGetValue("*", out var any)) return any;
        if (_byLanguage.TryGetValue(Loc.Default, out var d)) return d;
        return _byLanguage.Values.FirstOrDefault() ?? "";
    }

    public bool IsEmpty => _byLanguage.Count == 0 || _byLanguage.Values.All(string.IsNullOrWhiteSpace);
    public IReadOnlyDictionary<string, string> Variants => _byLanguage;

    public static implicit operator LocalizedText(string text) => new(text);
    public override string ToString() => Value;
}

public sealed class LocalizedTextConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String) return new LocalizedText(reader.GetString() ?? "");
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Texto localizado deve ser string ou objeto por idioma.");
        var result = new LocalizedText();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var lang = reader.GetString()!;
            reader.Read();
            result.Set(lang, reader.GetString() ?? "");
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
    {
        if (value.Variants.Count == 1 && value.Variants.ContainsKey("*")) { writer.WriteStringValue(value.Variants["*"]); return; }
        writer.WriteStartObject();
        foreach (var (k, v) in value.Variants) writer.WriteString(k, v);
        writer.WriteEndObject();
    }
}
