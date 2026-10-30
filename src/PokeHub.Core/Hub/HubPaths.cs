namespace PokeHub.Core.Hub;

/// <summary>Layout de pastas do ecossistema PokeHub. Tudo vive abaixo de <see cref="Root"/>.</summary>
public sealed class HubPaths
{
    public HubPaths(string root) => Root = Path.GetFullPath(root);

    public string Root { get; }
    public string ConfigFile => Path.Combine(Root, "hub.json");
    public string Tools => Path.Combine(Root, "tools");
    public string Runtimes => Path.Combine(Root, "runtimes");
    public string Projects => Path.Combine(Root, "projects");
    public string Cache => Path.Combine(Root, "cache");
    public string Logs => Path.Combine(Root, "logs");

    public string ToolDir(string pluginId) => Path.Combine(Tools, pluginId);
    public string ProjectDir(string projectName) => Path.Combine(Projects, projectName);

    public IEnumerable<string> AllDirectories() => [Root, Tools, Runtimes, Projects, Cache, Logs];
}
