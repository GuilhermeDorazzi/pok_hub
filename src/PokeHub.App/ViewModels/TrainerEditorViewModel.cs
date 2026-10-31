using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

/// <summary>Linha do time no editor: espelha um PartyMember com notificação.</summary>
public sealed partial class PartyRowViewModel(PartyMember member, TrainerEditorViewModel owner) : ObservableObject
{
    public PartyMember Member { get; } = member;
    public TrainerEditorViewModel Owner { get; } = owner;

    public int Species { get => Member.Species; set { Member.Species = (ushort)value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(Sprite)); Owner.MarkDirty(); } }
    public BitmapSource? Sprite => Owner.Catalog.Sprite(Member.Species);
    public int Form { get => Member.Form; set { Member.Form = (ushort)Math.Clamp(value, 0, 63); OnPropertyChanged(); Owner.MarkDirty(); } }
    public int Level { get => Member.Level; set { Member.Level = (ushort)Math.Clamp(value, 1, 100); OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); Owner.MarkDirty(); } }
    public int Difficulty { get => Member.Difficulty; set { Member.Difficulty = (byte)Math.Clamp(value, 0, 255); OnPropertyChanged(); OnPropertyChanged(nameof(IvText)); Owner.MarkDirty(); } }
    public int Item { get => Member.Item; set { Member.Item = (ushort)value; OnPropertyChanged(); Owner.MarkDirty(); } }
    public int Move1 { get => Member.Moves[0]; set { Member.Moves[0] = (ushort)value; OnPropertyChanged(); Owner.MarkDirty(); } }
    public int Move2 { get => Member.Moves[1]; set { Member.Moves[1] = (ushort)value; OnPropertyChanged(); Owner.MarkDirty(); } }
    public int Move3 { get => Member.Moves[2]; set { Member.Moves[2] = (ushort)value; OnPropertyChanged(); Owner.MarkDirty(); } }
    public int Move4 { get => Member.Moves[3]; set { Member.Moves[3] = (ushort)value; OnPropertyChanged(); Owner.MarkDirty(); } }
    public int BallSeal { get => Member.BallSeal; set { Member.BallSeal = (ushort)value; OnPropertyChanged(); Owner.MarkDirty(); } }

    public bool ForceMale { get => Member.GenderAbility.HasFlag(GenderAbilityFlags.ForceMale); set => SetFlag(GenderAbilityFlags.ForceMale, value); }
    public bool ForceFemale { get => Member.GenderAbility.HasFlag(GenderAbilityFlags.ForceFemale); set => SetFlag(GenderAbilityFlags.ForceFemale, value); }
    public bool AbilitySlot1 { get => Member.GenderAbility.HasFlag(GenderAbilityFlags.AbilitySlot1); set => SetFlag(GenderAbilityFlags.AbilitySlot1, value); }
    public bool AbilitySlot2 { get => Member.GenderAbility.HasFlag(GenderAbilityFlags.AbilitySlot2); set => SetFlag(GenderAbilityFlags.AbilitySlot2, value); }

    private void SetFlag(GenderAbilityFlags flag, bool on)
    {
        Member.GenderAbility = on ? Member.GenderAbility | flag : Member.GenderAbility & ~flag;
        OnPropertyChanged(nameof(ForceMale)); OnPropertyChanged(nameof(ForceFemale));
        OnPropertyChanged(nameof(AbilitySlot1)); OnPropertyChanged(nameof(AbilitySlot2));
        Owner.MarkDirty();
    }

    public string IvText => Loc.T("tr.ivs", Member.Difficulty * 31 / 255);
    public string Summary => $"{Owner.SpeciesName(Member.Species)} Lv.{Member.Level}";
}

/// <summary>Uma flag de IA do treinador selecionado, como caixa de marcar.</summary>
public sealed class AiFlagOption(int bit, TrainerEditorViewModel owner) : ObservableObject
{
    public string Label => Loc.T($"tr.ai.{bit}");
    public bool IsSet { get => owner.GetAiBit(bit); set => owner.SetAiBit(bit, value); }
    public void Refresh() => OnPropertyChanged(nameof(IsSet));
}

public sealed partial class TrainerEditorViewModel : ObservableObject
{
    private readonly TrainerRepository _repo;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;

    public IReadOnlyList<string> Species => _repo.Species;
    public SpeciesCatalog Catalog { get; }
    public IReadOnlyList<AiFlagOption> AiFlags { get; }
    public IReadOnlyList<string> Items => _repo.Items;
    public IReadOnlyList<string> Moves => _repo.Moves;
    public IReadOnlyList<string> Classes => _repo.Classes;
    public bool HasBallSeal => _repo.Game.HasBallSeal;
    public string GameLabel => Loc.T("tr.gameLabel", _repo.Game.Game, _repo.Trainers.Count);

    public ObservableCollection<TrainerListItem> AllTrainers { get; } = [];
    public ObservableCollection<TrainerListItem> FilteredTrainers { get; } = [];
    public ObservableCollection<PartyRowViewModel> Party { get; } = [];

    [ObservableProperty] private TrainerListItem? _selected;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = "";

    public TrainerEditorViewModel(TrainerRepository repo, Action<string> log, Func<string, Task> snapshot)
    {
        _repo = repo;
        _log = log;
        _snapshot = snapshot;
        Catalog = new SpeciesCatalog(repo.Species, PokemonSprites.Load(repo.Game));
        AiFlags = Enumerable.Range(0, Trainer.AiFlagCount).Select(bit => new AiFlagOption(bit, this)).ToList();
        foreach (var t in repo.Trainers) AllTrainers.Add(new TrainerListItem(t, repo));
        ApplyFilter();
        Selected = FilteredTrainers.FirstOrDefault();
    }

    public string SpeciesName(int id) => id >= 0 && id < Species.Count ? Species[id] : $"#{id}";

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredTrainers.Clear();
        var f = Filter.Trim();
        foreach (var t in AllTrainers)
            if (f.Length == 0 || t.Display.Contains(f, StringComparison.OrdinalIgnoreCase) || t.Trainer.Id.ToString() == f)
                FilteredTrainers.Add(t);
    }

    partial void OnSelectedChanged(TrainerListItem? value)
    {
        Party.Clear();
        if (value is null) return;
        foreach (var m in value.Trainer.Party) Party.Add(new PartyRowViewModel(m, this));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TrainerClass));
        OnPropertyChanged(nameof(Sprite));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasMoves));
        OnPropertyChanged(nameof(DoubleBattle));
        OnPropertyChanged(nameof(Item1)); OnPropertyChanged(nameof(Item2)); OnPropertyChanged(nameof(Item3)); OnPropertyChanged(nameof(Item4));
        OnPropertyChanged(nameof(AiFlagsHex));
        RefreshAiFlags();
        AddPokemonCommand.NotifyCanExecuteChanged();
    }

    public Trainer? Current => Selected?.Trainer;

    // ----- campos do treinador selecionado -----

    public string Name
    {
        get => Selected is null ? "" : _repo.NameOf(Selected.Trainer);
        set
        {
            if (Selected is null) return;
            while (_repo.Names.Count <= Selected.Trainer.Id) _repo.Names.Add("");
            _repo.Names[Selected.Trainer.Id] = value;
            Selected.Refresh();
            OnPropertyChanged();
            MarkDirty();
        }
    }

    public int TrainerClass
    {
        get => Current?.Class ?? 0;
        set { if (Current is null) return; Current.Class = (byte)value; Selected!.Refresh(); OnPropertyChanged(); OnPropertyChanged(nameof(Sprite)); MarkDirty(); }
    }

    /// <summary>A sprite vem da classe: treinadores da mesma classe têm a mesma imagem.</summary>
    public BitmapSource? Sprite => Current is { } t ? _repo.Sprites?.Render(t.Class)?.ToBitmap() : null;

    public bool HasItems { get => Current?.HasItems ?? false; set { if (Current is null) return; Current.HasItems = value; OnPropertyChanged(); MarkDirty(); } }
    public bool HasMoves { get => Current?.HasMoves ?? false; set { if (Current is null) return; Current.HasMoves = value; OnPropertyChanged(); MarkDirty(); } }
    public bool DoubleBattle { get => Current?.DoubleBattle ?? false; set { if (Current is null) return; Current.DoubleBattle = value; OnPropertyChanged(); MarkDirty(); } }

    public int Item1 { get => Current?.Items[0] ?? 0; set => SetItem(0, value); }
    public int Item2 { get => Current?.Items[1] ?? 0; set => SetItem(1, value); }
    public int Item3 { get => Current?.Items[2] ?? 0; set => SetItem(2, value); }
    public int Item4 { get => Current?.Items[3] ?? 0; set => SetItem(3, value); }
    private void SetItem(int i, int v) { if (Current is null) return; Current.Items[i] = (ushort)v; OnPropertyChanged($"Item{i + 1}"); MarkDirty(); }

    public string AiFlagsHex
    {
        get => Current is null ? "" : $"0x{Current.AiFlags:X}";
        set
        {
            if (Current is null) return;
            var s = value.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v)) { Current.AiFlags = v; RefreshAiFlags(); MarkDirty(); }
            OnPropertyChanged();
        }
    }

    public bool GetAiBit(int bit) => Current is { } t && (t.AiFlags & (1u << bit)) != 0;

    public void SetAiBit(int bit, bool on)
    {
        if (Current is not { } t) return;
        t.AiFlags = on ? t.AiFlags | (1u << bit) : t.AiFlags & ~(1u << bit);
        OnPropertyChanged(nameof(AiFlagsHex));
        RefreshAiFlags();
        MarkDirty();
    }

    private void RefreshAiFlags() { foreach (var flag in AiFlags) flag.Refresh(); }

    public void MarkDirty() { IsDirty = true; Status = Loc.T("common.unsaved"); }

    private bool CanAddPokemon() => Current is not null && Current.Party.Count < Trainer.MaxParty;

    [RelayCommand(CanExecute = nameof(CanAddPokemon))]
    private void AddPokemon()
    {
        if (Current is null) return;
        var template = Current.Party.LastOrDefault()?.Clone() ?? new PartyMember { Species = 1, Level = 5 };
        Current.Party.Add(template);
        Party.Add(new PartyRowViewModel(template, this));
        Selected!.Refresh();
        AddPokemonCommand.NotifyCanExecuteChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void RemovePokemon(PartyRowViewModel? row)
    {
        if (row is null || Current is null || Current.Party.Count <= 1) return;
        Current.Party.Remove(row.Member);
        Party.Remove(row);
        Selected!.Refresh();
        AddPokemonCommand.NotifyCanExecuteChanged();
        MarkDirty();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _repo.Save();
            IsDirty = false;
            Status = Loc.T("common.savedAt", DateTime.Now.ToString("HH:mm:ss"));
            _log(Loc.T("tr.savedLog"));
            await _snapshot(Loc.T("tr.snapshot"));
        }
        catch (Exception ex)
        {
            Status = Loc.T("common.saveError", ex.Message);
            _log(Loc.T("tr.saveErrorLog", ex.Message));
        }
    }
}

public sealed partial class TrainerListItem(Trainer trainer, TrainerRepository repo) : ObservableObject
{
    public Trainer Trainer { get; } = trainer;
    public string Display => $"{Trainer.Id:D3}  {repo.ClassOf(Trainer)} {repo.NameOf(Trainer)}  ({Trainer.Party.Count})";
    public void Refresh() => OnPropertyChanged(nameof(Display));
}
