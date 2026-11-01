using System.IO;
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
        Title = (PokedexAreaRepository.IsSpecial(block) ? Loc.T("dx.specialShort") : Loc.T("dx.normalShort")) + " · " + title;
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

    // ----- mapa da Pokédex (lido da ROM) -----

    /// <summary>Mapa renderizado da ROM e formas das áreas; null quando os gráficos não foram encontrados.</summary>
    public PokedexAreaMap? Map { get; private set; }
    public string MapError { get; private set; } = "";
    public bool HasMap => Map is not null;
    /// <summary>Os oito blocos, para o seletor "Mostrar no mapa".</summary>
    public List<AreaBlockViewModel> Blocks { get; } = [];
    [ObservableProperty] private AreaBlockViewModel? _selectedMapBlock;
    /// <summary>Disparado quando as áreas marcadas do bloco mostrado mudam (ou o bloco/espécie muda).</summary>
    public event Action? MapChanged;

    partial void OnSelectedMapBlockChanged(AreaBlockViewModel? value) => MapChanged?.Invoke();

    private void LoadMap(PokedexAreaRepository repo)
    {
        try { Map = PokedexAreaMap.Load(_project); }
        catch (Exception ex) { MapError = Loc.T("dx.mapUnavailable", ex.Message); _log(MapError); }
    }

    /// <summary>Clique no mapa: alterna a área sob o tile no bloco mostrado.</summary>
    public void ToggleAreaAt(int tileX, int tileY)
    {
        if (Map is null || SelectedMapBlock is null) return;
        bool special = PokedexAreaRepository.IsSpecial(SelectedMapBlock.Block);
        var hit = Map.HitTest(tileX, tileY, special, SelectedMapBlock.Areas.Select(a => a.Location.Id));
        if (hit is null) return;
        var area = SelectedMapBlock.Areas.First(a => a.Location.Id == hit);
        area.IsSelected = !area.IsSelected;
    }

    public (uint? Id, string? Name) AreaAt(int tileX, int tileY)
    {
        if (Map is null || SelectedMapBlock is null) return (null, null);
        bool special = PokedexAreaRepository.IsSpecial(SelectedMapBlock.Block);
        var hit = Map.HitTest(tileX, tileY, special, SelectedMapBlock.Areas.Select(a => a.Location.Id));
        return hit is null ? (null, null) : (hit, SelectedMapBlock.Areas.First(a => a.Location.Id == hit).Name);
    }

    private readonly PokeHub.Core.Projects.Project _project;

    public bool CanFollowEncounters => _repo.Game.IsHgss;

    /// <summary>Refaz as áreas de todas as espécies a partir dos encontros. O que estiver sem salvar vai junto.</summary>
    [RelayCommand]
    private async Task FollowEncountersAsync()
    {
        try
        {
            await _snapshot(Loc.T("dx.beforeFollow"));
            var result = PokedexAreaSync.Apply(_project, _repo);
            IsDirty = _repo.HasChanges;
            LoadSpecies();
            Status = Loc.T("dx.followDone", result.Added, result.Removed, result.SpeciesChanged);
            _log(Status);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            Status = Loc.T("common.saveError", ex.Message);
            _log(Status);
        }
    }

    public PokedexAreaEditorViewModel(PokedexAreaRepository repo, PokeHub.Core.Projects.Project project, Action<string> log, Func<string, Task> snapshot)
    {
        _repo = repo;
        _project = project;
        _log = log;
        _snapshot = snapshot;
        LoadMap(repo);
        for (int i = 1; i <= PokedexAreaRepository.SpeciesCount; i++)
            Species.Add($"{i:D3}  {(i < repo.SpeciesNames.Count ? repo.SpeciesNames[i] : "")}");

        NormalBlocks.Add(new(PokedexAreaBlock.NormalMorning, Loc.T("dx.morning"), repo.Normal, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalDay, Loc.T("dx.day"), repo.Normal, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalNight, Loc.T("dx.night"), repo.Normal, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalAll, Loc.T(repo.Game.IsPlatinum ? "dx.extra" : "dx.anytime"), repo.Normal, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialMorning, Loc.T("dx.morning"), repo.Special, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialDay, Loc.T("dx.day"), repo.Special, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialNight, Loc.T("dx.night"), repo.Special, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialAll, Loc.T(repo.Game.IsPlatinum ? "dx.extra" : "dx.anytime"), repo.Special, this));
        if (repo.Game.IsPlatinum)
        {
            NormalBlocks.Add(new(PokedexAreaBlock.NormalNational, Loc.T("dx.national"), repo.Normal, this));
            SpecialBlocks.Add(new(PokedexAreaBlock.SpecialNational, Loc.T("dx.national"), repo.Special, this));
        }
        Blocks.AddRange(NormalBlocks.Concat(SpecialBlocks));
        SelectedMapBlock = NormalBlocks[0]; // manhã
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
        MapChanged?.Invoke();
    }

    public void MarkDirty()
    {
        if (_loading) return;
        foreach (var b in AllBlocks) b.NotifyCount();
        // escreve no repositório na hora: trocar de espécie não perde a edição; só "Salvar" grava no disco
        foreach (var b in AllBlocks) _repo.SetAreas(SelectedSpecies, b.Block, b.SelectedIds);
        IsDirty = _repo.HasChanges;
        Status = IsDirty ? Loc.T("common.unsaved") : "";
        MapChanged?.Invoke();
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
