using PokeHub.Core.Localization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;

namespace PokeHub.Core.Runtimes;

public enum RuntimeSource { Adoptium, GitHubRelease }

/// <summary>Um runtime que o hub sabe instalar em runtimes/&lt;id&gt;.</summary>
public sealed record RuntimeSpec(
    string Id, string Name, RuntimeSource Source, string ExecutableName,
    int JavaFeatureVersion = 0, string? Repo = null, string? AssetPattern = null, string? ExecutableRelativePath = null,
    bool SfxExtract = false);

/// <summary>
/// Instala e localiza runtimes: JREs Temurin (API do Adoptium) e o Git portátil (MinGit, release do Git for Windows).
/// Requisitos que o hub não gerencia (ex.: "dotnet-framework-4.8", "unix-shell") são tratados em outro lugar ou são informativos.
/// </summary>
public sealed class RuntimeManager(HubPaths hub, HubConfig config)
{
    public const string GitId = "git";
    public const string DsRomId = "dsrom";
    public const string Msys2Id = "msys2";

    public static readonly IReadOnlyDictionary<string, RuntimeSpec> Known = new Dictionary<string, RuntimeSpec>(StringComparer.OrdinalIgnoreCase)
    {
        ["java21"] = new("java21", "Eclipse Temurin JRE 21", RuntimeSource.Adoptium, "java.exe", JavaFeatureVersion: 21),
        ["java17"] = new("java17", "Eclipse Temurin JRE 17", RuntimeSource.Adoptium, "java.exe", JavaFeatureVersion: 17),
        [GitId] = new(GitId, Loc.T("core.rt.gitName"), RuntimeSource.GitHubRelease, "git.exe",
            Repo: "git-for-windows/git", AssetPattern: @"^MinGit-[\d.]+-64-bit\.zip$", ExecutableRelativePath: @"cmd\git.exe"),
        [DsRomId] = new(DsRomId, Loc.T("core.rt.dsromName"), RuntimeSource.GitHubRelease, "dsrom.exe",
            Repo: "AetiasHax/ds-rom", AssetPattern: @"^dsrom-windows-x86_64\.exe$"),
        // pacote auto-extraível (7-Zip SFX): descompacta msys64/ sem instalador nem administrador
        [Msys2Id] = new(Msys2Id, Loc.T("core.rt.msysName"), RuntimeSource.GitHubRelease, "bash.exe",
            Repo: "msys2/msys2-installer", AssetPattern: @"^msys2-base-x86_64-latest\.sfx\.exe$",
            ExecutableRelativePath: @"msys64\usr\bin\bash.exe", SfxExtract: true),
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
            if (GetInstalled(r) is { } t) { log.Report(Loc.T("core.rt.alreadyInstalled", r, t.Version)); continue; }
            await InstallAsync(r, log, ct);
        }
    }

    public async Task<InstalledTool> InstallAsync(string id, IProgress<string> log, CancellationToken ct = default)
    {
        var spec = Known.GetValueOrDefault(id) ?? throw new NotSupportedException(Loc.T("core.rt.unknown", id));
        var (version, url, size) = spec.Source switch
        {
            RuntimeSource.Adoptium => await QueryAdoptiumAsync(spec, log, ct),
            RuntimeSource.GitHubRelease => await QueryGitHubAsync(spec, log, ct),
            _ => throw new NotSupportedException(spec.Source.ToString()),
        };

        var cacheFile = Path.Combine(hub.Cache, "runtimes", id, Path.GetFileName(new Uri(url).LocalPath));
        if (!File.Exists(cacheFile))
        {
            log.Report(Loc.T("core.install.downloading", Path.GetFileName(cacheFile), size / 1024 / 1024));
            await new GitHubReleaseClient().DownloadAsync(url, cacheFile, log, ct);
        }
        else log.Report(Loc.T("core.rt.cache"));

        var target = Path.Combine(hub.Runtimes, id);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        string exe;
        if (spec.SfxExtract)
        {
            log.Report(Loc.T("core.rt.sfxExtract", target));
            var code = await ProcessRunner.RunAsync(cacheFile, ["-y", "-o" + target], target, log, ct);
            if (code != 0) throw new InvalidOperationException(Loc.T("core.rt.sfxFailed", spec.Name, code));
            exe = Path.Combine(target, spec.ExecutableRelativePath!);
            if (!File.Exists(exe)) throw new FileNotFoundException(Loc.T("core.rt.exeMissingAfterSfx", spec.ExecutableName, spec.Name), exe);
        }
        else if (!cacheFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // asset é o próprio executável
            exe = Path.Combine(target, spec.ExecutableName);
            File.Copy(cacheFile, exe, overwrite: true);
        }
        else if (ZipExtract(cacheFile, target, log) && spec.ExecutableRelativePath is { } rel && File.Exists(Path.Combine(target, rel)))
            exe = Path.Combine(target, rel);
        else
            exe = Directory.EnumerateFiles(target, spec.ExecutableName, SearchOption.AllDirectories)
                .OrderBy(p => p.Length).FirstOrDefault()
                ?? throw new FileNotFoundException(Loc.T("core.rt.exeMissingInPackage", spec.ExecutableName, spec.Name));

        var tool = new InstalledTool { Version = version, ExecutablePath = exe, Source = spec.Source.ToString().ToLowerInvariant() };
        config.InstalledRuntimes[id] = tool;
        config.Save(hub.ConfigFile);
        log.Report(Loc.T("core.install.done", spec.Name, version));
        return tool;
    }

    private static bool ZipExtract(string zip, string target, IProgress<string> log)
    {
        log.Report(Loc.T("core.install.extracting", target));
        ZipFile.ExtractToDirectory(zip, target, overwriteFiles: true);
        return true;
    }

    /// <summary>dsrom.exe gerenciado, ou null se ainda não instalado.</summary>
    public string? ResolveDsRom() => GetInstalled(DsRomId)?.ExecutablePath;

    /// <summary>Caminho do java.exe a usar: runtime gerenciado se houver, senão "java" do PATH.</summary>
    public string ResolveJava(IEnumerable<string> requirements)
    {
        foreach (var r in requirements.Where(IsManaged))
            if (GetInstalled(r) is { } t) return t.ExecutablePath;
        foreach (var spec in Known.Values.Where(s => s.Source == RuntimeSource.Adoptium))
            if (GetInstalled(spec.Id) is { } t) return t.ExecutablePath;
        return "java";
    }

    /// <summary>git.exe a usar: o gerenciado, senão o do PATH, senão null.</summary>
    public string? ResolveGit()
    {
        if (GetInstalled(GitId) is { } t) return t.ExecutablePath;
        return ProcessRunner.IsOnPath("git") ? "git" : null;
    }

    private static async Task<(string Version, string Url, long Size)> QueryAdoptiumAsync(RuntimeSpec spec, IProgress<string> log, CancellationToken ct)
    {
        log.Report(Loc.T("core.rt.adoptiumQuery", spec.Name));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PokeHub/0.1");
        var url = $"https://api.adoptium.net/v3/assets/latest/{spec.JavaFeatureVersion}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url, ct));
        foreach (var asset in doc.RootElement.EnumerateArray())
        {
            var binary = asset.GetProperty("binary");
            if (binary.GetProperty("image_type").GetString() != "jre") continue;
            var pkg = binary.GetProperty("package");
            return (asset.GetProperty("release_name").GetString() ?? $"jre-{spec.JavaFeatureVersion}",
                    pkg.GetProperty("link").GetString()!,
                    pkg.GetProperty("size").GetInt64());
        }
        throw new InvalidOperationException(Loc.T("core.rt.adoptiumNone", spec.JavaFeatureVersion));
    }

    private static async Task<(string Version, string Url, long Size)> QueryGitHubAsync(RuntimeSpec spec, IProgress<string> log, CancellationToken ct)
    {
        log.Report(Loc.T("core.install.queryRelease", spec.Repo));
        var release = await new GitHubReleaseClient().GetLatestAsync(spec.Repo!, ct);
        var re = new Regex(spec.AssetPattern!, RegexOptions.IgnoreCase);
        var asset = release.Assets.FirstOrDefault(a => re.IsMatch(a.Name))
            ?? throw new InvalidOperationException(Loc.T("core.rt.noAsset", spec.Repo, release.Tag, spec.AssetPattern));
        return (release.Tag, asset.DownloadUrl, asset.Size);
    }
}
