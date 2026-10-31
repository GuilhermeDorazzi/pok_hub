using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.App.Views;
using PokeHub.Core.Decomp;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;
using PokeHub.Core.Runtimes;

namespace PokeHub.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly HubPaths _paths;
    private readonly HubConfig _config;
    private readonly PluginCatalog _catalog;
    private readonly PluginInstaller _installer;
    private readonly ToolLauncher _launcher;
    private readonly DecompBuildService _builds;
    private readonly WorkspaceGit _git;
    private readonly DependencyChecker _deps;
    private readonly WorkspaceService _workspace;
    private readonly ProjectService _projects;
    private readonly StringBuilder _log = new();

    public IReadOnlyList<ContextItem> Contexts { get; private set; } = ContextItem.Build();
    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;
    public ObservableCollection<ToolItemViewModel> Tools { get; } = [];
    public ObservableCollection<Project> Projects { get; } = [];

    [ObservableProperty] private ContextItem _selectedContext;
    [ObservableProperty] private LanguageOption _selectedLanguage = LanguageOption.All.First(l => l.Code == Loc.Instance.Language);
    [ObservableProperty] private Project? _currentProject;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _isBusy;

    public string RootPath => _paths.Root;
    public string ProjectSummary => CurrentProject is { } p
        ? Loc.T("vm.projectSummary", p.Name, p.Config.BaseGame, p.Config.GameCode, p.Config.Generation, WorkspaceService.FormatLabel(p))
        : Loc.T("vm.noProject");

    private bool HasProjectAndIdle => CurrentProject is not null && !IsBusy;

    partial void OnIsBusyChanged(bool value) => NotifyProjectCommands();

    private void NotifyProjectCommands()
    {
        ExtractRomCommand.NotifyCanExecuteChanged();
        PackRomCommand.NotifyCanExecuteChanged();
        PackAndRunCommand.NotifyCanExecuteChanged();
        SaveVersionCommand.NotifyCanExecuteChanged();
        ShowHistoryCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ProjectSummary));
    }

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private async Task ExtractRomAsync()
    {
        var p = CurrentProject!;
        if (WorkspaceService.IsExtracted(p))
        {
            var r = MessageBox.Show(Loc.T("vm.extractOverwrite.text"), Loc.T("vm.extractOverwrite.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        var ok = await RunBusy(Loc.T("vm.extracting", p.Name), () => _workspace.ExtractAsync(p, new Progress<string>(Log), overwrite: true));
        if (ok) await EnsureWorkspaceRepoAsync(p);
    }

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private Task PackRomAsync() => PackInternalAsync(run: false);

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private Task PackAndRunAsync() => PackInternalAsync(run: true);

    private async Task PackInternalAsync(bool run)
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        if (!GitMissing && WorkspaceGit.IsRepo(p))
        {
            try { await _git.SnapshotAsync(p, Loc.T("vm.beforePack"), new Progress<string>(Log)); }
            catch (Exception ex) { Log(Loc.T("vm.snapshotFail", ex.Message)); }
        }
        var output = await RunBusy(Loc.T("vm.packing", p.Name), () => _workspace.PackAsync(p, new Progress<string>(Log)));
        if (output is null || !run) return;

        var melon = _catalog.Get("melonds");
        var installed = melon is null ? null : _installer.GetInstalled(melon, p);
        if (melon is null || installed is null)
        {
            Log(Loc.T("vm.melonMissingLog"));
            MessageBox.Show(Loc.T("vm.melonMissingText"), Loc.T("vm.emulatorTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rt = _launcher.Launch(melon, installed, p, new Progress<string>(Log));
        Log(Loc.T("vm.melonOpened", output, rt.Process.Id));
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
            Log(Loc.T("vm.error", ex.Message));
            MessageBox.Show(ex.Message, Loc.T("vm.failTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
        _builds = new DecompBuildService(config, paths);
        _git = new WorkspaceGit(_installer.Runtimes);
        _deps = new DependencyChecker(_installer.Runtimes, _builds);
        _workspace = new WorkspaceService(_installer.Runtimes);
        _projects = new ProjectService(paths);

        _launcher.Output += (rt, line) => Log($"[{rt.PluginId}] {line}");
        _launcher.Exited += rt => _ = SnapshotAfterToolAsync(rt.PluginId);

        SelectedContext = Contexts[0];
        Log(Loc.T("vm.started", paths.Root));
        Log(Loc.T("vm.plugins", catalog.All.Count, string.Join(", ", catalog.All.Select(p => p.Id))));
        var dotnet = DependencyChecker.ReadDotNetRelease();
        Log(dotnet is null ? Loc.T("vm.dotnetMissing") : Loc.T("vm.dotnetPresent", dotnet, dotnet >= 528040 ? Loc.T("vm.dotnet48") : Loc.T("vm.dotnetOld")));
        ReloadProjects();
        if (config.LastProject is { } last) CurrentProject = Projects.FirstOrDefault(p => p.Name == last);
        RebuildTools();
        RefreshGitStatus();
        _ = DetectShellsAsync();
        _ = CheckGitAsync();
    }

    // ----- Git -----

    [ObservableProperty] private string _gitStatus = "";
    [ObservableProperty] private bool _gitMissing;

    private void RefreshGitStatus()
    {
        var git = _installer.Runtimes.ResolveGit();
        GitMissing = git is null;
        GitStatus = git is null ? Loc.T("vm.git.missing")
                  : git == "git" ? Loc.T("vm.git.system")
                  : Loc.T("vm.git.managed", _installer.Runtimes.GetInstalled(RuntimeManager.GitId)?.Version);
        InstallGitCommand.NotifyCanExecuteChanged();
        SaveVersionCommand.NotifyCanExecuteChanged();
        ShowHistoryCommand.NotifyCanExecuteChanged();
    }

    private async Task CheckGitAsync()
    {
        if (!GitMissing) { Log(GitStatus); return; }
        Log(Loc.T("vm.git.notFoundLog"));
        var r = MessageBox.Show(Loc.T("vm.git.askText"), Loc.T("vm.git.askTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes) await InstallGitAsync();
        else Log(Loc.T("vm.git.deferred"));
    }

    [RelayCommand(CanExecute = nameof(GitMissing))]
    private async Task InstallGitAsync()
    {
        await RunBusy(Loc.T("vm.git.installing"), () => _installer.Runtimes.InstallAsync(RuntimeManager.GitId, new Progress<string>(Log)));
        RefreshGitStatus();
    }

    private bool CanUseGit => CurrentProject is not null && !IsBusy && !GitMissing;

    [RelayCommand(CanExecute = nameof(CanUseGit))]
    private async Task SaveVersionAsync()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.git.wsEmptyVersion")); return; }
        await RunBusy(Loc.T("vm.git.saving"), async () =>
        {
            await _git.EnsureRepoAsync(p, new Progress<string>(Log));
            return await _git.SnapshotAsync(p, Loc.T("vm.git.userVersion"), new Progress<string>(Log));
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseGit))]
    private void ShowHistory()
    {
        var p = CurrentProject!;
        if (!WorkspaceGit.IsRepo(p)) { Log(Loc.T("vm.git.notRepo")); return; }
        var win = new HistoryWindow(_git, p, Log) { Owner = Application.Current.MainWindow };
        win.ShowDialog();
        if (win.Restored) NotifyProjectCommands();
    }

    private async Task SnapshotForEditorAsync(Project p, string message)
    {
        if (GitMissing || !WorkspaceGit.IsRepo(p)) return;
        try { await _git.SnapshotAsync(p, message, new Progress<string>(Log)); }
        catch (Exception ex) { Log(Loc.T("vm.snapshotFail", ex.Message)); }
    }

    private async Task SnapshotAfterToolAsync(string pluginId)
    {
        try
        {
            var p = CurrentProject;
            if (p is null || GitMissing || !WorkspaceGit.IsRepo(p)) return;
            await _git.SnapshotAsync(p, Loc.T("vm.git.afterTool", pluginId), new Progress<string>(Log));
        }
        catch (Exception ex) { Log(Loc.T("vm.git.afterToolFail", pluginId, ex.Message)); }
    }

    private async Task EnsureWorkspaceRepoAsync(Project p)
    {
        if (GitMissing) { Log(Loc.T("vm.git.noGitNoVersion")); return; }
        try { await _git.EnsureRepoAsync(p, new Progress<string>(Log)); }
        catch (Exception ex) { Log(Loc.T("vm.git.repoFail", ex.Message)); }
    }

    private async Task DetectShellsAsync()
    {
        try
        {
            var shells = await _builds.GetShellsAsync();
            Log(shells.Count == 0 ? Loc.T("vm.shells.none") : Loc.T("vm.shells.found", string.Join("; ", shells.Select(s => s.Description))));
            foreach (var t in Tools) t.Refresh();
        }
        catch (Exception ex) { Log(Loc.T("vm.shells.error", ex.Message)); }
    }

    private void ReloadProjects()
    {
        Projects.Clear();
        foreach (var p in _projects.ListProjects().OrderBy(p => p.Name)) Projects.Add(p);
    }

    partial void OnSelectedContextChanged(ContextItem value) => RebuildTools();

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value.Code == Loc.Instance.Language) return;
        Loc.Instance.Language = value.Code;
        HubBootstrapper.SaveLanguage(value.Code);
        var current = SelectedContext.Context;
        Contexts = ContextItem.Build();
        OnPropertyChanged(nameof(Contexts));
        SelectedContext = Contexts.First(c => c.Context == current);
        RefreshGitStatus();
        NotifyProjectCommands();
        RebuildTools();
    }

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
            Tools.Add(new ToolItemViewModel(m, _installer, _launcher, _builds, _deps, () => CurrentProject, Log, SnapshotForEditorAsync));
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
            Log(Loc.T("vm.newProject.created", created.Name, created.Config.BaseGame, created.Config.GameCode, created.Paths.Dir));
            ReloadProjects();
            CurrentProject = Projects.First(p => p.Name == created.Name);
            var ok = await RunBusy(Loc.T("vm.newProject.extracting"), () => _workspace.ExtractAsync(CurrentProject!, new Progress<string>(Log)));
            if (ok) await EnsureWorkspaceRepoAsync(CurrentProject!);
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
