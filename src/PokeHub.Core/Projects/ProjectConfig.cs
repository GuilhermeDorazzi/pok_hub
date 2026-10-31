using PokeHub.Core.Localization;
using System.Text.Json;
using PokeHub.Core.Hub;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Projects;

/// <summary>project.json dentro de projects/&lt;nome&gt;/.</summary>
public sealed class ProjectConfig
{
    public int SchemaVersion { get; set; } = 1;
    public required string Name { get; set; }
    public BaseGame BaseGame { get; set; }
    public string GameCode { get; set; } = "";
    public int Generation { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>Ferramentas instaladas no escopo do projeto (ex.: decomps), por id de plugin.</summary>
    public Dictionary<string, InstalledTool> ProjectTools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static ProjectConfig Load(string file)
    {
        var cfg = JsonSerializer.Deserialize<ProjectConfig>(File.ReadAllText(file), HubConfig.JsonOptions)
            ?? throw new InvalidDataException(Loc.T("core.project.invalidConfig", file));
        cfg.ProjectTools = new Dictionary<string, InstalledTool>(cfg.ProjectTools, StringComparer.OrdinalIgnoreCase);
        return cfg;
    }

    public void Save(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, HubConfig.JsonOptions));
}

/// <summary>Caminhos de um projeto aberto.</summary>
public sealed class ProjectPaths
{
    public ProjectPaths(string dir) => Dir = dir;
    public string Dir { get; }
    public string ConfigFile => Path.Combine(Dir, "project.json");
    public string BaseRom => Path.Combine(Dir, "base.nds");
    /// <summary>ROM extraída, fonte única da verdade. As ferramentas apontam para cá.</summary>
    public string Workspace => Path.Combine(Dir, "workspace");
    public string Builds => Path.Combine(Dir, "builds");
    public string Decomp => Path.Combine(Dir, "decomp");
    public string ToolDir(string pluginId) => Path.Combine(Decomp, pluginId);
}

public sealed class Project
{
    public required ProjectPaths Paths { get; init; }
    public required ProjectConfig Config { get; init; }
    public string Name => Config.Name;
    public void Save() => Config.Save(Paths.ConfigFile);
}
