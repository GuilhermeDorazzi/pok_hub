using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

/// <summary>O que acontece ao vencer a Elite: liberar os treinadores e subir o nível deles.</summary>
public sealed partial class PostEliteViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly PostEliteService _service;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;

    public bool CanScaleLevels { get; }
    public string LevelsHint { get; }

    [ObservableProperty] private bool _resetTrainers;
    [ObservableProperty] private string _resetSkip;
    [ObservableProperty] private bool _openIlexShrine;
    [ObservableProperty] private bool _scaleLevels;
    [ObservableProperty] private int _levelPercent;
    [ObservableProperty] private int _levelFloor;
    [ObservableProperty] private string _levelSkip;
    [ObservableProperty] private bool _kantoBadges;
    [ObservableProperty] private string _kantoTargets;
    [ObservableProperty] private int _kantoRouteGap;
    [ObservableProperty] private string _status = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    public PostEliteViewModel(Project project, HubPaths hub, Action<string> log, Func<string, Task> snapshot)
    {
        _project = project;
        _service = new PostEliteService(hub);
        _log = log;
        _snapshot = snapshot;
        var s = PostEliteSettings.Load(project);
        _resetTrainers = s.ResetTrainers;
        _resetSkip = string.Join(", ", s.ResetSkip);
        _openIlexShrine = s.OpenIlexShrine;
        _scaleLevels = s.ScaleLevels;
        _levelPercent = s.LevelPercent;
        _levelFloor = s.LevelFloor;
        _levelSkip = string.Join(", ", s.LevelSkip);
        _kantoBadges = s.KantoBadges;
        _kantoTargets = string.Join(", ", s.KantoTargets);
        _kantoRouteGap = s.KantoRouteGap;
        CanScaleLevels = PostEliteService.CanScaleLevels(project);
        LevelsHint = Loc.T(CanScaleLevels ? "postElite.levels.hint" : "core.postElite.levelsNeed");
    }

    private bool CanApply => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        try
        {
            IsBusy = true;
            var resetSkip = PostEliteService.ParseIds(ResetSkip);
            var levelSkip = PostEliteService.ParseIds(LevelSkip);
            await _snapshot(Loc.T("postElite.before"));
            int count = PostEliteService.ApplyReset(_project, ResetTrainers, resetSkip);
            PostEliteService.ApplyIlex(_project, OpenIlexShrine);
            var targets = PostEliteService.ParseTargets(KantoTargets);
            if (CanScaleLevels || ScaleLevels || KantoBadges)
                await _service.ApplyLevelsAsync(_project, ScaleLevels, LevelPercent, LevelFloor, levelSkip, new Progress<string>(_log), default, KantoBadges, targets, KantoRouteGap);
            await _snapshot(Loc.T("postElite.after"));
            Status = Loc.T("postElite.done", count);
            _log(Status);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            _log(Loc.T("vm.error", ex.Message));
        }
        finally { IsBusy = false; }
    }
}
