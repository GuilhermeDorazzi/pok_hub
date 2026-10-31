using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

/// <summary>Uma linha de encontro: até três espécies (por período) e uma faixa de nível, ligadas ao modelo por acessores.</summary>
public sealed partial class EncounterRowViewModel : ObservableObject
{
    private readonly Func<ushort> _get1; private readonly Action<ushort> _set1;
    private readonly Func<ushort>? _get2; private readonly Action<ushort>? _set2;
    private readonly Func<ushort>? _get3; private readonly Action<ushort>? _set3;
    private readonly Func<int> _getMin; private readonly Action<int> _setMin;
    private readonly Func<int>? _getMax; private readonly Action<int>? _setMax;
    private readonly Action _changed;

    public string Label { get; }

    public EncounterRowViewModel(string label, Action changed,
        Func<ushort> get1, Action<ushort> set1, Func<int> getMin, Action<int> setMin,
        Func<int>? getMax = null, Action<int>? setMax = null,
        Func<ushort>? get2 = null, Action<ushort>? set2 = null, Func<ushort>? get3 = null, Action<ushort>? set3 = null)
    {
        Label = label; _changed = changed;
        _get1 = get1; _set1 = set1; _getMin = getMin; _setMin = setMin; _getMax = getMax; _setMax = setMax;
        _get2 = get2; _set2 = set2; _get3 = get3; _set3 = set3;
    }

    public int Species { get => _get1(); set { _set1((ushort)Math.Max(0, value)); OnPropertyChanged(); _changed(); } }
    public int Species2 { get => _get2?.Invoke() ?? 0; set { _set2?.Invoke((ushort)Math.Max(0, value)); OnPropertyChanged(); _changed(); } }
    public int Species3 { get => _get3?.Invoke() ?? 0; set { _set3?.Invoke((ushort)Math.Max(0, value)); OnPropertyChanged(); _changed(); } }
    public int MinLevel { get => _getMin(); set { _setMin(Math.Clamp(value, 0, 100)); OnPropertyChanged(); _changed(); } }
    public int MaxLevel { get => _getMax?.Invoke() ?? MinLevel; set { _setMax?.Invoke(Math.Clamp(value, 0, 100)); OnPropertyChanged(); _changed(); } }
    public bool HasMax => _getMax is not null;
    public bool HasTimes => _get2 is not null;
}

public sealed partial class EncounterGroupViewModel : ObservableObject
{
    private readonly Func<int>? _getRate; private readonly Action<int>? _setRate;
    private readonly Action _changed;

    public string Title { get; }
    public ObservableCollection<EncounterRowViewModel> Rows { get; } = [];
    public bool HasRate => _getRate is not null;
    public bool HasTimes => Rows.Count > 0 && Rows[0].HasTimes;
    public bool HasMax => Rows.Count > 0 && Rows[0].HasMax;

    public EncounterGroupViewModel(string title, Action changed, Func<int>? getRate = null, Action<int>? setRate = null)
    {
        Title = title; _changed = changed; _getRate = getRate; _setRate = setRate;
    }

    public int Rate { get => _getRate?.Invoke() ?? 0; set { _setRate?.Invoke(Math.Clamp(value, 0, 255)); OnPropertyChanged(); _changed(); } }
}

public sealed partial class EncounterEditorViewModel : ObservableObject
{
    private readonly EncounterRepository _repo;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;

    public IReadOnlyList<string> Species => _repo.Species;
    public SpeciesCatalog Catalog => _catalog ??= new SpeciesCatalog(_repo.Species, PokemonSprites.Load(_repo.Game));
    private SpeciesCatalog? _catalog;
    public ObservableCollection<EncounterEntry> AllEntries { get; } = [];
    public ObservableCollection<EncounterEntry> FilteredEntries { get; } = [];
    public ObservableCollection<EncounterGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private EncounterEntry? _selected;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = "";

    public string GameLabel => Loc.T("enc.gameLabel", _repo.Game.Game, _repo.Entries.Count, _repo.Headers.Count);
    public string LabelOf(EncounterEntry e) => _repo.Label(e);

    public EncounterEditorViewModel(EncounterRepository repo, Action<string> log, Func<string, Task> snapshot)
    {
        _repo = repo; _log = log; _snapshot = snapshot;
        foreach (var e in repo.Entries) AllEntries.Add(e);
        ApplyFilter();
        Selected = FilteredEntries.FirstOrDefault();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        var f = Filter.Trim();
        foreach (var e in AllEntries)
            if (f.Length == 0 || _repo.Label(e).Contains(f, StringComparison.OrdinalIgnoreCase)) FilteredEntries.Add(e);
    }

    partial void OnSelectedChanged(EncounterEntry? value)
    {
        Groups.Clear();
        if (value is null) return;
        if (value.Hgss is { } h) BuildHgss(h); else if (value.Dppt is { } d) BuildDppt(d);
        OnPropertyChanged(nameof(SelectedLabel));
    }

    public string SelectedLabel => Selected is null ? "" : _repo.Label(Selected);

    private void BuildHgss(HgssEncounters h)
    {
        var grass = new EncounterGroupViewModel(Loc.T("enc.grass"), MarkDirty, () => h.WalkingRate, v => h.WalkingRate = (byte)v);
        for (int i = 0; i < 12; i++)
        {
            int k = i;
            grass.Rows.Add(new EncounterRowViewModel($"{EncounterOdds.Walking[k]}%", MarkDirty,
                () => h.Morning[k], v => h.Morning[k] = v, () => h.WalkingLevels[k], v => h.WalkingLevels[k] = (byte)v,
                null, null, () => h.Day[k], v => h.Day[k] = v, () => h.Night[k], v => h.Night[k] = v));
        }
        Groups.Add(grass);
        Groups.Add(Pair(Loc.T("enc.hoennSound"), h.HoennSound));
        Groups.Add(Pair(Loc.T("enc.sinnohSound"), h.SinnohSound));
        Groups.Add(Slots(Loc.T("enc.surf"), h.Surf, () => h.SurfRate, v => h.SurfRate = (byte)v, EncounterOdds.Surf));
        Groups.Add(Slots(Loc.T("enc.rockSmash"), h.RockSmash, () => h.RockSmashRate, v => h.RockSmashRate = (byte)v));
        Groups.Add(Slots(Loc.T("enc.oldRod"), h.OldRod, () => h.OldRodRate, v => h.OldRodRate = (byte)v));
        Groups.Add(Slots(Loc.T("enc.goodRod"), h.GoodRod, () => h.GoodRodRate, v => h.GoodRodRate = (byte)v));
        Groups.Add(Slots(Loc.T("enc.superRod"), h.SuperRod, () => h.SuperRodRate, v => h.SuperRodRate = (byte)v));
        var swarm = new EncounterGroupViewModel(Loc.T("enc.swarm"), MarkDirty);
        string[] swarmLabels = [Loc.T("enc.grass"), Loc.T("enc.surf"), Loc.T("enc.goodRod"), Loc.T("enc.superRod")];
        for (int i = 0; i < 4; i++) { int k = i; swarm.Rows.Add(SpeciesOnly(swarmLabels[k], () => h.Swarm[k], v => h.Swarm[k] = v)); }
        Groups.Add(swarm);
    }

    private void BuildDppt(DpptEncounters d)
    {
        var grass = new EncounterGroupViewModel(Loc.T("enc.grass"), MarkDirty, () => (int)d.WalkingRate, v => d.WalkingRate = (uint)v);
        for (int i = 0; i < 12; i++)
        {
            var s = d.Walking[i];
            grass.Rows.Add(new EncounterRowViewModel($"{EncounterOdds.Walking[i]}%", MarkDirty,
                () => s.Species, v => s.Species = v, () => s.MinLevel, v => s.MinLevel = s.MaxLevel = (byte)v));
        }
        Groups.Add(grass);
        Groups.Add(Pair(Loc.T("enc.day"), d.Day));
        Groups.Add(Pair(Loc.T("enc.night"), d.Night));
        Groups.Add(Pair(Loc.T("enc.swarm"), d.Swarm));
        Groups.Add(Pair(Loc.T("enc.radar"), d.Radar));
        foreach (var (title, arr) in new[] { ("Ruby", d.Ruby), ("Sapphire", d.Sapphire), ("Emerald", d.Emerald), ("FireRed", d.FireRed), ("LeafGreen", d.LeafGreen) })
            Groups.Add(Pair(Loc.T("enc.dualSlot", title), arr));
        Groups.Add(Slots(Loc.T("enc.surf"), d.Surf, () => (int)d.SurfRate, v => d.SurfRate = (uint)v, EncounterOdds.Surf));
        Groups.Add(Slots(Loc.T("enc.oldRod"), d.OldRod, () => (int)d.OldRodRate, v => d.OldRodRate = (uint)v));
        Groups.Add(Slots(Loc.T("enc.goodRod"), d.GoodRod, () => (int)d.GoodRodRate, v => d.GoodRodRate = (uint)v));
        Groups.Add(Slots(Loc.T("enc.superRod"), d.SuperRod, () => (int)d.SuperRodRate, v => d.SuperRodRate = (uint)v));
    }

    private EncounterGroupViewModel Pair(string title, ushort[] arr)
    {
        var g = new EncounterGroupViewModel(title, MarkDirty);
        for (int i = 0; i < arr.Length; i++) { int k = i; g.Rows.Add(SpeciesOnly($"{k + 1}", () => arr[k], v => arr[k] = v)); }
        return g;
    }

    private EncounterRowViewModel SpeciesOnly(string label, Func<ushort> get, Action<ushort> set) =>
        new(label, MarkDirty, get, set, () => 0, _ => { });

    private EncounterGroupViewModel Slots(string title, EncounterSlot[] slots, Func<int> getRate, Action<int> setRate, int[]? odds = null)
    {
        var g = new EncounterGroupViewModel(title, MarkDirty, getRate, setRate);
        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            g.Rows.Add(new EncounterRowViewModel(odds is not null && i < odds.Length ? $"{odds[i]}%" : $"{i + 1}", MarkDirty,
                () => s.Species, v => s.Species = v, () => s.MinLevel, v => s.MinLevel = (byte)v, () => s.MaxLevel, v => s.MaxLevel = (byte)v));
        }
        return g;
    }

    private void MarkDirty()
    {
        IsDirty = _repo.HasChanges;
        Status = IsDirty ? Loc.T("common.unsaved") : "";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _repo.Save();
            IsDirty = false;
            Status = Loc.T("common.savedAt", DateTime.Now.ToString("HH:mm:ss"));
            _log(Loc.T("enc.savedLog"));
            await _snapshot(Loc.T("enc.snapshot"));
        }
        catch (Exception ex)
        {
            Status = Loc.T("common.saveError", ex.Message);
            _log(Loc.T("enc.saveErrorLog", ex.Message));
        }
    }
}
