using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.App.Views;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HubPaths _paths;
    private readonly HubConfig _config;
    private readonly PluginCatalog _catalog;
    private readonly PluginInstaller _installer;
    private readonly ToolLauncher _launcher;
    private readonly ProjectService _projects;
    private readonly StringBuilder _log = new();

    public IReadOnlyList<ContextItem> Contexts => ContextItem.All;
    public ObservableCollection<ToolItemViewModel> Tools { get; } = [];
    public ObservableCollection<Project> Projects { get; } = [];

    [ObservableProperty] private ContextItem _selectedContext = ContextItem.All[0];
    [ObservableProperty] private Project? _currentProject;
    [ObservableProperty] private string _logText = "";

    public string RootPath => _paths.Root;
    public string ProjectSummary => CurrentProject is { } p
        ? $"{p.Name}  ·  {p.Config.BaseGame} ({p.Config.GameCode})  ·  Gen {p.Config.Generation}"
        : "Nenhum projeto aberto";

    public MainViewModel(HubPaths paths, HubConfig config, PluginCatalog catalog)
    {
        _paths = paths;
        _config = config;
        _catalog = catalog;
        _installer = new PluginInstaller(paths, config);
        _launcher = new ToolLauncher(paths);
        _projects = new ProjectService(paths);

        _launcher.Output += (rt, line) => Log($"[{rt.PluginId}] {line}");

        Log($"PokeHub iniciado. Raiz: {paths.Root}");
        Log($"{catalog.All.Count} plugins carregados: {string.Join(", ", catalog.All.Select(p => p.Id))}");
        ReloadProjects();
        if (config.LastProject is { } last) CurrentProject = Projects.FirstOrDefault(p => p.Name == last);
        RebuildTools();
    }

    private void ReloadProjects()
    {
        Projects.Clear();
        foreach (var p in _projects.ListProjects().OrderBy(p => p.Name)) Projects.Add(p);
    }

    partial void OnSelectedContextChanged(ContextItem value) => RebuildTools();

    partial void OnCurrentProjectChanged(Project? value)
    {
        _config.LastProject = value?.Name;
        _config.Save(_paths.ConfigFile);
        OnPropertyChanged(nameof(ProjectSummary));
        RebuildTools();
    }

    private void RebuildTools()
    {
        Tools.Clear();
        // GEN5: quando houver projeto Gen 5 aberto, o filtro por geração esconde os plugins só de Gen 4.
        var gen = CurrentProject?.Config.Generation;
        foreach (var m in _catalog.ForContext(SelectedContext.Context, gen))
            Tools.Add(new ToolItemViewModel(m, _installer, _launcher, () => CurrentProject, Log));
    }

    public void Log(string line)
    {
        void Append()
        {
            _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(line);
            LogText = _log.ToString();
        }
        if (Application.Current?.Dispatcher.CheckAccess() ?? true) Append();
        else Application.Current!.Dispatcher.Invoke(Append);
    }

    [RelayCommand]
    private void NewProject()
    {
        var win = new NewProjectWindow(_projects) { Owner = Application.Current.MainWindow };
        if (win.ShowDialog() == true && win.Created is { } created)
        {
            Log($"Projeto '{created.Name}' criado: {created.Config.BaseGame} ({created.Config.GameCode}) em {created.Paths.Dir}");
            ReloadProjects();
            CurrentProject = Projects.First(p => p.Name == created.Name);
        }
    }

    [RelayCommand]
    private void OpenRootFolder() => Process.Start(new ProcessStartInfo("explorer.exe", _paths.Root) { UseShellExecute = true });

    [RelayCommand]
    private void OpenProjectFolder()
    {
        if (CurrentProject is { } p) Process.Start(new ProcessStartInfo("explorer.exe", p.Paths.Dir) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ClearLog() { _log.Clear(); LogText = ""; }
}
