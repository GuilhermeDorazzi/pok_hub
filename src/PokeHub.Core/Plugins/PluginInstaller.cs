using System.IO.Compression;
using System.Text.RegularExpressions;
using PokeHub.Core.Hub;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Plugins;

/// <summary>Instala ferramentas em tools/&lt;id&gt; (escopo hub) ou projects/x/decomp/&lt;id&gt; (escopo projeto).</summary>
public sealed class PluginInstaller(HubPaths hub, HubConfig config)
{
    private readonly GitHubReleaseClient _github = new();

    public InstalledTool? GetInstalled(PluginManifest m, Project? project) =>
        m.Scope == PluginScope.Project
            ? project?.Config.ProjectTools.GetValueOrDefault(m.Id)
            : config.InstalledTools.GetValueOrDefault(m.Id);

    public string TargetDir(PluginManifest m, Project? project) =>
        m.Scope == PluginScope.Project
            ? (project ?? throw new InvalidOperationException("Este plugin é instalado por projeto; abra um projeto.")).Paths.ToolDir(m.Id)
            : hub.ToolDir(m.Id);

    public async Task<InstalledTool> InstallAsync(PluginManifest m, Project? project, IProgress<string> log, CancellationToken ct = default)
    {
        var tool = m.Source.Type switch
        {
            SourceType.GitHubRelease => await InstallFromReleaseAsync(m, project, log, ct),
            SourceType.GitClone => await InstallFromGitAsync(m, project, log, ct),
            SourceType.System => throw new InvalidOperationException("Ferramenta de sistema: use RegisterSystemTool com o caminho do executável."),
            _ => throw new NotSupportedException(m.Source.Type.ToString()),
        };
        Record(m, project, tool);
        log.Report($"{m.Name} instalado: {tool.Version}");
        return tool;
    }

    public InstalledTool RegisterSystemTool(PluginManifest m, Project? project, string executablePath)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("Executável não encontrado.", executablePath);
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
        var repo = m.Source.Repo ?? throw new InvalidDataException($"{m.Id}: source.repo ausente");
        log.Report($"Consultando última release de {repo}...");
        var release = await _github.GetLatestAsync(repo, ct);

        var pattern = new Regex(m.Source.AssetPattern ?? ".*", RegexOptions.IgnoreCase);
        var asset = release.Assets.FirstOrDefault(a => pattern.IsMatch(a.Name))
            ?? throw new InvalidOperationException($"Nenhum asset da release {release.Tag} bate com '{m.Source.AssetPattern}'. Assets: {string.Join(", ", release.Assets.Select(a => a.Name))}");

        var cacheFile = Path.Combine(hub.Cache, m.Id, release.Tag, asset.Name);
        if (!File.Exists(cacheFile))
        {
            log.Report($"Baixando {asset.Name} ({asset.Size / 1024 / 1024} MB)...");
            await _github.DownloadAsync(asset.DownloadUrl, cacheFile, log, ct);
        }
        else log.Report($"Usando cache: {asset.Name}");

        var target = TargetDir(m, project);
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        Directory.CreateDirectory(target);

        string exePath;
        if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            log.Report($"Extraindo para {target}...");
            ZipFile.ExtractToDirectory(cacheFile, target, overwriteFiles: true);
            var exeName = m.Source.Executable ?? throw new InvalidDataException($"{m.Id}: source.executable é obrigatório para assets zip");
            exePath = Directory.EnumerateFiles(target, exeName, SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new FileNotFoundException($"'{exeName}' não encontrado dentro de {asset.Name}");
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
        if (!ProcessRunner.IsOnPath("git")) throw new InvalidOperationException("git não encontrado no PATH. Instale o Git for Windows.");
        var repo = m.Source.Repo ?? throw new InvalidDataException($"{m.Id}: source.repo ausente");
        var url = repo.Contains("://") ? repo : $"https://github.com/{repo}.git";
        var target = TargetDir(m, project);

        if (Directory.Exists(Path.Combine(target, ".git")))
        {
            log.Report($"Repositório já existe, atualizando ({target})...");
            var pull = await ProcessRunner.RunAsync("git", ["pull", "--ff-only"], target, log, ct);
            if (pull != 0) throw new InvalidOperationException("git pull falhou. Veja o log.");
        }
        else
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var args = new List<string> { "clone" };
            if (m.Source.Recursive) args.Add("--recursive");
            if (m.Source.Branch is { } b) { args.Add("--branch"); args.Add(b); }
            args.Add(url); args.Add(target);
            var code = await ProcessRunner.RunAsync("git", args, null, log, ct);
            if (code != 0) throw new InvalidOperationException("git clone falhou. Veja o log.");
        }

        var head = Path.Combine(target, ".git", "HEAD");
        var version = File.Exists(head) ? File.ReadAllText(head).Trim().Replace("ref: refs/heads/", "") : "git";
        return new InstalledTool { Version = version, ExecutablePath = target, Source = $"git:{url}" };
    }
}
