using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PokeHub.Core.Decomp;
using PokeHub.Core.Hub;
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

    public PluginManifest Manifest { get; }

    [ObservableProperty] private InstalledTool? _installed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _statusText = "";

    public ToolItemViewModel(PluginManifest manifest, PluginInstaller installer, ToolLauncher launcher, DecompBuildService builds,
        DependencyChecker deps, Func<Project?> currentProject, Action<string> log, Func<Project, string, Task> snapshot)
    {
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
    public string Description => Manifest.Description;
    public string LicenseText => Manifest.License is { } l ? $"Licença: {l}" : "";
    public string RequiresText => Manifest.Requires.Count > 0
        ? "Requer: " + string.Join(", ", _deps.CheckAll(Manifest.Requires).Select(d =>
            d.Satisfied is null && d.Detail == "" ? d.Name
            : d.IsMissing ? $"{d.Name} (FALTANDO: {d.Detail})"
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
        var text = $"{Manifest.Name} depende de algo que o hub não instala sozinho:\n\n{lines}\n\n" +
                   (withUrl is null ? $"Continuar com '{action}' mesmo assim?"
                                    : $"Sim = abrir a página de download de {withUrl.Name}\nNão = continuar com '{action}' mesmo assim\nCancelar = parar");
        var r = MessageBox.Show(text, "Dependência faltando", withUrl is null ? MessageBoxButton.YesNo : MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes && withUrl is not null)
        {
            Process.Start(new ProcessStartInfo(withUrl.InstallUrl!) { UseShellExecute = true });
            _log($"Página de download aberta: {withUrl.InstallUrl}. Instale e tente de novo.");
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
            var r = MessageBox.Show(notice + "\n\nIniciar o primeiro build agora?", $"Primeiro build: {Manifest.Name}",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r != MessageBoxResult.Yes) { _log($"{Manifest.Name}: build cancelado."); return; }
        }
        try
        {
            IsBusy = true;
            StatusText = firstBuild ? "Compilando (primeiro build, demora)..." : "Compilando...";
            var rom = await _builds.BuildAsync(Manifest, Installed, project, new Progress<string>(_log));
            _log($"{Manifest.Name}: ROM copiada para {rom}. Use 'Abrir' no melonDS ou 'Empacotar e rodar' para testar.");
        }
        catch (Exception ex)
        {
            _log($"ERRO no build de {Manifest.Name}: {ex.Message}");
            MessageBox.Show(ex.Message, $"Falha no build de {Manifest.Name}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }
    public string? Notes => Manifest.Notes;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Manifest.Notes);
    public string? Purpose => Manifest.Purpose;
    public string? Cost => Manifest.Cost;
    public bool HasPurpose => !string.IsNullOrWhiteSpace(Manifest.Purpose);
    public bool HasCost => !string.IsNullOrWhiteSpace(Manifest.Cost);

    /// <summary>Instalações pesadas pedem confirmação mostrando o propósito e o custo.</summary>
    private bool ConfirmHeavyInstall()
    {
        if (!Manifest.ConfirmInstall || IsInstalled) return true;
        var text = $"{Manifest.Name}\n\n{Manifest.Purpose}\n\nCusto: {Manifest.Cost}\n\nInstalar agora?";
        return MessageBox.Show(text, "Antes de instalar", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes;
    }
    public bool IsInstalled => Installed is not null;
    public bool IsBuiltin => Manifest.Source.Type == SourceType.Builtin;
    public bool IsRuntime => Manifest.Source.Type == SourceType.Runtime;
    public bool CanInstallOrUpdate => !IsBuiltin;
    public string InstallButtonText => IsSystemSource ? "Apontar executável"
        : IsRuntime && IsInstalled ? "Preparar toolchain de novo"
        : IsInstalled ? "Atualizar" : "Instalar";
    public bool CanLaunch => IsInstalled && Manifest.Launch is not null && !IsBusy && (!IsBuiltin || _currentProject() is not null);
    public bool IsSystemSource => Manifest.Source.Type == SourceType.System;

    public void Refresh()
    {
        Installed = _installer.GetInstalled(Manifest, _currentProject());
        StatusText = IsBuiltin ? (_currentProject() is null ? "Abra um projeto" : "Embutido no hub")
            : Installed is null
            ? (Manifest.Scope == PluginScope.Project && _currentProject() is null ? "Abra um projeto para instalar" : "Não instalado")
            : $"Instalado: {Installed.Version}";
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
        if (!ConfirmDependencies("instalar")) return;
        if (!ConfirmHeavyInstall()) { _log($"{Manifest.Name}: instalação cancelada."); return; }
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
                if (Manifest.Source.Type == SourceType.Runtime && Manifest.Setup?.Pacman is { Count: > 0 } packages)
                {
                    StatusText = "Preparando toolchain...";
                    var shells = await _builds.GetShellsAsync(refresh: true);
                    var msysRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Installed.ExecutablePath)!, "..", ".."));
                    var shell = shells.FirstOrDefault(s => s.Kind == UnixShellKind.Msys2 && string.Equals(Path.GetFullPath(s.Location), msysRoot, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("MSYS2 extraído mas não detectado.");
                    await _builds.PrepareMsys2ToolchainAsync(shell, packages, progress, libpngVersion: Manifest.Setup.LibpngVersion);
                    await _builds.GetShellsAsync(refresh: true);
                }
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
        if (!ConfirmDependencies("abrir")) return;
        if (IsBuiltin)
        {
            var project = _currentProject();
            if (project is null) return;
            try { BuiltinEditors.Open(Manifest.Id, project, _log, msg => _snapshot(project, msg)); }
            catch (Exception ex)
            {
                _log($"ERRO ao abrir {Manifest.Name}: {ex.Message}");
                MessageBox.Show(ex.Message, $"Falha ao abrir {Manifest.Name}", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }
        try
        {
            var rt = _launcher.Launch(Manifest, Installed, _currentProject(), new Progress<string>(_log));
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
