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
    public delegate void Hook(PluginManifest manifest, Project? project, string executable, IProgress<string> log);

    public static readonly IReadOnlyDictionary<string, Hook> All = new Dictionary<string, Hook>(StringComparer.OrdinalIgnoreCase)
    {
        ["dspre-open-workspace"] = DspreOpenWorkspace,
        ["melonds-default-keys"] = MelonDsDefaultKeys,
    };

    public static void Run(string? hookId, PluginManifest manifest, Project? project, string executable, IProgress<string> log)
    {
        if (hookId is null) return;
        if (!All.TryGetValue(hookId, out var hook)) { log.Report(Loc.T("core.hook.unknown", hookId)); return; }
        hook(manifest, project, executable, log);
    }

    /// <summary>A configuração fica ao lado do executável, na instalação do hub. Um erro aqui não pode impedir o emulador de abrir.</summary>
    private static void MelonDsDefaultKeys(PluginManifest manifest, Project? project, string executable, IProgress<string> log)
    {
        var file = Path.Combine(Path.GetDirectoryName(executable) ?? "", "melonDS.toml");
        try
        {
            var updated = MelonDsKeys.WithDefaults(File.Exists(file) ? File.ReadAllText(file) : null);
            if (updated is null) return;
            File.WriteAllText(file, updated);
            log.Report(Loc.T("core.hook.melonKeys"));
        }
        catch (IOException ex) { log.Report(Loc.T("core.hook.melonKeysFailed", ex.Message)); }
        catch (UnauthorizedAccessException ex) { log.Report(Loc.T("core.hook.melonKeysFailed", ex.Message)); }
    }

    /// <summary>
    /// O DSPRE lê %AppData%\DSPRE\userSettings.json e, se openDefaultRom aponta para uma pasta válida,
    /// abre esse projeto ao iniciar (perguntando antes, a menos que neverAskForOpening esteja ligado).
    /// </summary>
    public static string DspreSettingsFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DSPRE", "userSettings.json");

    private static void DspreOpenWorkspace(PluginManifest manifest, Project? project, string executable, IProgress<string> log)
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
