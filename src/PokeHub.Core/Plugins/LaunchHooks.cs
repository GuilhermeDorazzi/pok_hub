using PokeHub.Core.Localization;
using System.Text.Json.Nodes;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Plugins;

/// <summary>
/// Preparações que o hub faz antes de abrir uma ferramenta, para ela já apontar para o projeto.
/// Referenciadas pelo manifesto em launch.hook.
/// </summary>
public static class LaunchHooks
{
    public delegate void Hook(PluginManifest manifest, Project? project, IProgress<string> log);

    public static readonly IReadOnlyDictionary<string, Hook> All = new Dictionary<string, Hook>(StringComparer.OrdinalIgnoreCase)
    {
        ["dspre-open-workspace"] = DspreOpenWorkspace,
    };

    public static void Run(string? hookId, PluginManifest manifest, Project? project, IProgress<string> log)
    {
        if (hookId is null) return;
        if (!All.TryGetValue(hookId, out var hook)) { log.Report(Loc.T("core.hook.unknown", hookId)); return; }
        hook(manifest, project, log);
    }

    /// <summary>
    /// O DSPRE lê %AppData%\DSPRE\userSettings.json e, se openDefaultRom aponta para uma pasta válida,
    /// abre esse projeto ao iniciar (perguntando antes, a menos que neverAskForOpening esteja ligado).
    /// </summary>
    public static string DspreSettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DSPRE", "userSettings.json");

    private static void DspreOpenWorkspace(PluginManifest manifest, Project? project, IProgress<string> log)
    {
        if (project is null || !WorkspaceService.IsExtracted(project)) return;
        SetDspreOpenDefaultRom(DspreSettingsFile, project.Paths.Workspace);
        log.Report(Loc.T("core.hook.dspre", project.Paths.Workspace));
    }

    public static void SetDspreOpenDefaultRom(string settingsFile, string workspace)
    {
        JsonObject root;
        try { root = File.Exists(settingsFile) ? JsonNode.Parse(File.ReadAllText(settingsFile)) as JsonObject ?? new JsonObject() : new JsonObject(); }
        catch { root = new JsonObject(); }
        root["openDefaultRom"] = workspace;
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        File.WriteAllText(settingsFile, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
