using System.Text.Json;

namespace PokeHub.Core.Hub;

/// <summary>
/// Primeira execução: guarda onde fica o ecossistema (em %AppData%\PokeHub\settings.json)
/// e cria a árvore de pastas.
/// </summary>
public static class HubBootstrapper
{
    private static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PokeHub");
    private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

    private sealed class AppSettings { public string? RootPath { get; set; } }

    public static string? FindConfiguredRoot()
    {
        if (!File.Exists(SettingsFile)) return null;
        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), HubConfig.JsonOptions);
        return s?.RootPath is { } p && Directory.Exists(p) ? p : null;
    }

    public static HubPaths Initialize(string root)
    {
        var paths = new HubPaths(root);
        foreach (var dir in paths.AllDirectories()) Directory.CreateDirectory(dir);
        if (!File.Exists(paths.ConfigFile)) new HubConfig().Save(paths.ConfigFile);

        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new AppSettings { RootPath = paths.Root }, HubConfig.JsonOptions));
        return paths;
    }
}
