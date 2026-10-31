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

    private sealed class AppSettings { public string? RootPath { get; set; } public string? Language { get; set; } }

    private static AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFile)) return new AppSettings();
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), HubConfig.JsonOptions) ?? new AppSettings(); }
        catch { return new AppSettings(); }
    }

    private static void SaveSettings(AppSettings s)
    {
        Directory.CreateDirectory(SettingsDir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(s, HubConfig.JsonOptions));
    }

    public static string? FindConfiguredRoot()
    {
        var s = LoadSettings();
        return s.RootPath is { } p && Directory.Exists(p) ? p : null;
    }

    /// <summary>Idioma salvo em settings.json, ou null se nunca escolhido.</summary>
    public static string? SavedLanguage() => LoadSettings().Language;

    public static void SaveLanguage(string language)
    {
        var s = LoadSettings();
        s.Language = language;
        SaveSettings(s);
    }

    /// <param name="remember">false deixa settings.json como está: uma pasta passada só para esta execução não muda a do aplicativo.</param>
    public static HubPaths Initialize(string root, bool remember = true)
    {
        var paths = new HubPaths(root);
        foreach (var dir in paths.AllDirectories()) Directory.CreateDirectory(dir);
        if (!File.Exists(paths.ConfigFile)) new HubConfig().Save(paths.ConfigFile);
        if (!remember) return paths;

        var s = LoadSettings();
        s.RootPath = paths.Root;
        SaveSettings(s);
        return paths;
    }
}
