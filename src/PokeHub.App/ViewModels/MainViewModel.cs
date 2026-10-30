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
    [ObservableProperty] private bool _isBusy;

    public string RootPath => _paths.Root;
    public string ProjectSummary => CurrentProject is { } p
        ? $"{p.Name}  ·  {p.Config.BaseGame} ({p.Config.GameCode})  ·  Gen {p.Config.Generation}  ·  {(WorkspaceService.IsExtracted(p) ? "workspace extraído" : "workspace vazio")}"
        : "Nenhum projeto aberto";

    private bool HasProjectAndIdle => CurrentProject is not null && !IsBusy;

    partial void OnIsBusyChanged(bool value) => NotifyProjectCommands();

    private void NotifyProjectCommands()
    {
        ExtractRomCommand.NotifyCanExecuteChanged();
        PackRomCommand.NotifyCanExecuteChanged();
        PackAndRunCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ProjectSummary));
    }

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private async Task ExtractRomAsync()
    {
        var p = CurrentProject!;
        if (WorkspaceService.IsExtracted(p))
        {
            var r = MessageBox.Show("workspace/ já contém uma ROM extraída. Extrair de novo apaga TODAS as alterações feitas nele.\n\nContinuar?",
                "Sobrescrever workspace", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        await RunBusy($"Extraindo {p.Name}...", () => WorkspaceService.ExtractAsync(p, new Progress<string>(Log), overwrite: true));
    }

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private Task PackRomAsync() => PackInternalAsync(run: false);

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private Task PackAndRunAsync() => PackInternalAsync(run: true);

    private async Task PackInternalAsync(bool run)
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log("workspace/ vazio: extraia a ROM primeiro."); return; }
        var output = await RunBusy($"Empacotando {p.Name}...", () => WorkspaceService.PackAsync(p, new Progress<string>(Log)));
        if (output is null || !run) return;

        var melon = _catalog.Get("melonds");
        var installed = melon is null ? null : _installer.GetInstalled(melon, p);
        if (melon is null || installed is null)
        {
            Log("melonDS não está instalado: instale em Emulador para rodar a ROM.");
            MessageBox.Show("melonDS não está instalado. Instale na aba Emulador.", "Emulador", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rt = _launcher.Launch(melon, installed, p);
        Log($"melonDS aberto com {output} (pid {rt.Process.Id})");
    }

    private async Task<T?> RunBusy<T>(string what, Func<Task<T>> work)
    {
        try
        {
            IsBusy = true;
            Log(what);
            return await work();
        }
        catch (Exception ex)
        {
            Log($"ERRO: {ex.Message}");
            MessageBox.Show(ex.Message, "Falha", MessageBoxButton.OK, MessageBoxImage.Error);
            return default;
        }
        finally
        {
            IsBusy = false;
            NotifyProjectCommands();
        }
    }

    public MainViewModel(HubPaths paths, HubConfig config, PluginCatalog catalog)
    {
        _paths = paths;
        _config = config;
        _catalog = catalog;
        _installer = new PluginInstaller(paths, config);
        _launcher = new ToolLauncher(paths, _installer.Runtimes);
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
        NotifyProjectCommands();
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
    private async Task NewProjectAsync()
    {
        var win = new NewProjectWindow(_projects) { Owner = Application.Current.MainWindow };
        if (win.ShowDialog() == true && win.Created is { } created)
        {
            Log($"Projeto '{created.Name}' criado: {created.Config.BaseGame} ({created.Config.GameCode}) em {created.Paths.Dir}");
            ReloadProjects();
            CurrentProject = Projects.First(p => p.Name == created.Name);
            await RunBusy("Extraindo a ROM base para workspace/...", () => WorkspaceService.ExtractAsync(CurrentProject!, new Progress<string>(Log)));
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
