using PokeHub.Core.Decomp;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;
using PokeHub.Core.Runtimes;

namespace PokeHub.Mcp;

/// <summary>Serviços do Core ligados a uma pasta raiz do hub. É o mesmo conjunto que a janela principal usa.</summary>
public sealed class HubContext
{
    public HubPaths Paths { get; }
    public HubConfig Config { get; }
    public PluginCatalog Catalog { get; }
    public PluginInstaller Installer { get; }
    public RuntimeManager Runtimes => Installer.Runtimes;
    public ToolLauncher Launcher { get; }
    public DecompBuildService Builds { get; }
    public DependencyChecker Deps { get; }
    public WorkspaceService Workspace { get; }
    public WorkspaceGit Git { get; }
    public ProjectService Projects { get; }

    private HubContext(string root, bool isConfiguredRoot)
    {
        Paths = HubBootstrapper.Initialize(root, remember: isConfiguredRoot);
        Config = HubConfig.Load(Paths.ConfigFile);
        Catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        Installer = new PluginInstaller(Paths, Config);
        Launcher = new ToolLauncher(Paths, Runtimes);
        Builds = new DecompBuildService(Config, Paths);
        Deps = new DependencyChecker(Runtimes, Builds);
        Workspace = new WorkspaceService(Runtimes);
        Git = new WorkspaceGit(Runtimes);
        Projects = new ProjectService(Paths);
    }

    public static HubContext Create(string[] args)
    {
        Loc.Instance.Language = Loc.Resolve(HubBootstrapper.SavedLanguage());
        var i = Array.IndexOf(args, "--root");
        var chosen = i >= 0 && i + 1 < args.Length ? args[i + 1] : Environment.GetEnvironmentVariable("POKEHUB_ROOT");
        var root = chosen
            ?? HubBootstrapper.FindConfiguredRoot()
            ?? throw new InvalidOperationException("Pasta do hub não configurada: abra o PokHub uma vez, ou passe --root <pasta> / POKEHUB_ROOT.");
        return new HubContext(root, isConfiguredRoot: chosen is null);
    }

    /// <summary>Projeto pelo nome; erro claro quando não existe.</summary>
    public Project OpenProject(string name) =>
        Projects.Open(name) ?? throw new ArgumentException($"Projeto '{name}' não existe. Projetos: {string.Join(", ", Projects.ListProjects().Select(p => p.Name))}");

    public PluginManifest GetManifest(string id) =>
        Catalog.Get(id) ?? throw new ArgumentException($"Ferramenta '{id}' não existe. Ids: {string.Join(", ", Catalog.All.Select(m => m.Id))}");
}

/// <summary>Coleta as linhas de progresso do Core para devolver ao agente.</summary>
public sealed class LogCollector : IProgress<string>
{
    public List<string> Lines { get; } = [];
    public void Report(string value) => Lines.Add(value);
}
