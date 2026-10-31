using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PokeHub.Core.Decomp;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;
using PokeHub.Core.Runtimes;

namespace PokeHub.App.ViewModels;

public sealed partial class ToolItemViewModel : ObservableObject
{
    private readonly PluginInstaller _installer;
    private readonly ToolLauncher _launcher;
    private readonly DecompBuildService _builds;
    private readonly DependencyChecker _deps;
    private readonly Func<Project?> _currentProject;
    private readonly Action<string> _log;
    private readonly Func<Project, string, Task> _snapshot;
    private readonly Action? _installedChanged;

    public PluginManifest Manifest { get; }

    [ObservableProperty] private InstalledTool? _installed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "";

    public ToolItemViewModel(PluginManifest manifest, PluginInstaller installer, ToolLauncher launcher, DecompBuildService builds,
        DependencyChecker deps, Func<Project?> currentProject, Action<string> log, Func<Project, string, Task> snapshot, Action? installedChanged = null)
    {
        _installedChanged = installedChanged;
        Manifest = manifest;
        _installer = installer;
        _launcher = launcher;
        _builds = builds;
        _deps = deps;
        _snapshot = snapshot;
        _currentProject = currentProject;
        _log = log;
        Refresh();
    }

    public string Name => Manifest.Name;
    public string Description => Manifest.Description.Value;
    public string LicenseText => Manifest.License is { } l ? Loc.T("tool.license", l) : "";
    public string RequiresText => Manifest.Requires.Count > 0
        ? Loc.T("tool.requires") + string.Join(", ", _deps.CheckAll(Manifest.Requires).Select(d =>
            d.Satisfied is null && d.Detail == "" ? d.Name
            : d.IsMissing ? Loc.T("tool.missing", d.Name, d.Detail)
            : $"{d.Name} ({d.Detail})"))
        : "";

    public bool HasMissingDependency => _deps.CheckAll(Manifest.Requires).Any(d => d.IsMissing);

    /// <summary>Avisa sobre dependências faltantes. Devolve false se o usuário cancelou a ação.</summary>
    private bool ConfirmDependencies(string action)
    {
        var missing = _deps.CheckAll(Manifest.Requires).Where(d => d.IsMissing).ToList();
        if (missing.Count == 0) return true;
        var lines = string.Join("\n", missing.Select(d => $"• {d.Name}: {d.Detail}"));
        var withUrl = missing.FirstOrDefault(d => d.InstallUrl is not null);
        var text = Loc.T("tool.dep.text", Manifest.Name, lines) +
                   (withUrl is null ? Loc.T("tool.dep.continue", action) : Loc.T("tool.dep.options", withUrl.Name, action));
        var r = MessageBox.Show(text, Loc.T("tool.dep.title"), withUrl is null ? MessageBoxButton.YesNo : MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes && withUrl is not null)
        {
            Process.Start(new ProcessStartInfo(withUrl.InstallUrl!) { UseShellExecute = true });
            _log(Loc.T("tool.dep.opened", withUrl.InstallUrl));
            return false;
        }
        return withUrl is null ? r == MessageBoxResult.Yes : true;
    }

    public bool HasBuild => Manifest.Build is not null;
    public bool CanBuild => HasBuild && IsInstalled && !IsBusy && _currentProject() is not null;

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        var project = _currentProject();
        if (Installed is null || project is null) return;
        bool firstBuild = !Directory.Exists(Path.Combine(Installed.ExecutablePath, "build"));
        if (firstBuild && Manifest.Build?.FirstBuildNotice is { } notice)
        {
            var r = MessageBox.Show(notice.Value + Loc.T("tool.firstBuild.ask"), Loc.T("tool.firstBuild.title", Manifest.Name),
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r != MessageBoxResult.Yes) { _log(Loc.T("tool.build.cancelled", Manifest.Name)); return; }
        }
        try
        {
            IsBusy = true;
            StatusText = firstBuild ? Loc.T("tool.status.buildingFirst") : Loc.T("tool.status.building");
            var rom = await _builds.BuildAsync(Manifest, Installed, project, new Progress<string>(_log));
            _log(Loc.T("tool.build.done", Manifest.Name, rom));
        }
        catch (Exception ex)
        {
            _log(Loc.T("tool.build.error", Manifest.Name, ex.Message));
            MessageBox.Show(ex.Message, Loc.T("tool.build.errorTitle", Manifest.Name), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }
    public string? Notes => Manifest.Notes?.Value;
    public bool HasNotes => Manifest.Notes is { IsEmpty: false };
    public string? Purpose => Manifest.Purpose?.Value;
    public string? Cost => Manifest.Cost?.Value;
    public bool HasPurpose => Manifest.Purpose is { IsEmpty: false };
    public bool HasCost => Manifest.Cost is { IsEmpty: false };

    /// <summary>Instalações pesadas pedem confirmação mostrando o propósito e o custo.</summary>
    private bool ConfirmHeavyInstall()
    {
        if (!Manifest.ConfirmInstall || IsInstalled) return true;
        var text = Loc.T("tool.confirm.text", Manifest.Name, Purpose, Cost);
        return MessageBox.Show(text, Loc.T("tool.confirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
    }
    public bool IsInstalled => Installed is not null;
    public bool IsBuiltin => Manifest.Source.Type == SourceType.Builtin;
    public bool IsRuntime => Manifest.Source.Type == SourceType.Runtime;
    public bool CanInstallOrUpdate => !IsBuiltin;
    public string InstallButtonText => IsSystemSource ? Loc.T("tool.installBtn.system")
        : IsRuntime && IsInstalled ? Loc.T("tool.installBtn.prepare")
        : IsInstalled ? Loc.T("tool.installBtn.update") : Loc.T("tool.installBtn.install");
    public bool CanLaunch => IsInstalled && Manifest.Launch is not null && !IsBusy && (!IsBuiltin || _currentProject() is not null);
    public bool IsSystemSource => Manifest.Source.Type == SourceType.System;

    public void Refresh()
    {
        Installed = _installer.GetInstalled(Manifest, _currentProject());
        StatusText = IsBuiltin ? (_currentProject() is null ? Loc.T("tool.status.openProject") : Loc.T("tool.status.builtin"))
            : Installed is null
            ? (Manifest.Scope == PluginScope.Project && _currentProject() is null ? Loc.T("tool.status.projectScope") : Loc.T("tool.status.notInstalled"))
            : Loc.T("tool.status.installed", Installed.Version);
        NotifyAll();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(RequiresText));
        OnPropertyChanged(nameof(HasMissingDependency));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(CanBuild));
        OnPropertyChanged(nameof(InstallButtonText));
        InstallCommand.NotifyCanExecuteChanged();
        BuildCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => NotifyAll();
    partial void OnInstalledChanged(InstalledTool? value) => NotifyAll();

    private bool CanInstall() => !IsBusy && !(Manifest.Scope == PluginScope.Project && _currentProject() is null);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        var project = _currentProject();
        if (!ConfirmDependencies(Loc.T("tool.action.install"))) return;
        if (!ConfirmHeavyInstall()) { _log(Loc.T("tool.install.cancelled", Manifest.Name)); return; }
        try
        {
            IsBusy = true;
            if (IsSystemSource)
            {
                var dlg = new OpenFileDialog { Title = Loc.T("tool.exeDialog", Manifest.Name), Filter = Loc.T("tool.exeFilter") };
                if (dlg.ShowDialog() != true) return;
                Installed = _installer.RegisterSystemTool(Manifest, project, dlg.FileName);
                _log(Loc.T("tool.registered", Manifest.Name, dlg.FileName));
            }
            else
            {
                StatusText = Loc.T("tool.status.installing");
                var progress = new Progress<string>(_log);
                Installed = await _installer.InstallAsync(Manifest, project, progress);
                if (Manifest.Source.Type == SourceType.Runtime && Manifest.Setup?.Pacman is { Count: > 0 } packages)
                {
                    StatusText = Loc.T("tool.status.preparing");
                    var shells = await _builds.GetShellsAsync(refresh: true);
                    var msysRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Installed.ExecutablePath)!, "..", ".."));
                    var shell = shells.FirstOrDefault(s => s.Kind == UnixShellKind.Msys2 && string.Equals(Path.GetFullPath(s.Location), msysRoot, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(Loc.T("tool.msysNotDetected"));
                    await _builds.PrepareMsys2ToolchainAsync(shell, packages, progress, libpngVersion: Manifest.Setup.LibpngVersion);
                    await _builds.GetShellsAsync(refresh: true);
                }
            }
        }
        catch (Exception ex)
        {
            _log(Loc.T("tool.installError", Manifest.Name, ex.Message));
            MessageBox.Show(ex.Message, Loc.T("tool.installErrorTitle", Manifest.Name), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            Refresh();
            _installedChanged?.Invoke();
        }
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Launch()
    {
        if (Installed is null) return;
        if (!ConfirmDependencies(Loc.T("tool.action.open"))) return;
        if (IsBuiltin)
        {
            var project = _currentProject();
            if (project is null) return;
            try { BuiltinEditors.Open(Manifest.Id, project, _log, msg => _snapshot(project, msg)); }
            catch (Exception ex)
            {
                _log(Loc.T("tool.openError", Manifest.Name, ex.Message));
                MessageBox.Show(ex.Message, Loc.T("tool.openErrorTitle", Manifest.Name), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }
        try
        {
            var rt = _launcher.Launch(Manifest, Installed, _currentProject(), new Progress<string>(_log));
            IsRunning = true;
            _log(Loc.T("tool.opened", Manifest.Name, rt.Process.Id));
            rt.Process.Exited += (_, _) => Application.Current.Dispatcher.Invoke(() =>
            {
                IsRunning = false;
                _log(Loc.T("tool.closed", Manifest.Name, rt.Process.ExitCode));
            });
        }
        catch (Exception ex)
        {
            _log(Loc.T("tool.openError", Manifest.Name, ex.Message));
            MessageBox.Show(ex.Message, Loc.T("tool.openErrorTitle", Manifest.Name), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(IsInstalled))]
    private void OpenFolder()
    {
        if (Installed is null) return;
        var dir = Directory.Exists(Installed.ExecutablePath) ? Installed.ExecutablePath : Path.GetDirectoryName(Installed.ExecutablePath);
        if (dir is not null) Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        if (Manifest.Homepage is { } url) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
