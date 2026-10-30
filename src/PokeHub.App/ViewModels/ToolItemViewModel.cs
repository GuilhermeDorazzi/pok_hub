using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

public sealed partial class ToolItemViewModel : ObservableObject
{
    private readonly PluginInstaller _installer;
    private readonly ToolLauncher _launcher;
    private readonly Func<Project?> _currentProject;
    private readonly Action<string> _log;

    public PluginManifest Manifest { get; }

    [ObservableProperty] private InstalledTool? _installed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "";

    public ToolItemViewModel(PluginManifest manifest, PluginInstaller installer, ToolLauncher launcher,
        Func<Project?> currentProject, Action<string> log)
    {
        Manifest = manifest;
        _installer = installer;
        _launcher = launcher;
        _currentProject = currentProject;
        _log = log;
        Refresh();
    }

    public string Name => Manifest.Name;
    public string Description => Manifest.Description;
    public string LicenseText => Manifest.License is { } l ? $"Licença: {l}" : "";
    public string RequiresText => Manifest.Requires.Count > 0 ? $"Requer: {string.Join(", ", Manifest.Requires)}" : "";
    public string? Notes => Manifest.Notes;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Manifest.Notes);
    public bool IsInstalled => Installed is not null;
    public bool CanLaunch => IsInstalled && Manifest.Launch is not null && !IsBusy;
    public bool IsSystemSource => Manifest.Source.Type == SourceType.System;
    public string InstallButtonText => IsSystemSource ? "Apontar executável" : IsInstalled ? "Atualizar" : "Instalar";

    public void Refresh()
    {
        Installed = _installer.GetInstalled(Manifest, _currentProject());
        StatusText = Installed is null
            ? (Manifest.Scope == PluginScope.Project && _currentProject() is null ? "Abra um projeto para instalar" : "Não instalado")
            : $"Instalado: {Installed.Version}";
        NotifyAll();
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(CanLaunch));
        OnPropertyChanged(nameof(InstallButtonText));
        InstallCommand.NotifyCanExecuteChanged();
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
        try
        {
            IsBusy = true;
            if (IsSystemSource)
            {
                var dlg = new OpenFileDialog { Title = $"Executável de {Manifest.Name}", Filter = "Executáveis|*.exe;*.jar;*.bat|Todos|*.*" };
                if (dlg.ShowDialog() != true) return;
                Installed = _installer.RegisterSystemTool(Manifest, project, dlg.FileName);
                _log($"{Manifest.Name}: registrado em {dlg.FileName}");
            }
            else
            {
                StatusText = "Instalando...";
                var progress = new Progress<string>(_log);
                Installed = await _installer.InstallAsync(Manifest, project, progress);
            }
        }
        catch (Exception ex)
        {
            _log($"ERRO ao instalar {Manifest.Name}: {ex.Message}");
            MessageBox.Show(ex.Message, $"Falha ao instalar {Manifest.Name}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private void Launch()
    {
        if (Installed is null) return;
        try
        {
            var rt = _launcher.Launch(Manifest, Installed, _currentProject());
            IsRunning = true;
            _log($"{Manifest.Name} aberto (pid {rt.Process.Id})");
            rt.Process.Exited += (_, _) => Application.Current.Dispatcher.Invoke(() =>
            {
                IsRunning = false;
                _log($"{Manifest.Name} fechado (código {rt.Process.ExitCode})");
            });
        }
        catch (Exception ex)
        {
            _log($"ERRO ao abrir {Manifest.Name}: {ex.Message}");
            MessageBox.Show(ex.Message, $"Falha ao abrir {Manifest.Name}", MessageBoxButton.OK, MessageBoxImage.Error);
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
