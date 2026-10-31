using System.Text.Json;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;

namespace PokeHub.Core.Help;

/// <summary>
/// Guia passo a passo (guides/*.json): como usar um recurso do hub ou uma ferramenta externa, com o mínimo
/// traduzido da documentação original. Textos são <see cref="LocalizedText"/> (pt-BR e en-US).
/// </summary>
public sealed class Guide
{
    public required string Id { get; set; }
    /// <summary>Grupo na seção Ajuda: start, edit, test, advanced.</summary>
    public string Group { get; set; } = "edit";
    /// <summary>Chave do card da tela inicial que leva a este guia (ex.: "maps").</summary>
    public string? Feature { get; set; }
    /// <summary>Ids de plugins cujas linhas em Ferramentas mostram o botão Guia.</summary>
    public List<string> Tools { get; set; } = [];
    public required LocalizedText Title { get; set; }
    public LocalizedText? Intro { get; set; }
    public List<LocalizedText> Steps { get; set; } = [];
    public List<LocalizedText> Tips { get; set; } = [];
    public List<GuideLink> Links { get; set; } = [];
}

public sealed class GuideLink
{
    public required LocalizedText Label { get; set; }
    public required string Url { get; set; }
}

public sealed class GuideCatalog
{
    private readonly List<Guide> _guides = [];

    public IReadOnlyList<Guide> All => _guides;
    public static readonly string[] GroupOrder = ["start", "edit", "test", "advanced"];

    public Guide? Get(string id) => _guides.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public Guide? ForFeature(string feature) => _guides.FirstOrDefault(g => feature.Equals(g.Feature, StringComparison.OrdinalIgnoreCase));
    public Guide? ForTool(string pluginId) => _guides.FirstOrDefault(g => g.Tools.Contains(pluginId, StringComparer.OrdinalIgnoreCase));

    /// <summary>Carrega guides/*.json; a ordem dos arquivos (prefixo numérico) é a ordem de exibição.</summary>
    public static GuideCatalog LoadFromDirectory(string dir)
    {
        var catalog = new GuideCatalog();
        if (!Directory.Exists(dir)) return catalog;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var g = JsonSerializer.Deserialize<Guide>(File.ReadAllText(file), HubConfig.JsonOptions);
            if (g is not null) catalog._guides.Add(g);
        }
        return catalog;
    }
}
