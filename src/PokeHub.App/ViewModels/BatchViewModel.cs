using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Batch;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

public sealed record NamedIndex(int Index, string Name);

/// <summary>Operações em lote: regras para treinadores e encontros, prévia e aplicação com versão salva antes e depois.</summary>
public sealed partial class BatchViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Action<string> _log;
    private readonly Func<Project, string, Task> _snapshot;
    private TrainerRepository _trainers;
    private EncounterRepository _encounters;

    public BatchViewModel(Project project, Action<string> log, Func<Project, string, Task> snapshot)
    {
        _project = project;
        _log = log;
        _snapshot = snapshot;
        _trainers = TrainerRepository.Load(project);
        _encounters = EncounterRepository.Load(project);
        Classes = [new(-1, Loc.T("batch.allClasses")), .. _trainers.Classes.Select((c, i) => new NamedIndex(i, c)).Where(c => c.Name.Length > 0)];
        Catalog = new SpeciesCatalog(_trainers.Species, PokemonSprites.Load(_trainers.Game));
        Items = [new(0, Loc.T("batch.noItem")), .. _trainers.Items.Select((s, i) => new NamedIndex(i, s)).Where(s => s.Index > 0 && s.Name.Length > 0 && s.Name != "-----").OrderBy(s => s.Name)];
        SelectedClass = Classes[0];
    }

    public IReadOnlyList<NamedIndex> Classes { get; }
    public SpeciesCatalog Catalog { get; }
    public IReadOnlyList<NamedIndex> Items { get; }

    // treinadores
    [ObservableProperty] private bool _trainersEnabled = true;
    [ObservableProperty] private NamedIndex? _selectedClass;
    [ObservableProperty] private string _nameFilter = "";
    [ObservableProperty] private double _trainerLevelPercent = 100;
    [ObservableProperty] private int _trainerLevelOffset;
    [ObservableProperty] private int _trainerMinLevel = 1;
    [ObservableProperty] private int _trainerMaxLevel = 100;
    [ObservableProperty] private bool _setHeldItem;
    [ObservableProperty] private NamedIndex? _heldItem;
    [ObservableProperty] private bool _trainerReplace;
    [ObservableProperty] private int _trainerFrom = -1;
    [ObservableProperty] private int _trainerTo = -1;

    // encontros
    [ObservableProperty] private bool _encountersEnabled;
    [ObservableProperty] private bool _encounterReplace = true;
    [ObservableProperty] private int _encounterFrom = -1;
    [ObservableProperty] private int _encounterTo = -1;
    [ObservableProperty] private double _encounterLevelPercent = 100;
    [ObservableProperty] private int _encounterLevelOffset;
    [ObservableProperty] private int _encounterMinLevel = 1;
    [ObservableProperty] private int _encounterMaxLevel = 100;

    public ObservableCollection<string> Preview { get; } = [];
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    public bool Applied { get; private set; }

    private TrainerBatchOptions TrainerOptions() => new()
    {
        ClassFilter = SelectedClass is { Index: >= 0 } c ? c.Index : null,
        NameFilter = NameFilter.Trim(),
        LevelPercent = TrainerLevelPercent, LevelOffset = TrainerLevelOffset, MinLevel = TrainerMinLevel, MaxLevel = TrainerMaxLevel,
        HeldItem = SetHeldItem && HeldItem is { } i ? (ushort)i.Index : null,
        ReplaceSpeciesFrom = TrainerReplace && TrainerFrom >= 0 ? (ushort)TrainerFrom : null,
        ReplaceSpeciesTo = TrainerReplace && TrainerTo >= 0 ? (ushort)TrainerTo : null,
    };

    private EncounterBatchOptions EncounterOptions() => new()
    {
        ReplaceSpeciesFrom = EncounterReplace && EncounterFrom >= 0 ? (ushort)EncounterFrom : null,
        ReplaceSpeciesTo = EncounterReplace && EncounterTo >= 0 ? (ushort)EncounterTo : null,
        LevelPercent = EncounterLevelPercent, LevelOffset = EncounterLevelOffset, MinLevel = EncounterMinLevel, MaxLevel = EncounterMaxLevel,
    };

    [RelayCommand]
    private void BuildPreview()
    {
        Preview.Clear();
        var lines = new List<string>();
        if (TrainersEnabled) lines.AddRange(BatchEditor.PreviewTrainers(_trainers, TrainerOptions()));
        if (EncountersEnabled) lines.AddRange(BatchEditor.PreviewEncounters(_encounters, EncounterOptions()));
        foreach (var l in lines.Take(2000)) Preview.Add(l);
        Status = lines.Count == 0 ? Loc.T("batch.noChanges") : lines.Count > 2000 ? Loc.T("batch.previewMore", lines.Count) : Loc.T("batch.previewCount", lines.Count);
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply => Preview.Count > 0 && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        try
        {
            IsBusy = true;
            await _snapshot(_project, Loc.T("batch.beforeSnapshot"));
            int n = 0;
            if (TrainersEnabled) { n += BatchEditor.ApplyTrainers(_trainers, TrainerOptions()).Count; _trainers.Save(); }
            if (EncountersEnabled) { n += BatchEditor.ApplyEncounters(_encounters, EncounterOptions()).Count; _encounters.Save(); }
            _log(Loc.T("batch.applied", n));
            await _snapshot(_project, Loc.T("batch.afterSnapshot", n));
            Applied = true;
            // recarrega para a próxima prévia partir do estado novo
            _trainers = TrainerRepository.Load(_project);
            _encounters = EncounterRepository.Load(_project);
            Preview.Clear();
            Status = Loc.T("batch.applied", n);
        }
        catch (Exception ex)
        {
            _log(Loc.T("vm.error", ex.Message));
            Status = ex.Message;
        }
        finally { IsBusy = false; ApplyCommand.NotifyCanExecuteChanged(); }
    }
}
