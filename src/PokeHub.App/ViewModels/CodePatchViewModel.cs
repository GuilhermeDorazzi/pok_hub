using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.CodeInjection;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

/// <summary>Uma linha da lista de patches. Marcar ou desmarcar grava no patch.json.</summary>
public sealed partial class CodePatchItem(CodePatch patch, string applied) : ObservableObject
{
    public CodePatch Patch { get; } = patch;
    public string Name => patch.Name;
    public string Description => patch.Manifest.Description;
    public int Hooks => patch.Manifest.Hooks.Count;
    public string Applied => applied;

    [ObservableProperty] private bool _enabled = patch.Manifest.Enabled;

    partial void OnEnabledChanged(bool value)
    {
        patch.Manifest.Enabled = value;
        patch.Save();
    }
}

/// <summary>Injeção de código: lista os patches do projeto, cria novos, compila e aplica, com versão salva antes e depois.</summary>
public sealed partial class CodePatchViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly CodePatchService _service;
    private readonly Action<string> _log;
    private readonly Func<Project, string, Task> _snapshot;

    public CodePatchViewModel(Project project, HubPaths hub, Action<string> log, Func<Project, string, Task> snapshot)
    {
        _project = project;
        _service = new CodePatchService(hub);
        _log = log;
        _snapshot = snapshot;
        Reload();
    }

    public ObservableCollection<CodePatchItem> Patches { get; } = [];

    [ObservableProperty] private string _newName = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(EditHooksCommand))] private CodePatchItem? _selectedPatch;
    [ObservableProperty] private string _status = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RevertCommand), nameof(CreateCommand))] private bool _isBusy;
    /// <summary>Saída do compilador e dos ganchos, para ler o erro sem sair da janela.</summary>
    [ObservableProperty] private string _output = "";

    private bool CanRun => !IsBusy;

    [RelayCommand]
    private void Reload()
    {
        Patches.Clear();
        try
        {
            var regions = CodePatchService.LoadLedger(_project).Regions;
            foreach (var patch in CodePatchService.List(_project))
            {
                var region = regions.FirstOrDefault(r => r.Patch == patch.Name);
                Patches.Add(new CodePatchItem(patch, region is null ? Loc.T("inject.appliedNo") : Loc.T("inject.appliedYes", region.Size, region.Address)));
            }
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Create()
    {
        try
        {
            var patch = CodePatchService.Create(_project, NewName.Trim());
            NewName = "";
            Reload();
            Process.Start(new ProcessStartInfo("explorer.exe", patch.Directory) { UseShellExecute = true });
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private bool HasSelection => SelectedPatch is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void EditHooks()
    {
        new Views.PatchHooksWindow(new PatchHooksViewModel(_project, SelectedPatch!.Patch)) { Owner = System.Windows.Application.Current.Windows.OfType<Views.CodePatchWindow>().FirstOrDefault() }.ShowDialog();
        Reload();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dir = CodePatchService.PatchesDir(_project);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ApplyAsync() => RunAsync(_service.ApplyAsync, "inject.snapshotBefore", "inject.snapshotAfter",
        r => Loc.T("inject.done", r.Patches.Count(p => p.Size > 0 || p.Hooks > 0), r.FreeBytes)); // patch ativo ainda sem código nem ganchos não muda o jogo

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RevertAsync()
    {
        if (MessageBox.Show(Loc.T("inject.confirmRevert"), Loc.T("inject.title"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return Task.CompletedTask;
        return RunAsync(_service.RevertAsync, "inject.snapshotBeforeRevert", "inject.snapshotAfterRevert", _ => Loc.T("inject.reverted"));
    }

    private async Task RunAsync(Func<Project, IProgress<string>, CancellationToken, Task<PatchApplyResult>> action, string before, string after, Func<PatchApplyResult, string> done)
    {
        IsBusy = true;
        Output = "";
        Status = Loc.T("inject.busy");
        var progress = new Progress<string>(line => { Output += line + Environment.NewLine; _log(line); });
        try
        {
            await _snapshot(_project, Loc.T(before));
            var result = await Task.Run(() => action(_project, progress, CancellationToken.None));
            await _snapshot(_project, Loc.T(after));
            Status = done(result);
            _log(Status);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            _log(Loc.T("vm.error", ex.Message));
        }
        finally
        {
            IsBusy = false;
            Reload();
        }
    }
}
