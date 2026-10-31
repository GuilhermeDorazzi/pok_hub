using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

public sealed partial class AreaCheckViewModel(PokedexLocation location, Action changed) : ObservableObject
{
    public PokedexLocation Location { get; } = location;
    public string Name => Location.Name;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) changed(); }
    }

    /// <summary>Define sem disparar a notificação de alteração (usado ao carregar).</summary>
    public void Load(bool selected) { _isSelected = selected; OnPropertyChanged(nameof(IsSelected)); }
}

public sealed partial class AreaBlockViewModel : ObservableObject
{
    private readonly PokedexAreaEditorViewModel _owner;
    public PokedexAreaBlock Block { get; }
    public string Title { get; }
    public ObservableCollection<AreaCheckViewModel> Areas { get; } = [];

    public AreaBlockViewModel(PokedexAreaBlock block, string title, IReadOnlyList<PokedexLocation> locations, PokedexAreaEditorViewModel owner)
    {
        _owner = owner;
        Block = block;
        Title = title;
        foreach (var l in locations) Areas.Add(new AreaCheckViewModel(l, owner.MarkDirty));
    }

    public IEnumerable<uint> SelectedIds => Areas.Where(a => a.IsSelected).Select(a => a.Location.Id);
    public int SelectedCount => Areas.Count(a => a.IsSelected);

    public void Load(IReadOnlyList<uint> ids)
    {
        var set = ids.ToHashSet();
        foreach (var a in Areas) a.Load(set.Contains(a.Location.Id));
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand] private void SelectAll() { foreach (var a in Areas) a.IsSelected = true; }
    [RelayCommand] private void ClearAll() { foreach (var a in Areas) a.IsSelected = false; }

    public void NotifyCount() => OnPropertyChanged(nameof(SelectedCount));

    public void Sort(bool byName)
    {
        var ordered = (byName ? Areas.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase) : Areas.OrderBy(a => a.Location.Id)).ToList();
        Areas.Clear();
        foreach (var a in ordered) Areas.Add(a);
    }
}

public sealed partial class PokedexAreaEditorViewModel : ObservableObject
{
    private readonly PokedexAreaRepository _repo;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;
    private bool _loading;

    public ObservableCollection<string> Species { get; } = [];
    public ObservableCollection<AreaBlockViewModel> NormalBlocks { get; } = [];
    public ObservableCollection<AreaBlockViewModel> SpecialBlocks { get; } = [];

    [ObservableProperty] private int _selectedSpecies = 1;
    [ObservableProperty] private bool _orderByName;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _status = "";

    public string GameLabel => Loc.T("dx.gameLabel", _repo.Game.Game);

    public PokedexAreaEditorViewModel(PokedexAreaRepository repo, Action<string> log, Func<string, Task> snapshot)
    {
        _repo = repo;
        _log = log;
        _snapshot = snapshot;
        for (int i = 1; i <= PokedexAreaRepository.SpeciesCount; i++)
            Species.Add($"{i:D3}  {(i < repo.SpeciesNames.Count ? repo.SpeciesNames[i] : "")}");

        NormalBlocks.Add(new(PokedexAreaBlock.NormalMorning, Loc.T("dx.morning"), PokedexAreaRepository.NormalLocations, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalDay, Loc.T("dx.day"), PokedexAreaRepository.NormalLocations, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalNight, Loc.T("dx.night"), PokedexAreaRepository.NormalLocations, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalAll, Loc.T("dx.anytime"), PokedexAreaRepository.NormalLocations, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialMorning, Loc.T("dx.morning"), PokedexAreaRepository.SpecialLocations, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialDay, Loc.T("dx.day"), PokedexAreaRepository.SpecialLocations, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialNight, Loc.T("dx.night"), PokedexAreaRepository.SpecialLocations, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialAll, Loc.T("dx.anytime"), PokedexAreaRepository.SpecialLocations, this));
        LoadSpecies();
    }

    private IEnumerable<AreaBlockViewModel> AllBlocks => NormalBlocks.Concat(SpecialBlocks);

    /// <summary>Índice no ComboBox é species - 1.</summary>
    public int SelectedIndex
    {
        get => SelectedSpecies - 1;
        set { if (value >= 0) SelectedSpecies = value + 1; }
    }

    partial void OnSelectedSpeciesChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedIndex));
        LoadSpecies();
    }

    partial void OnOrderByNameChanged(bool value)
    {
        foreach (var b in AllBlocks) b.Sort(value);
    }

    private void LoadSpecies()
    {
        _loading = true;
        try
        {
            foreach (var b in AllBlocks) b.Load(_repo.GetAreas(SelectedSpecies, b.Block));
        }
        finally { _loading = false; }
    }

    public void MarkDirty()
    {
        if (_loading) return;
        foreach (var b in AllBlocks) b.NotifyCount();
        // escreve no repositório na hora: trocar de espécie não perde a edição; só "Salvar" grava no disco
        foreach (var b in AllBlocks) _repo.SetAreas(SelectedSpecies, b.Block, b.SelectedIds);
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
            _log(Loc.T("dx.savedLog"));
            await _snapshot(Loc.T("dx.snapshot"));
        }
        catch (Exception ex)
        {
            Status = Loc.T("common.saveError", ex.Message);
            _log(Loc.T("dx.saveErrorLog", ex.Message));
        }
    }
}
