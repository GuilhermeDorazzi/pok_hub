using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Batch;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

public sealed record NamedIndex(int Index, string Name);

public sealed partial class BatchAiFlag(int bit, string name) : ObservableObject
{
    public int Bit { get; } = bit;
    public string Name { get; } = name;
    [ObservableProperty] private bool _isOn;
}

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
        BattleItem1 = BattleItem2 = BattleItem3 = BattleItem4 = Items[0];
        CanEvolve = GameTables.Supports(_trainers.Game);
        Regions = _trainers.Game.IsHgss
            ? [new((int)EncounterRegion.All, Loc.T("batch.region.all")), new((int)EncounterRegion.First, Loc.T("batch.region.johto")), new((int)EncounterRegion.Second, Loc.T("batch.region.kanto"))]
            : [new((int)EncounterRegion.All, Loc.T("batch.region.all"))];
        SelectedRegion = Regions[0];
    }

    public IReadOnlyList<NamedIndex> Classes { get; }
    public SpeciesCatalog Catalog { get; }
    public IReadOnlyList<NamedIndex> Items { get; }
    public IReadOnlyList<NamedIndex> Regions { get; }
    /// <summary>Diamond e Pearl não têm a tabela de evoluções no hub.</summary>
    public bool CanEvolve { get; }

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
    [ObservableProperty] private bool _setAi;
    [ObservableProperty] private bool _setBattleItems;
    [ObservableProperty] private NamedIndex? _battleItem1;
    [ObservableProperty] private NamedIndex? _battleItem2;
    [ObservableProperty] private NamedIndex? _battleItem3;
    [ObservableProperty] private NamedIndex? _battleItem4;
    [ObservableProperty] private bool _trainerEvolve;
    [ObservableProperty] private int _trainerEvolveOtherAt = 40;
    /// <summary>Uma caixa por opção de IA. Marcada liga a opção, desmarcada desliga; as opções só valem com "Definir a IA" ligado.</summary>
    public IReadOnlyList<BatchAiFlag> AiFlags { get; } = Enumerable.Range(0, Trainer.AiFlagCount).Select(i => new BatchAiFlag(i, Loc.T($"tr.ai.{i}"))).ToList();

    // encontros
    [ObservableProperty] private bool _encountersEnabled;
    [ObservableProperty] private bool _encounterReplace = true;
    [ObservableProperty] private int _encounterFrom = -1;
    [ObservableProperty] private int _encounterTo = -1;
    [ObservableProperty] private double _encounterLevelPercent = 100;
    [ObservableProperty] private int _encounterLevelOffset;
    [ObservableProperty] private int _encounterMinLevel = 1;
    [ObservableProperty] private int _encounterMaxLevel = 100;
    [ObservableProperty] private NamedIndex? _selectedRegion;
    [ObservableProperty] private string _placeFilter = "";
    [ObservableProperty] private bool _encounterEvolve;
    [ObservableProperty] private int _encounterEvolveOtherAt = 40;

    private EvolutionChart? _chart;
    /// <summary>Lida uma vez e só se alguém pedir; a tabela não muda nesta janela.</summary>
    private EvolutionChart Chart => _chart ??= EvolutionChart.Load(_project);

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
        AiFlagsOn = SetAi ? AiFlags.Where(f => f.IsOn).Aggregate(0u, (bits, f) => bits | 1u << f.Bit) : 0,
        AiFlagsOff = SetAi ? AiFlags.Where(f => !f.IsOn).Aggregate(0u, (bits, f) => bits | 1u << f.Bit) : 0,
        BattleItems = SetBattleItems ? [(ushort)(BattleItem1?.Index ?? 0), (ushort)(BattleItem2?.Index ?? 0), (ushort)(BattleItem3?.Index ?? 0), (ushort)(BattleItem4?.Index ?? 0)] : null,
        Evolutions = TrainerEvolve ? Chart : null,
        EvolveOtherAt = TrainerEvolveOtherAt,
    };

    /// <summary>Receita para treinadores difíceis: as regras gerais de escolha de golpe, leitura de HP e o que combina com o time. Fica marcada para revisar antes da prévia.</summary>
    [RelayCommand]
    private void CompetitiveRecipe()
    {
        SetAi = true;
        int[] recipe = [0, 1, 2, 3, 8, 9, 10];
        foreach (var f in AiFlags) f.IsOn = recipe.Contains(f.Bit);
    }

    private EncounterBatchOptions EncounterOptions() => new()
    {
        ReplaceSpeciesFrom = EncounterReplace && EncounterFrom >= 0 ? (ushort)EncounterFrom : null,
        ReplaceSpeciesTo = EncounterReplace && EncounterTo >= 0 ? (ushort)EncounterTo : null,
        LevelPercent = EncounterLevelPercent, LevelOffset = EncounterLevelOffset, MinLevel = EncounterMinLevel, MaxLevel = EncounterMaxLevel,
        Region = (EncounterRegion)(SelectedRegion?.Index ?? 0),
        PlaceFilter = PlaceFilter,
        Evolutions = EncounterEvolve ? Chart : null,
        EvolveOtherAt = EncounterEvolveOtherAt,
    };

    [RelayCommand]
    private void BuildPreview()
    {
        var lines = new List<string>();
        if (TrainersEnabled) lines.AddRange(BatchEditor.PreviewTrainers(_trainers, TrainerOptions()));
        if (EncountersEnabled) lines.AddRange(BatchEditor.PreviewEncounters(_encounters, EncounterOptions()));
        _allPreview = lines;
        ApplyPreviewFilter();
        Status = lines.Count == 0 ? Loc.T("batch.noChanges") : lines.Count > 2000 ? Loc.T("batch.previewMore", lines.Count) : Loc.T("batch.previewCount", lines.Count);
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private List<string> _allPreview = [];
    [ObservableProperty] private string _previewFilter = "";
    [ObservableProperty] private string _previewCount = "";

    partial void OnPreviewFilterChanged(string value) => ApplyPreviewFilter();

    /// <summary>Mostra no máximo 2000 linhas por vez; o filtro procura em todas, então nada fica escondido por causa do limite.</summary>
    private void ApplyPreviewFilter()
    {
        Preview.Clear();
        var query = PreviewFilter.Trim();
        var matching = query.Length == 0 ? _allPreview : _allPreview.Where(l => l.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var l in matching.Take(2000)) Preview.Add(l);
        PreviewCount = _allPreview.Count == 0 ? "" : Loc.T("report.shown", Preview.Count, _allPreview.Count);
    }

    private bool CanApply => _allPreview.Count > 0 && !IsBusy;

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
