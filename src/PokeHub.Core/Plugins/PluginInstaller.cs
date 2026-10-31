using PokeHub.Core.Localization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using PokeHub.Core.Hub;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;
using PokeHub.Core.Runtimes;

namespace PokeHub.Core.Plugins;

/// <summary>Instala ferramentas em tools/&lt;id&gt; (escopo hub) ou projects/x/decomp/&lt;id&gt; (escopo projeto).</summary>
public sealed class PluginInstaller(HubPaths hub, HubConfig config)
{
    private readonly GitHubReleaseClient _github = new();

    public RuntimeManager Runtimes { get; } = new(hub, config);

    public static readonly InstalledTool BuiltinTool = new() { Version = "embutido", ExecutablePath = "", Source = "builtin" };

    public InstalledTool? GetInstalled(PluginManifest m, Project? project) =>
        m.Source.Type == SourceType.Builtin ? BuiltinTool
        : m.Source.Type == SourceType.Runtime ? Runtimes.GetInstalled(m.Source.RuntimeId ?? m.Id)
        : m.Scope == PluginScope.Project
            ? project?.Config.ProjectTools.GetValueOrDefault(m.Id)
            : config.InstalledTools.GetValueOrDefault(m.Id);

    public string TargetDir(PluginManifest m, Project? project) =>
        m.Scope == PluginScope.Project
            ? (project ?? throw new InvalidOperationException(Loc.T("core.install.projectScope"))).Paths.ToolDir(m.Id)
            : hub.ToolDir(m.Id);

    public async Task<InstalledTool> InstallAsync(PluginManifest m, Project? project, IProgress<string> log, CancellationToken ct = default)
    {
        await Runtimes.EnsureAsync(m.Requires, log, ct);
        var tool = m.Source.Type switch
        {
            SourceType.GitHubRelease => await InstallFromReleaseAsync(m, project, log, ct),
            SourceType.GitClone => await InstallFromGitAsync(m, project, log, ct),
            SourceType.System => throw new InvalidOperationException(Loc.T("core.install.systemSource")),
            SourceType.Runtime => await InstallRuntimeAsync(m, log, ct),
            _ => throw new NotSupportedException(m.Source.Type.ToString()),
        };
        if (m.Source.Type != SourceType.Runtime) Record(m, project, tool); // runtimes se registram sozinhos em installedRuntimes
        log.Report(Loc.T("core.install.done", m.Name, tool.Version));
        return tool;
    }

    private async Task<InstalledTool> InstallRuntimeAsync(PluginManifest m, IProgress<string> log, CancellationToken ct)
    {
        var id = m.Source.RuntimeId ?? m.Id;
        return Runtimes.GetInstalled(id) ?? await Runtimes.InstallAsync(id, log, ct);
    }

    public InstalledTool RegisterSystemTool(PluginManifest m, Project? project, string executablePath)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException(Loc.T("core.install.exeMissing"), executablePath);
        var tool = new InstalledTool { Version = "system", ExecutablePath = executablePath, Source = "system" };
        Record(m, project, tool);
        return tool;
    }

    private void Record(PluginManifest m, Project? project, InstalledTool tool)
    {
        if (m.Scope == PluginScope.Project)
        {
            project!.Config.ProjectTools[m.Id] = tool;
            project.Save();
        }
        else
        {
            config.InstalledTools[m.Id] = tool;
            config.Save(hub.ConfigFile);
        }
    }

    private async Task<InstalledTool> InstallFromReleaseAsync(PluginManifest m, Project? project, IProgress<string> log, CancellationToken ct)
    {
        var repo = m.Source.Repo ?? throw new InvalidDataException(Loc.T("core.install.noRepo", m.Id));
        log.Report(Loc.T("core.install.queryRelease", repo));
        var release = await _github.GetLatestAsync(repo, ct);

        var pattern = new Regex(m.Source.AssetPattern ?? ".*", RegexOptions.IgnoreCase);
        var asset = release.Assets.FirstOrDefault(a => pattern.IsMatch(a.Name))
            ?? throw new InvalidOperationException(Loc.T("core.install.noAsset", release.Tag, m.Source.AssetPattern, string.Join(", ", release.Assets.Select(a => a.Name))));

        var cacheFile = Path.Combine(hub.Cache, m.Id, release.Tag, asset.Name);
        if (!File.Exists(cacheFile))
        {
            log.Report(Loc.T("core.install.downloading", asset.Name, asset.Size / 1024 / 1024));
            await _github.DownloadAsync(asset.DownloadUrl, cacheFile, log, ct);
        }
        else log.Report(Loc.T("core.install.cache", asset.Name));

        var target = TargetDir(m, project);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        string exePath;
        if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            log.Report(Loc.T("core.install.extracting", target));
            ZipFile.ExtractToDirectory(cacheFile, target, overwriteFiles: true);
            var exeName = m.Source.Executable ?? throw new InvalidDataException(Loc.T("core.install.zipNeedsExe", m.Id));
            exePath = Directory.EnumerateFiles(target, exeName, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException(Loc.T("core.install.exeNotInZip", exeName, asset.Name));
        }
        else
        {
            exePath = Path.Combine(target, m.Source.Executable ?? asset.Name);
            File.Copy(cacheFile, exePath, overwrite: true);
        }

        return new InstalledTool { Version = release.Tag, ExecutablePath = exePath, Source = $"github:{repo}" };
    }

    private async Task<InstalledTool> InstallFromGitAsync(PluginManifest m, Project? project, IProgress<string> log, CancellationToken ct)
    {
        var git = Runtimes.ResolveGit() ?? throw new InvalidOperationException(Loc.T("core.install.gitMissing"));
        var repo = m.Source.Repo ?? throw new InvalidDataException(Loc.T("core.install.noRepo", m.Id));
        var url = repo.Contains("://") ? repo : $"https://github.com/{repo}.git";
        var target = TargetDir(m, project);

        if (Directory.Exists(Path.Combine(target, ".git")))
        {
            log.Report(Loc.T("core.install.repoExists", target));
            var pull = await ProcessRunner.RunAsync(git, ["pull", "--ff-only"], target, log, ct);
            if (pull != 0) throw new InvalidOperationException(Loc.T("core.install.pullFailed"));
        }
        else
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var args = new List<string> { "clone" };
            if (m.Source.Recursive) args.Add("--recursive");
            if (m.Source.Branch is { } b) { args.Add("--branch"); args.Add(b); }
            args.Add(url); args.Add(target);
            var code = await ProcessRunner.RunAsync(git, args, null, log, ct);
            if (code != 0) throw new InvalidOperationException(Loc.T("core.install.cloneFailed"));
        }

        var head = Path.Combine(target, ".git", "HEAD");
        var version = File.Exists(head) ? File.ReadAllText(head).Trim().Replace("ref: refs/heads/", "") : "git";
        return new InstalledTool { Version = version, ExecutablePath = target, Source = $"git:{url}" };
    }
}
