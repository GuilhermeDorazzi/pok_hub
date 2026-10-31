using System.Diagnostics;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Projects;
using PokeHub.Core.Runtimes;

namespace PokeHub.Core.Processes;

/// <summary>Uma ferramenta aberta pelo hub.</summary>
public sealed class RunningTool : IDisposable
{
    public required string PluginId { get; init; }
    public required Process Process { get; init; }
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public bool HasExited => Process.HasExited;
    public void Dispose() => Process.Dispose();
}

/// <summary>Resolve argumentos com variáveis do projeto e abre a ferramenta como processo filho.</summary>
public sealed class ToolLauncher(HubPaths hub, RuntimeManager runtimes)
{
    private readonly List<RunningTool> _running = [];
    public IReadOnlyList<RunningTool> Running => _running;

    public event Action<RunningTool, string>? Output;
    public event Action<RunningTool>? Exited;

    public RunningTool Launch(PluginManifest m, InstalledTool tool, Project? project, IProgress<string>? log = null)
    {
        var launch = m.Launch ?? new LaunchSpec();
        var exe = tool.ExecutablePath;
        LaunchHooks.Run(launch.Hook, m, project, exe, log ?? new Progress<string>(_ => { }));
        var vars = BuildVariables(project, exe);

        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = launch.Runtime is not null || IsBatch(exe),
            WorkingDirectory = Expand(launch.WorkingDirectory, vars) ?? Path.GetDirectoryName(exe) ?? hub.Root,
        };

        if (launch.Runtime == "java")
        {
            psi.FileName = runtimes.ResolveJava(m.Requires);
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(exe);
        }
        else
        {
            psi.FileName = exe;
            // o .bat de um programa Java acha o java pelo JAVA_HOME; sem isso usaria o do sistema, se houver
            if (m.Requires.Any(RuntimeManager.IsManaged) && JavaHomeOf(runtimes.ResolveJava(m.Requires)) is { } javaHome)
                psi.Environment["JAVA_HOME"] = javaHome;
        }

        foreach (var a in launch.Args) psi.ArgumentList.Add(Expand(a, vars)!);

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var rt = new RunningTool { PluginId = m.Id, Process = p };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) Output?.Invoke(rt, e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) Output?.Invoke(rt, e.Data); };
        p.Exited += (_, _) => { _running.Remove(rt); Exited?.Invoke(rt); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _running.Add(rt);
        return rt;
    }

    private static bool IsBatch(string exe) => exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    /// <summary>Pasta do Java a partir de ...\\bin\\java.exe; null se for só "java" do PATH.</summary>
    public static string? JavaHomeOf(string javaExe) =>
        Path.IsPathRooted(javaExe) ? Path.GetDirectoryName(Path.GetDirectoryName(javaExe)) : null;

    private Dictionary<string, string> BuildVariables(Project? project, string exe)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hub"] = hub.Root,
            ["tool"] = Path.GetDirectoryName(exe) ?? hub.Root,
        };
        if (project is null) return vars;
        vars["project"] = project.Paths.Dir;
        vars["workspace"] = project.Paths.Workspace;
        vars["baseRom"] = project.Paths.BaseRom;
        vars["builds"] = project.Paths.Builds;
        vars["decomp"] = project.Paths.Decomp;
        vars["rom"] = LatestBuild(project) ?? project.Paths.BaseRom;
        return vars;
    }

    /// <summary>Último .nds gerado em builds/, se houver.</summary>
    public static string? LatestBuild(Project project) =>
        Directory.Exists(project.Paths.Builds)
            ? Directory.EnumerateFiles(project.Paths.Builds, "*.nds").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;

    private static string? Expand(string? template, Dictionary<string, string> vars)
    {
        if (template is null) return null;
        foreach (var (k, v) in vars) template = template.Replace("${" + k + "}", v, StringComparison.OrdinalIgnoreCase);
        return template;
    }
}
