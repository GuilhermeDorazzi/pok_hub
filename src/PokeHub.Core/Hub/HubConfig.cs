using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeHub.Core.Hub;

/// <summary>Conteúdo de hub.json: o que está instalado no escopo do hub.</summary>
public sealed class HubConfig
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public Dictionary<string, InstalledTool> InstalledTools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastProject { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static HubConfig Load(string file)
    {
        if (!File.Exists(file)) return new HubConfig();
        var cfg = JsonSerializer.Deserialize<HubConfig>(File.ReadAllText(file), JsonOptions) ?? new HubConfig();
        // O desserializador recria o dicionário sem o comparador: restaurar case-insensitive.
        cfg.InstalledTools = new Dictionary<string, InstalledTool>(cfg.InstalledTools, StringComparer.OrdinalIgnoreCase);
        return cfg;
    }

    public void Save(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, JsonOptions));
}

public sealed class InstalledTool
{
    public required string Version { get; set; }
    public required string ExecutablePath { get; set; }
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;
    public string? Source { get; set; }
}
