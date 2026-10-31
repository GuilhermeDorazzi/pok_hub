using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;

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

    public string GameLabel => $"{_repo.Game.Game} · áreas da Pokédex (a/1/3/3)";

    public PokedexAreaEditorViewModel(PokedexAreaRepository repo, Action<string> log, Func<string, Task> snapshot)
    {
        _repo = repo;
        _log = log;
        _snapshot = snapshot;
        for (int i = 1; i <= PokedexAreaRepository.SpeciesCount; i++)
            Species.Add($"{i:D3}  {(i < repo.SpeciesNames.Count ? repo.SpeciesNames[i] : "")}");

        NormalBlocks.Add(new(PokedexAreaBlock.NormalMorning, "Manhã", PokedexAreaRepository.NormalLocations, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalDay, "Dia", PokedexAreaRepository.NormalLocations, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalNight, "Noite", PokedexAreaRepository.NormalLocations, this));
        NormalBlocks.Add(new(PokedexAreaBlock.NormalAll, "Qualquer hora", PokedexAreaRepository.NormalLocations, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialMorning, "Manhã", PokedexAreaRepository.SpecialLocations, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialDay, "Dia", PokedexAreaRepository.SpecialLocations, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialNight, "Noite", PokedexAreaRepository.SpecialLocations, this));
        SpecialBlocks.Add(new(PokedexAreaBlock.SpecialAll, "Qualquer hora", PokedexAreaRepository.SpecialLocations, this));
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
        Status = IsDirty ? "Alterações não salvas" : "";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _repo.Save();
            IsDirty = false;
            Status = $"Salvo às {DateTime.Now:HH:mm:ss}";
            _log("Áreas da Pokédex salvas no workspace.");
            await _snapshot("Editor de áreas da Pokédex");
        }
        catch (Exception ex)
        {
            Status = "Erro ao salvar: " + ex.Message;
            _log("ERRO ao salvar áreas da Pokédex: " + ex.Message);
        }
    }
}
