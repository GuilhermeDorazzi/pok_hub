using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

/// <summary>Um atributo com caixa de texto e controle deslizante.</summary>
public sealed partial class StatRow(string label, Func<byte> get, Action<byte> set, Action changed) : ObservableObject
{
    public string Label { get; } = label;
    public int Value
    {
        get => get();
        set { set((byte)Math.Clamp(value, 0, 255)); OnPropertyChanged(); changed(); }
    }
    public void Refresh() => OnPropertyChanged(nameof(Value));
}

/// <summary>Pontos de esforço de um atributo, de 0 a 3.</summary>
public sealed partial class EvRow(string label, int stat, StatsEditorViewModel owner) : ObservableObject
{
    public string Label { get; } = label;
    public int Value
    {
        get => owner.Current?.EvYield(stat) ?? 0;
        set { if (owner.Current is null) return; owner.Current.SetEvYield(stat, value); OnPropertyChanged(); owner.MarkDirty(); }
    }
    public void Refresh() => OnPropertyChanged(nameof(Value));
}

public sealed partial class StatsEditorViewModel : ObservableObject
{
    private readonly PersonalRepository _repo;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;

    public SpeciesCatalog Catalog { get; }
    public IReadOnlyList<string> Types { get; } = PersonalRepository.TypeNames;
    public IReadOnlyList<string> Items => _repo.Items;
    public IReadOnlyList<string> Abilities => _repo.Abilities;
    public IReadOnlyList<string> GrowthRates { get; }
    public ObservableCollection<SpeciesOption> Filtered { get; } = [];
    public IReadOnlyList<StatRow> Stats { get; }
    public IReadOnlyList<EvRow> Evs { get; }
    public PersonalData? Current { get; private set; }

    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private SpeciesOption? _selected;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = "";

    public StatsEditorViewModel(PersonalRepository repo, Action<string> log, Func<string, Task> snapshot)
    {
        _repo = repo; _log = log; _snapshot = snapshot;
        Catalog = new SpeciesCatalog(repo.Species, PokemonSprites.Load(repo.Game));
        GrowthRates = Enumerable.Range(0, 6).Select(i => Loc.T($"stats.growth.{i}")).ToList();
        Stats =
        [
            new(Loc.T("stats.hp"), () => Current?.Hp ?? 0, v => { if (Current is { } c) c.Hp = v; }, StatChanged),
            new(Loc.T("stats.attack"), () => Current?.Attack ?? 0, v => { if (Current is { } c) c.Attack = v; }, StatChanged),
            new(Loc.T("stats.defense"), () => Current?.Defense ?? 0, v => { if (Current is { } c) c.Defense = v; }, StatChanged),
            new(Loc.T("stats.spAttack"), () => Current?.SpAttack ?? 0, v => { if (Current is { } c) c.SpAttack = v; }, StatChanged),
            new(Loc.T("stats.spDefense"), () => Current?.SpDefense ?? 0, v => { if (Current is { } c) c.SpDefense = v; }, StatChanged),
            new(Loc.T("stats.speed"), () => Current?.Speed ?? 0, v => { if (Current is { } c) c.Speed = v; }, StatChanged),
        ];
        Evs =
        [
            new(Loc.T("stats.hp"), 0, this), new(Loc.T("stats.attack"), 1, this), new(Loc.T("stats.defense"), 2, this),
            new(Loc.T("stats.speed"), 3, this), new(Loc.T("stats.spAttack"), 4, this), new(Loc.T("stats.spDefense"), 5, this),
        ];
        ApplyFilter();
        Selected = Filtered.FirstOrDefault();
    }

    private void StatChanged() { MarkDirty(); OnPropertyChanged(nameof(Total)); }
    public void MarkDirty() { IsDirty = true; Status = Loc.T("common.unsaved"); }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Filtered.Clear();
        foreach (var o in Catalog.Search(Filter, hideEmpty: true).Where(o => _repo.Get(o.Id) is not null)) Filtered.Add(o);
    }

    partial void OnSelectedChanged(SpeciesOption? value)
    {
        Current = value is null ? null : _repo.Get(value.Id);
        foreach (var s in Stats) s.Refresh();
        foreach (var e in Evs) e.Refresh();
        foreach (var name in new[] { nameof(Total), nameof(Sprite), nameof(Type1), nameof(Type2), nameof(Ability1), nameof(Ability2), nameof(Item1), nameof(Item2), nameof(CatchRate), nameof(ExpYield), nameof(Friendship), nameof(EggCycles), nameof(GrowthRate), nameof(GenderRatio), nameof(GenderText) })
            OnPropertyChanged(name);
    }

    public int Total => Current?.Total ?? 0;
    public BitmapSource? Sprite => Selected is { } s ? Catalog.Sprite(s.Id) : null;

    private int Bind(Func<PersonalData, int> get) => Current is { } c ? get(c) : 0;
    private void Change(Action<PersonalData> set, string property) { if (Current is { } c) { set(c); OnPropertyChanged(property); MarkDirty(); } }

    public int Type1 { get => Bind(c => c.Type1); set => Change(c => c.Type1 = (byte)value, nameof(Type1)); }
    public int Type2 { get => Bind(c => c.Type2); set => Change(c => c.Type2 = (byte)value, nameof(Type2)); }
    public int Ability1 { get => Bind(c => c.Ability1); set => Change(c => c.Ability1 = (byte)value, nameof(Ability1)); }
    public int Ability2 { get => Bind(c => c.Ability2); set => Change(c => c.Ability2 = (byte)value, nameof(Ability2)); }
    public int Item1 { get => Bind(c => c.Item1); set => Change(c => c.Item1 = (ushort)value, nameof(Item1)); }
    public int Item2 { get => Bind(c => c.Item2); set => Change(c => c.Item2 = (ushort)value, nameof(Item2)); }
    public int CatchRate { get => Bind(c => c.CatchRate); set => Change(c => c.CatchRate = (byte)Math.Clamp(value, 0, 255), nameof(CatchRate)); }
    public int ExpYield { get => Bind(c => c.ExpYield); set => Change(c => c.ExpYield = (byte)Math.Clamp(value, 0, 255), nameof(ExpYield)); }
    public int Friendship { get => Bind(c => c.Friendship); set => Change(c => c.Friendship = (byte)Math.Clamp(value, 0, 255), nameof(Friendship)); }
    public int EggCycles { get => Bind(c => c.EggCycles); set => Change(c => c.EggCycles = (byte)Math.Clamp(value, 0, 255), nameof(EggCycles)); }
    public int GrowthRate { get => Bind(c => c.GrowthRate); set => Change(c => c.GrowthRate = (byte)value, nameof(GrowthRate)); }
    public int GenderRatio
    {
        get => Bind(c => c.GenderRatio);
        set { Change(c => c.GenderRatio = (byte)Math.Clamp(value, 0, 255), nameof(GenderRatio)); OnPropertyChanged(nameof(GenderText)); }
    }

    /// <summary>0 só machos, 254 só fêmeas, 255 sem gênero; no meio, a proporção de fêmeas.</summary>
    public string GenderText => GenderRatio switch
    {
        0 => Loc.T("stats.gender.male"),
        254 => Loc.T("stats.gender.female"),
        255 => Loc.T("stats.gender.none"),
        var r => Loc.T("stats.gender.ratio", Math.Round(r * 100.0 / 254, 1)),
    };

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _repo.Save();
            IsDirty = false;
            Status = Loc.T("common.savedAt", DateTime.Now.ToString("HH:mm:ss"));
            _log(Loc.T("stats.saved"));
            await _snapshot(Loc.T("stats.snapshot"));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = Loc.T("common.saveError", ex.Message);
        }
    }
}
