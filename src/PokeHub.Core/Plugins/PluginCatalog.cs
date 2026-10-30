using System.Text.Json;
using PokeHub.Core.Hub;

namespace PokeHub.Core.Plugins;

/// <summary>Carrega todos os manifestos de uma pasta.</summary>
public sealed class PluginCatalog
{
    private readonly Dictionary<string, PluginManifest> _byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<PluginManifest> All => _byId.Values;
    public PluginManifest? Get(string id) => _byId.GetValueOrDefault(id);

    public IEnumerable<PluginManifest> ForContext(ToolContext ctx, int? generation = null) =>
        All.Where(p => p.Contexts.Contains(ctx) && (generation is null || p.Generations.Contains(generation.Value)))
           .OrderBy(p => p.Name);

    public static PluginCatalog LoadFromDirectory(string dir)
    {
        var catalog = new PluginCatalog();
        if (!Directory.Exists(dir)) return catalog;
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f))
        {
            var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(file), HubConfig.JsonOptions);
            if (m is null) continue;
            catalog._byId[m.Id] = m;
        }
        return catalog;
    }
}
