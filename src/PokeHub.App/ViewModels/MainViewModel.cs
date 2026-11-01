using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PokeHub.App.Views;
using PokeHub.Core.Decomp;
using PokeHub.Core.Gen4;
using PokeHub.Core.Help;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Patch;
using PokeHub.Core.Reports;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;
using PokeHub.Core.Runtimes;

namespace PokeHub.App.ViewModels;

public enum HubSection { Home, Project, Tools, Help, Settings }

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
    private readonly GitHubReleaseClient _releases = new();
    private readonly GuideCatalog _guides = GuideCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "guides"));
    private readonly StringBuilder _log = new();

    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;
    public ObservableCollection<Project> Projects { get; } = [];

    /// <summary>Todas as ferramentas do catálogo para a geração do projeto aberto (inclui editores embutidos).</summary>
    public List<ToolItemViewModel> AllTools { get; } = [];
    public ObservableCollection<ToolGroupViewModel> ToolGroups { get; } = [];
    public ObservableCollection<FeatureCardGroup> CardGroups { get; } = [];

    [ObservableProperty] private LanguageOption _selectedLanguage = LanguageOption.All.First(l => l.Code == Loc.Instance.Language);
    [ObservableProperty] private Project? _currentProject;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _lastLogLine = "";
    [ObservableProperty] private bool _isBusy;

    // ----- navegação -----

    [ObservableProperty] private HubSection _section = HubSection.Home;

    public bool IsHome { get => Section == HubSection.Home; set { if (value) Section = HubSection.Home; } }
    public bool IsProjectSection { get => Section == HubSection.Project; set { if (value) Section = HubSection.Project; } }
    public bool IsTools { get => Section == HubSection.Tools; set { if (value) Section = HubSection.Tools; } }
    public bool IsHelp { get => Section == HubSection.Help; set { if (value) Section = HubSection.Help; } }
    public bool IsSettings { get => Section == HubSection.Settings; set { if (value) Section = HubSection.Settings; } }

    partial void OnSectionChanged(HubSection value)
    {
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(IsProjectSection));
        OnPropertyChanged(nameof(IsTools));
        OnPropertyChanged(nameof(IsHelp));
        OnPropertyChanged(nameof(IsSettings));
    }

    [RelayCommand]
    private void ShowTools() => Section = HubSection.Tools;

    // ----- ajuda -----

    public ObservableCollection<GuideViewModel> Guides { get; } = [];
    /// <summary>Guias agrupados (Começando, Editar o jogo, Testar, Avançado) para a lista da seção Ajuda.</summary>
    public System.ComponentModel.ICollectionView GuidesView { get; }
    [ObservableProperty] private GuideViewModel? _selectedGuide;

    private void RebuildGuides()
    {
        var current = SelectedGuide?.Id;
        Guides.Clear();
        foreach (var g in _guides.All.OrderBy(g => Array.IndexOf(GuideCatalog.GroupOrder, g.Group))) Guides.Add(new GuideViewModel(g));
        SelectedGuide = Guides.FirstOrDefault(g => g.Id == current) ?? Guides.FirstOrDefault();
    }

    /// <summary>Abre a seção Ajuda num guia (id do guia, chave de card ou id de plugin).</summary>
    [RelayCommand]
    private void ShowGuide(string? key)
    {
        if (key is not null)
        {
            var g = _guides.Get(key) ?? _guides.ForFeature(key) ?? _guides.ForTool(key);
            if (g is not null) SelectedGuide = Guides.FirstOrDefault(v => v.Id == g.Id);
        }
        Section = HubSection.Help;
    }

    public string? GuideIdFor(string featureKey) => _guides.ForFeature(featureKey)?.Id;
    public string? GuideIdForTool(string pluginId) => _guides.ForTool(pluginId)?.Id;

    // ----- projeto -----

    public string RootPath => _paths.Root;
    public bool HasProject => CurrentProject is not null;
    public string ProjectTitle => CurrentProject?.Name ?? Loc.T("vm.noProject");
    public string ProjectSummary => CurrentProject is { } p
        ? Loc.T("vm.projectSummary", p.Name, p.Config.BaseGame, p.Config.GameCode, p.Config.Generation, WorkspaceService.FormatLabel(p))
        : Loc.T("project.none");
    public string ProjectGame => CurrentProject is { } p ? $"{p.Config.BaseGame} ({p.Config.GameCode})" : "";
    public string ProjectGeneration => CurrentProject is { } p ? Loc.T("project.gen", p.Config.Generation) : "";
    public string ProjectWorkspace => CurrentProject is { } p ? WorkspaceService.FormatLabel(p) : "";
    public string ProjectDir => CurrentProject?.Paths.Dir ?? "";

    private bool HasProjectAndIdle => CurrentProject is not null && !IsBusy;

    partial void OnIsBusyChanged(bool value) => NotifyProjectCommands();

    private void NotifyProjectCommands()
    {
        ExtractRomCommand.NotifyCanExecuteChanged();
        PackRomCommand.NotifyCanExecuteChanged();
        PlayCommand.NotifyCanExecuteChanged();
        SaveVersionCommand.NotifyCanExecuteChanged();
        ShowHistoryCommand.NotifyCanExecuteChanged();
        OpenProjectFolderCommand.NotifyCanExecuteChanged();
        CreatePatchCommand.NotifyCanExecuteChanged();
        OpenBatchCommand.NotifyCanExecuteChanged();
        OpenCodePatchesCommand.NotifyCanExecuteChanged();
        OpenNarcBrowserCommand.NotifyCanExecuteChanged();
        OpenStatsCommand.NotifyCanExecuteChanged();
        OpenTablesCommand.NotifyCanExecuteChanged();
        OpenScriptsCommand.NotifyCanExecuteChanged();
        OpenNewEventCommand.NotifyCanExecuteChanged();
        OpenCatchCommand.NotifyCanExecuteChanged();
        OpenPostEliteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasProject));
        OnPropertyChanged(nameof(ProjectTitle));
        OnPropertyChanged(nameof(ProjectSummary));
        OnPropertyChanged(nameof(ProjectGame));
        OnPropertyChanged(nameof(ProjectGeneration));
        OnPropertyChanged(nameof(ProjectWorkspace));
        OnPropertyChanged(nameof(ProjectDir));
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

    // ----- patch BPS -----

    /// <summary>Gera builds/&lt;nome&gt;.bps: diferença entre a ROM base e a ROM empacotada agora.</summary>
    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private async Task CreatePatchAsync()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        var output = await RunBusy(Loc.T("vm.packing", p.Name), () => _workspace.PackAsync(p, new Progress<string>(Log)));
        if (output is null) return;
        var patchFile = Path.Combine(p.Paths.Builds, p.Name + ".bps");
        var result = await RunBusy(Loc.T("patch.creating"), async () =>
        {
            var source = await File.ReadAllBytesAsync(p.Paths.BaseRom);
            var target = await File.ReadAllBytesAsync(output);
            var patch = await Task.Run(() => Bps.Create(source, target, $"PokeHub {p.Name}"));
            await File.WriteAllBytesAsync(patchFile, patch);
            return patch.Length;
        });
        if (result == 0) return;
        Log(Loc.T("patch.created", patchFile, result / 1024));
        MessageBox.Show(Loc.T("patch.createdText", patchFile, result / 1024), Loc.T("patch.title"), MessageBoxButton.OK, MessageBoxImage.Information);
        Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + patchFile + "\"") { UseShellExecute = true });
    }

    /// <summary>Aplica um .bps sobre uma ROM limpa e salva a ROM resultante; o usuário pode criar um projeto a partir dela.</summary>
    [RelayCommand]
    private async Task ApplyPatchAsync()
    {
        var romDlg = new OpenFileDialog { Title = Loc.T("patch.pickRom"), Filter = Loc.T("np.filter") };
        if (romDlg.ShowDialog() != true) return;
        var patchDlg = new OpenFileDialog { Title = Loc.T("patch.pickPatch"), Filter = Loc.T("patch.filter") };
        if (patchDlg.ShowDialog() != true) return;
        var saveDlg = new SaveFileDialog { Title = Loc.T("patch.pickOutput"), Filter = Loc.T("np.filter"),
            FileName = Path.GetFileNameWithoutExtension(patchDlg.FileName) + ".nds", InitialDirectory = Path.GetDirectoryName(patchDlg.FileName) };
        if (saveDlg.ShowDialog() != true) return;
        var ok = await RunBusy(Loc.T("patch.applying"), async () =>
        {
            var source = await File.ReadAllBytesAsync(romDlg.FileName);
            var patch = await File.ReadAllBytesAsync(patchDlg.FileName);
            var target = await Task.Run(() => Bps.Apply(source, patch));
            await File.WriteAllBytesAsync(saveDlg.FileName, target);
            return true;
        });
        if (!ok) return;
        Log(Loc.T("patch.applied", saveDlg.FileName));
        var r = MessageBox.Show(Loc.T("patch.appliedText", saveDlg.FileName), Loc.T("patch.title"), MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (r == MessageBoxResult.Yes) await NewProjectAsync(saveDlg.FileName);
    }

    // ----- novo evento -----

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenNewEvent()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        try
        {
            var game = Gen4Game.For(p);
            if (!PokeHub.Core.Gen4.Scripts.NewEventService.Supports(game)) { Log(Loc.T("core.newev.hgssOnly")); return; }
            var text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
            var items = Gen4TextArchive.Read(text.Files[game.ItemsBank]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList();
            var species = Gen4TextArchive.Read(text.Files[game.SpeciesBank]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList();
            var vm = new NewEventViewModel(p, PokeHub.Core.Gen4.Scripts.EventMap.Load(p), items, species, Log, message => SnapshotForEditorAsync(p, message));
            new NewEventWindow(vm) { Owner = Application.Current.MainWindow }.Show();
        }
        catch (Exception ex) { Log(Loc.T("vm.error", ex.Message)); }
    }

    // ----- editor de scripts -----

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenScripts()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        try
        {
            var vm = new ScriptEditorViewModel(PokeHub.Core.Gen4.Scripts.ScriptArchive.Load(p), Log, message => SnapshotForEditorAsync(p, message));
            new ScriptEditorWindow(vm) { Owner = Application.Current.MainWindow }.Show();
        }
        catch (Exception ex) { Log(Loc.T("vm.error", ex.Message)); }
    }

    // ----- tabelas do jogo -----

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenTables()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        try
        {
            var game = Gen4Game.For(p);
            var text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
            List<string> Bank(int b) => Gen4TextArchive.Read(text.Files[b]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList();
            var vm = new GameTablesViewModel(GameTables.Load(p), Bank(game.SpeciesBank), Bank(game.MovesBank), Bank(game.ItemsBank), Bank(game.AbilitiesBank), Log, message => SnapshotForEditorAsync(p, message));
            new GameTablesWindow(vm) { Owner = Application.Current.MainWindow }.Show();
        }
        catch (Exception ex) { Log(Loc.T("vm.error", ex.Message)); }
    }

    // ----- stats dos Pokémon -----

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenStats()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        try
        {
            var vm = new StatsEditorViewModel(PersonalRepository.Load(p), Log, message => SnapshotForEditorAsync(p, message));
            new StatsEditorWindow(vm) { Owner = Application.Current.MainWindow }.Show();
        }
        catch (Exception ex) { Log(Loc.T("vm.error", ex.Message)); }
    }

    // ----- navegador de gráficos -----

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenNarcBrowser()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        try { new NarcBrowserWindow(new NarcBrowserViewModel(p, SnapshotForEditorAsync)) { Owner = Application.Current.MainWindow }.Show(); }
        catch (Exception ex) { Log(Loc.T("vm.error", ex.Message)); }
    }

    // ----- injeção de código -----

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenCodePatches()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        new CodePatchWindow(new CodePatchViewModel(p, _paths, Log, SnapshotForEditorAsync)) { Owner = Application.Current.MainWindow }.ShowDialog();
    }

    // ----- operações em lote -----

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenPostElite()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        if (!Gen4Game.For(p).IsHgss) { Log(Loc.T("core.postElite.hgssOnly")); return; }
        new PostEliteWindow(new PostEliteViewModel(p, _paths, Log, message => SnapshotForEditorAsync(p, message))) { Owner = Application.Current.MainWindow }.Show();
    }

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenCatch()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        try
        {
            new CatchWindow(new CatchViewModel(CatchAudit.Build(p))) { Owner = Application.Current.MainWindow }.Show();
        }
        catch (Exception ex) { Log(Loc.T("vm.error", ex.Message)); }
    }

    [RelayCommand(CanExecute = nameof(HasProjectAndIdle))]
    private void OpenBatch()
    {
        var p = CurrentProject!;
        if (!WorkspaceService.IsExtracted(p)) { Log(Loc.T("vm.wsEmptyExtract")); return; }
        try
        {
            var vm = new BatchViewModel(p, Log, SnapshotForEditorAsync);
            new BatchWindow(vm) { Owner = Application.Current.MainWindow }.ShowDialog();
            if (vm.Applied) NotifyProjectCommands();
        }
        catch (Exception ex)
        {
            Log(Loc.T("vm.error", ex.Message));
            MessageBox.Show(ex.Message, Loc.T("batch.title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>"O que mudou" entre uma versão e o workspace atual, em termos de jogo.</summary>
    public async Task<IReadOnlyList<ChangeLine>> CompareVersionAsync(Project p, string hash)
    {
        var changed = await _git.ChangedFilesAsync(p, hash);
        if (changed.Count == 0) return [];
        var temp = Path.Combine(Path.GetTempPath(), "pokehub-compare", Guid.NewGuid().ToString("N"));
        try
        {
            var old = await ChangeReport.ExportVersionAsync(_git, p, hash, temp);
            return await Task.Run(() => ChangeReport.Compare(old, p, changed));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    // ----- emuladores e Testar/Parar -----

    public ObservableCollection<EmulatorOption> Emulators { get; } = [];
    [ObservableProperty] private EmulatorOption? _selectedEmulator;
    [ObservableProperty] private bool _isEmulatorRunning;
    private RunningTool? _emulatorRun;

    public bool HasEmulators => Emulators.Count > 0;
    public string PlayButtonText => IsEmulatorRunning ? Loc.T("main.stop") : Loc.T("main.play");

    /// <summary>Emuladores instalados (contexto Emulator); mantém a escolha salva em hub.json quando ainda está instalada.</summary>
    public void RefreshEmulators()
    {
        var current = SelectedEmulator?.Id ?? _config.PreferredEmulator;
        Emulators.Clear();
        foreach (var m in _catalog.ForContext(ToolContext.Emulator, CurrentProject?.Config.Generation))
            if (_installer.GetInstalled(m, CurrentProject) is not null) Emulators.Add(new EmulatorOption(m.Id, m.Name));
        SelectedEmulator = Emulators.FirstOrDefault(e => e.Id == current) ?? Emulators.FirstOrDefault();
        OnPropertyChanged(nameof(HasEmulators));
        OnPropertyChanged(nameof(RuntimesSummary));
        PlayCommand.NotifyCanExecuteChanged();
        RefreshCards();
        RefreshUpdatesCount();
    }

    partial void OnSelectedEmulatorChanged(EmulatorOption? value)
    {
        if (value is null || value.Id == _config.PreferredEmulator) return;
        _config.PreferredEmulator = value.Id;
        _config.Save(_paths.ConfigFile);
    }

    partial void OnIsEmulatorRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayButtonText));
        PlayCommand.NotifyCanExecuteChanged();
        RefreshCards();
    }

    private bool CanPlay => IsEmulatorRunning || (HasProjectAndIdle && SelectedEmulator is not null);

    /// <summary>Testar: empacota e abre no emulador escolhido. Parar: encerra o emulador aberto pelo hub.</summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private async Task PlayAsync()
    {
        if (IsEmulatorRunning)
        {
            try { _emulatorRun?.Process.Kill(entireProcessTree: true); Log(Loc.T("vm.emulatorStopped")); }
            catch (Exception ex) { Log(Loc.T("vm.error", ex.Message)); }
            return;
        }
        await PackInternalAsync(run: true);
    }

    /// <summary>Card "Emulador" da tela inicial: Testar quando há emulador, senão leva à seção Ferramentas.</summary>
    [RelayCommand]
    private async Task EmulatorCardAsync()
    {
        if (!HasEmulators) { Section = HubSection.Tools; return; }
        if (PlayCommand.CanExecute(null)) await PlayAsync();
    }

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

        var emulator = SelectedEmulator is { } sel ? _catalog.Get(sel.Id) : null;
        var installed = emulator is null ? null : _installer.GetInstalled(emulator, p);
        if (emulator is null || installed is null)
        {
            Log(Loc.T("vm.emulatorMissingLog"));
            MessageBox.Show(Loc.T("vm.emulatorMissingText"), Loc.T("vm.emulatorTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var rt = _launcher.Launch(emulator, installed, p, new Progress<string>(Log));
        _emulatorRun = rt;
        IsEmulatorRunning = true;
        rt.Process.Exited += (_, _) => Application.Current.Dispatcher.Invoke(() => { if (ReferenceEquals(_emulatorRun, rt)) { _emulatorRun = null; IsEmulatorRunning = false; } });
        Log(Loc.T("vm.emulatorOpened", emulator.Name, output, rt.Process.Id));
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

        ToolsFilters = BuildFilters();
        SelectedToolsFilter = ToolsFilters[0];
        GuidesView = System.Windows.Data.CollectionViewSource.GetDefaultView(Guides);
        GuidesView.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(GuideViewModel.GroupTitle)));
        Log(Loc.T("vm.started", paths.Root));
        Log(Loc.T("vm.plugins", catalog.All.Count, string.Join(", ", catalog.All.Select(p => p.Id))));
        var dotnet = DependencyChecker.ReadDotNetRelease();
        Log(dotnet is null ? Loc.T("vm.dotnetMissing") : Loc.T("vm.dotnetPresent", dotnet, dotnet >= 528040 ? Loc.T("vm.dotnet48") : Loc.T("vm.dotnetOld")));
        ReloadProjects();
        RebuildGuides();
        if (config.LastProject is { } last) CurrentProject = Projects.FirstOrDefault(p => p.Name == last);
        RebuildTools();
        RefreshGitStatus();
        RefreshDependencies();
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
        OnPropertyChanged(nameof(RuntimesSummary));
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
        var win = new HistoryWindow(_git, p, Log, hash => CompareVersionAsync(p, hash)) { Owner = Application.Current.MainWindow };
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

    // ----- shells (decomp) -----

    public ObservableCollection<UnixShell> Shells { get; } = [];
    [ObservableProperty] private UnixShell? _selectedShell;
    public bool HasShells => Shells.Count > 0;

    private async Task DetectShellsAsync()
    {
        try
        {
            var shells = await _builds.GetShellsAsync();
            Log(shells.Count == 0 ? Loc.T("vm.shells.none") : Loc.T("vm.shells.found", string.Join("; ", shells.Select(s => s.Description))));
            Shells.Clear();
            foreach (var s in shells) Shells.Add(s);
            SelectedShell = await _builds.ResolveShellAsync();
            OnPropertyChanged(nameof(HasShells));
            foreach (var t in AllTools) t.Refresh();
            RefreshDependencies();
        }
        catch (Exception ex) { Log(Loc.T("vm.shells.error", ex.Message)); }
    }

    partial void OnSelectedShellChanged(UnixShell? value)
    {
        if (value is not null) _builds.SetPreferredShell(value);
    }

    // ----- dependências do Windows (seção Configurações) -----

    public ObservableCollection<DependencyStatus> Dependencies { get; } = [];

    [RelayCommand]
    private void RefreshDependencies()
    {
        Dependencies.Clear();
        foreach (var d in _deps.CheckAll(["dotnet-framework-4.8", "vcredist-x64", "unix-shell"])) Dependencies.Add(d);
    }

    /// <summary>Runtimes que o hub instalou sozinho (Java, Git, ds-rom, MSYS2).</summary>
    public string RuntimesSummary => _config.InstalledRuntimes.Count == 0
        ? Loc.T("tools.runtimesNone")
        : Loc.T("tools.runtimes", string.Join(", ", _config.InstalledRuntimes.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value.Version}")));

    // ----- seção Ferramentas: filtro e atualizações -----

    public IReadOnlyList<ToolsFilterOption> ToolsFilters { get; private set; }
    [ObservableProperty] private ToolsFilterOption _selectedToolsFilter;
    [ObservableProperty] private bool _isCheckingUpdates;
    [ObservableProperty] private int _updatesCount;
    public bool HasUpdates => UpdatesCount > 0;

    private static IReadOnlyList<ToolsFilterOption> BuildFilters() =>
        [new(ToolsFilter.All), new(ToolsFilter.Installed), new(ToolsFilter.Updates)];

    partial void OnSelectedToolsFilterChanged(ToolsFilterOption value) => ApplyToolsFilter();

    private void ApplyToolsFilter()
    {
        foreach (var g in ToolGroups) g.ApplyFilter(SelectedToolsFilter.Filter);
    }

    partial void OnUpdatesCountChanged(int value) => OnPropertyChanged(nameof(HasUpdates));

    private void RefreshUpdatesCount()
    {
        UpdatesCount = AllTools.Count(t => t.UpdateAvailable);
        if (SelectedToolsFilter.Filter == ToolsFilter.Updates) ApplyToolsFilter();
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates) return;
        try
        {
            IsCheckingUpdates = true;
            Log(Loc.T("tools.checking"));
            await Task.WhenAll(AllTools.Select(t => t.CheckUpdateAsync(_releases)));
            RefreshUpdatesCount();
            Log(UpdatesCount == 0 ? Loc.T("tools.upToDate") : Loc.T("tools.updatesFound", UpdatesCount));
        }
        finally { IsCheckingUpdates = false; }
    }

    // ----- projetos, idioma e reconstrução -----

    private void ReloadProjects()
    {
        Projects.Clear();
        foreach (var p in _projects.ListProjects().OrderBy(p => p.Name)) Projects.Add(p);
    }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value.Code == Loc.Instance.Language) return;
        Loc.Instance.Language = value.Code;
        HubBootstrapper.SaveLanguage(value.Code);
        var filter = SelectedToolsFilter.Filter;
        ToolsFilters = BuildFilters();
        OnPropertyChanged(nameof(ToolsFilters));
        SelectedToolsFilter = ToolsFilters.First(f => f.Filter == filter);
        RefreshGitStatus();
        RefreshDependencies();
        NotifyProjectCommands();
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(AppVersion));
        OnPropertyChanged(nameof(RuntimesSummary));
        RebuildGuides();
        RebuildTools();
    }

    partial void OnCurrentProjectChanged(Project? value)
    {
        _config.LastProject = value?.Name;
        _config.Save(_paths.ConfigFile);
        NotifyProjectCommands();
        RebuildTools();
    }

    /// <summary>Recria as ferramentas, os grupos da seção Ferramentas e os cards da tela inicial.</summary>
    private void RebuildTools()
    {
        var latest = AllTools.ToDictionary(t => t.Manifest.Id, t => t.LatestVersion, StringComparer.OrdinalIgnoreCase);
        AllTools.Clear();
        // GEN5: quando houver projeto Gen 5 aberto, o filtro por geração esconde os plugins só de Gen 4.
        var gen = CurrentProject?.Config.Generation;
        foreach (var m in _catalog.All.Where(m => gen is null || m.Generations.Contains(gen.Value)).OrderBy(m => m.Name))
        {
            var t = new ToolItemViewModel(m, _installer, _launcher, _builds, _deps, () => CurrentProject, Log, SnapshotForEditorAsync, RefreshEmulators) { GuideId = GuideIdForTool(m.Id) };
            if (latest.TryGetValue(m.Id, out var v)) t.LatestVersion = v;
            AllTools.Add(t);
        }

        ToolGroups.Clear();
        foreach (var key in new[] { "editors", "emulators", "decomp" })
            ToolGroups.Add(new ToolGroupViewModel(key, AllTools.Where(t => !t.IsBuiltin && ToolGroupViewModel.GroupOf(t.Manifest) == key)));
        ApplyToolsFilter();

        BuildCards();
        RefreshEmulators();
    }

    private ToolItemViewModel? Tool(string id) => AllTools.FirstOrDefault(t => t.Manifest.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Decomp correspondente ao jogo do projeto. Sem projeto, mostra a de HGSS para o card existir.</summary>
    private ToolItemViewModel? DecompTool() => CurrentProject?.Config.BaseGame switch
    {
        BaseGame.HeartGold or BaseGame.SoulSilver or null => Tool("pokeheartgold"),
        BaseGame.Platinum => Tool("pokeplatinum"),
        // GEN5: mapear Black/White para as decomps correspondentes quando existirem no catálogo.
        _ => null,
    };

    private void BuildCards()
    {
        bool Extracted() => CurrentProject is not null && WorkspaceService.IsExtracted(CurrentProject);
        string Open() => Loc.T("main.open");
        string NeedsProject() => Loc.T("tool.status.openProject");
        FeatureCardViewModel[] cards =
        [
            new("maps", FeatureGroup.Maps, Tool("dspre")),
            new("newevent", FeatureGroup.Maps, null, OpenNewEventCommand, Extracted, Open, NeedsProject),
            new("events", FeatureGroup.Maps, Tool("event-map")),
            new("scripts", FeatureGroup.Maps, null, OpenScriptsCommand, Extracted, Open, NeedsProject),
            new("encounters", FeatureGroup.Maps, Tool("encounter-editor")),
            new("newmaps", FeatureGroup.Maps, Tool("pdsms")),
            new("postelite", FeatureGroup.Maps, null, OpenPostEliteCommand, Extracted, Open, NeedsProject),

            new("stats", FeatureGroup.Pokemon, null, OpenStatsCommand, Extracted, Open, NeedsProject),
            new("trainers", FeatureGroup.Pokemon, Tool("trainer-editor")),
            new("tables", FeatureGroup.Pokemon, null, OpenTablesCommand, Extracted, Open, NeedsProject),
            new("pokedex", FeatureGroup.Pokemon, Tool("pokedex-area-editor")),
            new("pokemon", FeatureGroup.Pokemon, Tool("pokeditor")),
            new("catch", FeatureGroup.Pokemon, null, OpenCatchCommand, Extracted, Open, NeedsProject),

            new("text", FeatureGroup.Media, Tool("text-editor")),
            new("graphics", FeatureGroup.Media, Tool("nitropaint")),
            new("narcs", FeatureGroup.Media, null, OpenNarcBrowserCommand, Extracted, Open, NeedsProject),
            new("audio", FeatureGroup.Media, Tool("nitrostudio2")),
            new("models", FeatureGroup.Media, Tool("apicula")),

            new("emulator", FeatureGroup.Release, null, EmulatorCardCommand, () => HasEmulators, () => HasEmulators ? PlayButtonText : Loc.T("feature.goTools"), null,
                () => SelectedEmulator is { } emulator ? Tool(emulator.Id)?.ProgramIcon : null),
            new("patch", FeatureGroup.Release, null, CreatePatchCommand, Extracted, () => Loc.T("patch.create"), NeedsProject),
            new("randomizer", FeatureGroup.Release, Tool("randomizer-zx")),

            new("decomp", FeatureGroup.Advanced, DecompTool()),
            new("inject", FeatureGroup.Advanced, null, OpenCodePatchesCommand, Extracted, Open, NeedsProject),
            new("batch", FeatureGroup.Advanced, null, OpenBatchCommand, Extracted, Open, NeedsProject),
            new("files", FeatureGroup.Advanced, Tool("tinke")),
        ];
        CardGroups.Clear();
        foreach (var group in cards.GroupBy(c => c.Group)) CardGroups.Add(new FeatureCardGroup(group.Key, group.ToList()));
        RefreshCards();
    }

    private void RefreshCards()
    {
        foreach (var c in CardGroups.SelectMany(g => g.Cards))
        {
            c.GuideId = GuideIdFor(c.Key);
            c.Refresh();
        }
    }

    public void Log(string line)
    {
        void Append()
        {
            _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(line);
            LogText = _log.ToString();
            LastLogLine = line;
        }
        if (Application.Current?.Dispatcher.CheckAccess() ?? true) Append();
        else Application.Current!.Dispatcher.Invoke(Append);
    }

    [RelayCommand]
    private Task NewProjectAsync() => NewProjectAsync(null);

    private async Task NewProjectAsync(string? romPath)
    {
        var win = new NewProjectWindow(_projects, romPath) { Owner = Application.Current.MainWindow };
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

    [RelayCommand(CanExecute = nameof(HasProject))]
    private void OpenProjectFolder()
    {
        if (CurrentProject is { } p) Process.Start(new ProcessStartInfo("explorer.exe", p.Paths.Dir) { UseShellExecute = true });
    }

    /// <summary>Troca a pasta raiz do hub: grava em settings.json e pede para reabrir o PokeHub.</summary>
    [RelayCommand]
    private void ChangeRoot()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("settings.changeRoot.dialog"), Multiselect = false };
        if (dlg.ShowDialog() != true || string.Equals(dlg.FolderName, _paths.Root, StringComparison.OrdinalIgnoreCase)) return;
        HubBootstrapper.Initialize(dlg.FolderName);
        Log(Loc.T("settings.changeRoot.log", dlg.FolderName));
        var r = MessageBox.Show(Loc.T("settings.changeRoot.text", dlg.FolderName), Loc.T("settings.changeRoot.title"), MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (r == MessageBoxResult.Yes) Application.Current.Shutdown();
    }

    public string AppVersion => Loc.T("settings.version", typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?");
    public const string RepositoryUrl = "https://github.com/GuilhermeDorazzi/pok_hub";

    [RelayCommand]
    private void OpenRepository() => Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });

    [RelayCommand]
    private void ClearLog() { _log.Clear(); LogText = ""; LastLogLine = ""; }
}
