using System.IO.Compression;
using System.Text.Json;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;

namespace PokeHub.Core.Runtimes;

/// <summary>Um runtime que o hub sabe instalar em runtimes/&lt;id&gt; (hoje: JREs Temurin via API do Adoptium).</summary>
public sealed record RuntimeSpec(string Id, string Name, int JavaFeatureVersion, string ExecutableName);

/// <summary>
/// Instala e localiza runtimes declarados em <c>requires</c> dos manifestos (ex.: "java21").
/// Requisitos que o hub não gerencia (ex.: "dotnet-framework-4.8", "mwcc") são apenas informativos.
/// </summary>
public sealed class RuntimeManager(HubPaths hub, HubConfig config)
{
    public static readonly IReadOnlyDictionary<string, RuntimeSpec> Known = new Dictionary<string, RuntimeSpec>(StringComparer.OrdinalIgnoreCase)
    {
        ["java21"] = new("java21", "Eclipse Temurin JRE 21", 21, "java.exe"),
        ["java17"] = new("java17", "Eclipse Temurin JRE 17", 17, "java.exe"),
    };

    public static bool IsManaged(string requirement) => Known.ContainsKey(requirement);

    public InstalledTool? GetInstalled(string id)
    {
        var t = config.InstalledRuntimes.GetValueOrDefault(id);
        return t is not null && File.Exists(t.ExecutablePath) ? t : null;
    }

    /// <summary>Garante que todos os runtimes gerenciados de <paramref name="requirements"/> estejam instalados.</summary>
    public async Task EnsureAsync(IEnumerable<string> requirements, IProgress<string> log, CancellationToken ct = default)
    {
        foreach (var r in requirements.Where(IsManaged))
        {
            if (GetInstalled(r) is { } t) { log.Report($"Runtime {r} já instalado ({t.Version})"); continue; }
            await InstallAsync(r, log, ct);
        }
    }

    public async Task<InstalledTool> InstallAsync(string id, IProgress<string> log, CancellationToken ct = default)
    {
        var spec = Known.GetValueOrDefault(id) ?? throw new NotSupportedException($"Runtime desconhecido: {id}");
        log.Report($"Consultando Adoptium para {spec.Name}...");
        var (releaseName, url, size) = await QueryAdoptiumAsync(spec.JavaFeatureVersion, ct);

        var cacheFile = Path.Combine(hub.Cache, "runtimes", id, Path.GetFileName(new Uri(url).LocalPath));
        if (!File.Exists(cacheFile))
        {
            log.Report($"Baixando {Path.GetFileName(cacheFile)} ({size / 1024 / 1024} MB)...");
            await new GitHubReleaseClient().DownloadAsync(url, cacheFile, log, ct);
        }
        else log.Report("Usando cache do runtime.");

        var target = Path.Combine(hub.Runtimes, id);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);
        log.Report($"Extraindo para {target}...");
        ZipFile.ExtractToDirectory(cacheFile, target, overwriteFiles: true);

        var exe = Directory.EnumerateFiles(target, spec.ExecutableName, SearchOption.AllDirectories)
            .FirstOrDefault(p => Path.GetFileName(Path.GetDirectoryName(p)!).Equals("bin", StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"{spec.ExecutableName} não encontrado dentro do pacote do {spec.Name}.");

        var tool = new InstalledTool { Version = releaseName, ExecutablePath = exe, Source = "adoptium" };
        config.InstalledRuntimes[id] = tool;
        config.Save(hub.ConfigFile);
        log.Report($"{spec.Name} instalado: {releaseName}");
        return tool;
    }

    /// <summary>Caminho do java.exe a usar: runtime gerenciado se houver, senão "java" do PATH.</summary>
    public string ResolveJava(IEnumerable<string> requirements)
    {
        foreach (var r in requirements.Where(IsManaged))
            if (GetInstalled(r) is { } t) return t.ExecutablePath;
        foreach (var spec in Known.Values)
            if (GetInstalled(spec.Id) is { } t) return t.ExecutablePath;
        return "java";
    }

    private static async Task<(string ReleaseName, string Url, long Size)> QueryAdoptiumAsync(int feature, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PokeHub/0.1");
        var url = $"https://api.adoptium.net/v3/assets/latest/{feature}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        foreach (var asset in doc.RootElement.EnumerateArray())
        {
            var binary = asset.GetProperty("binary");
            if (binary.GetProperty("image_type").GetString() != "jre") continue;
            var pkg = binary.GetProperty("package");
            return (asset.GetProperty("release_name").GetString() ?? $"jre-{feature}",
                    pkg.GetProperty("link").GetString()!,
                    pkg.GetProperty("size").GetInt64());
        }
        throw new InvalidOperationException($"Adoptium não devolveu um JRE {feature} para Windows x64.");
    }
}
