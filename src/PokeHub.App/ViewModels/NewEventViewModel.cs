using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

public sealed record NewEventMap(int Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record NewEventSprite(int Id, BitmapSource Image);

public sealed record NewEventChoice(int Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Cria um NPC: mapa e posição na planta, aparência e o que ele faz. Grava tudo de uma vez, com versão antes e depois.</summary>
public sealed partial class NewEventViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;
    private readonly Gen4Font? _font;
    private EventMap _map;
    private readonly List<NewEventMap> _allMaps;

    public ObservableCollection<NewEventMap> Maps { get; } = [];
    public IReadOnlyList<NewEventSprite> Sprites { get; }
    public IReadOnlyList<NewEventChoice> Facings { get; } =
    [
        new(1, Loc.T("newev.facing.down")), new(0, Loc.T("newev.facing.up")), new(2, Loc.T("newev.facing.left")), new(3, Loc.T("newev.facing.right")),
    ];
    public IReadOnlyList<NewEventChoice> Items { get; }
    public event Action? LayoutChanged;

    [ObservableProperty] private string _mapFilter = "";
    [ObservableProperty] private NewEventMap? _selectedMap;
    [ObservableProperty] private BitmapSource? _layoutImage;
    [ObservableProperty] private NewEventSprite? _selectedSprite;
    [ObservableProperty] private NewEventChoice? _selectedFacing;
    [ObservableProperty] private bool _isTalk = true;
    [ObservableProperty] private bool _isItem;
    [ObservableProperty] private bool _isPokemon;
    [ObservableProperty] private bool _isTrade;
    [ObservableProperty] private NewEventChoice? _selectedMovement;
    [ObservableProperty] private string _range = "2";
    [ObservableProperty] private NewEventChoice? _askSpecies;
    [ObservableProperty] private NewEventChoice? _giveSpecies;
    [ObservableProperty] private string _nickname = "";
    [ObservableProperty] private string _askText = "";
    [ObservableProperty] private string _thanksText = "";
    [ObservableProperty] private string _wrongText = Loc.T("newev.trade.wrongDefault");
    [ObservableProperty] private string _declineText = Loc.T("newev.trade.declineDefault");
    [ObservableProperty] private NewEventChoice? _selectedSpecies;
    [ObservableProperty] private string _level = "10";
    [ObservableProperty] private bool _vanish = true;
    [ObservableProperty] private string _pokemonText = "";
    [ObservableProperty] private BitmapSource? _pokemonSprite;
    [ObservableProperty] private string _talkText = "";
    [ObservableProperty] private string _beforeText = "";
    [ObservableProperty] private string _afterText = "";
    [ObservableProperty] private NewEventChoice? _selectedItem;
    [ObservableProperty] private string _quantity = "1";
    [ObservableProperty] private string _position = Loc.T("newev.clickMap");
    [ObservableProperty] private string _status = "";

    public MapLayout? Layout { get; private set; }
    /// <summary>Tile escolhido, em coordenadas da planta.</summary>
    public (int X, int Y)? Tile { get; private set; }
    public IReadOnlyList<NewEventChoice> Species { get; }
    public bool UsesSpriteList => !IsPokemon;
    public string FreeTradeNote { get; }
    public IReadOnlyList<NewEventChoice> Movements { get; } =
    [
        new((int)NpcMovement.Fixed, Loc.T("newev.move.fixed")), new((int)NpcMovement.LookAround, Loc.T("newev.move.look")),
        new((int)NpcMovement.Wander, Loc.T("newev.move.wander")), new((int)NpcMovement.UpDown, Loc.T("newev.move.updown")), new((int)NpcMovement.LeftRight, Loc.T("newev.move.leftright")),
    ];
    public bool UsesFacing => SelectedMovement?.Value is null or (int)NpcMovement.Fixed;
    public bool UsesRange => SelectedMovement?.Value is (int)NpcMovement.Wander or (int)NpcMovement.UpDown or (int)NpcMovement.LeftRight;

    public NewEventViewModel(Project project, EventMap map, IReadOnlyList<string> items, IReadOnlyList<string> species, Action<string> log, Func<string, Task> snapshot)
    {
        _project = project; _map = map; _log = log; _snapshot = snapshot;
        _font = Gen4Font.Load(map.Game);
        Items = items.Select((name, i) => new NewEventChoice(i, $"{i:D3} {name}")).Skip(1).ToList();
        SelectedItem = Items.FirstOrDefault();
        Species = species.Select((name, i) => new NewEventChoice(i, $"{i:D3} {name}")).Where(s => NewEventService.SpriteForSpecies(s.Value) is not null).ToList();
        SelectedSpecies = Species.FirstOrDefault();
        AskSpecies = Species.FirstOrDefault();
        GiveSpecies = Species.FirstOrDefault();
        SelectedMovement = Movements[0];
        FreeTradeNote = Loc.T("newev.trade.free", NewEventService.FreeTradeSlots(map.Game).Count);
        SelectedFacing = Facings[0];
        Sprites = map.Sprites is { } sprites
            ? sprites.Ids.Select(id => (id, image: sprites.Render(id))).Where(s => s.image is not null).Select(s => new NewEventSprite(s.id, s.image!.ToBitmap())).ToList()
            : [];
        SelectedSprite = Sprites.FirstOrDefault();
        _allMaps = map.Headers.Where(h => h.ScriptsBank >= 0 && h.EventsBank >= 0 && h.MsgBank >= 0)
            .Select(h => new NewEventMap(h.Id, $"{h.InternalName}  {(h.LocationNameIndex >= 0 && h.LocationNameIndex < map.LocationNames.Count ? map.LocationNames[h.LocationNameIndex] : "")}".Trim())).ToList();
        ApplyMapFilter();
        SelectedMap = Maps.FirstOrDefault();
    }

    partial void OnMapFilterChanged(string value) => ApplyMapFilter();
    partial void OnIsPokemonChanged(bool value) => OnPropertyChanged(nameof(UsesSpriteList));
    partial void OnSelectedMovementChanged(NewEventChoice? value)
    {
        OnPropertyChanged(nameof(UsesFacing));
        OnPropertyChanged(nameof(UsesRange));
    }
    partial void OnSelectedSpeciesChanged(NewEventChoice? value) =>
        PokemonSprite = value is not null && NewEventService.SpriteForSpecies(value.Value) is { } sprite ? SpriteOf(sprite) : null;

    private void ApplyMapFilter()
    {
        var keep = SelectedMap;
        Maps.Clear();
        foreach (var m in _allMaps.Where(m => string.IsNullOrWhiteSpace(MapFilter) || m.Label.Contains(MapFilter, StringComparison.OrdinalIgnoreCase))) Maps.Add(m);
        if (keep is not null && Maps.Contains(keep)) SelectedMap = keep;
    }

    partial void OnSelectedMapChanged(NewEventMap? value) => LoadLayout();

    private void LoadLayout()
    {
        Tile = null;
        Position = Loc.T("newev.clickMap");
        Layout = SelectedMap is { } m ? _map.LayoutOf(m.Id) : null;
        LayoutImage = Layout?.Render().ToBitmap();
        LayoutChanged?.Invoke();
    }

    public void PickTile(int x, int y)
    {
        if (Layout is null || x < 0 || y < 0 || x >= Layout.Width || y >= Layout.Height) return;
        Tile = (x, y);
        Position = Loc.T(Layout.IsBlocked(x, y) ? "newev.tileBlocked" : "newev.tile", x + Layout.OriginX, y + Layout.OriginY);
        LayoutChanged?.Invoke();
    }

    public BitmapSource? SpriteOf(int id) => _map.Sprites?.Render(id)?.ToBitmap();

    /// <summary>Texto como o jogo vai receber: quebrado para caber na caixa, com a fonte do próprio jogo.</summary>
    private string Prepare(string text)
    {
        var map = (Func<string, int>)(s => _font is null ? s.Length * 6 : TextPreview.Render(Gen4TextArchive.Encode(s), _font).Lines.Max(l => l.Width));
        return TextPreview.Wrap(text.Trim(), map);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (SelectedMap is not { } map || Layout is null) { Status = Loc.T("newev.needMap"); return; }
        if (Tile is not { } tile) { Status = Loc.T("newev.clickMap"); return; }
        if (SelectedSprite is not { } sprite || SelectedFacing is not { } facing) { Status = Loc.T("newev.needSprite"); return; }
        if (IsTalk && string.IsNullOrWhiteSpace(TalkText) || IsItem && (string.IsNullOrWhiteSpace(BeforeText) || string.IsNullOrWhiteSpace(AfterText))) { Status = Loc.T("newev.needText"); return; }
        if (IsItem && (SelectedItem is null || !int.TryParse(Quantity, out _))) { Status = Loc.T("core.newev.badQuantity"); return; }
        if (IsPokemon && (SelectedSpecies is null || !int.TryParse(Level, out _))) { Status = Loc.T("core.newev.badLevel"); return; }
        if (IsTrade && (AskSpecies is null || GiveSpecies is null)) { Status = Loc.T("core.newev.badSpecies"); return; }
        if (IsTrade && (string.IsNullOrWhiteSpace(AskText) || string.IsNullOrWhiteSpace(ThanksText) || string.IsNullOrWhiteSpace(WrongText) || string.IsNullOrWhiteSpace(DeclineText))) { Status = Loc.T("newev.needText"); return; }
        int range = 1;
        if (UsesRange && !int.TryParse(Range, out range)) { Status = Loc.T("core.newev.badRange", 8); return; }

        try
        {
            NpcBehavior behavior = IsTalk ? new TalkBehavior(Prepare(TalkText))
                : IsItem ? new GiveItemBehavior(SelectedItem!.Value, int.Parse(Quantity), Prepare(BeforeText), Prepare(AfterText))
                : IsTrade ? new TradeBehavior(AskSpecies!.Value, GiveSpecies!.Value, string.IsNullOrWhiteSpace(Nickname) ? null : Nickname.Trim(), Prepare(AskText), Prepare(ThanksText), Prepare(WrongText), Prepare(DeclineText))
                : new PokemonBehavior(SelectedSpecies!.Value, int.Parse(Level), Vanish, string.IsNullOrWhiteSpace(PokemonText) ? null : Prepare(PokemonText));
            await _snapshot(Loc.T("newev.snapshotBefore", map.Label));
            var result = NewEventService.Create(_project, _map, new NewNpc(map.Id, tile.X + Layout.OriginX, tile.Y + Layout.OriginY, sprite.Id, facing.Value, behavior, (NpcMovement)(SelectedMovement?.Value ?? 0), range));
            await _snapshot(Loc.T("newev.snapshotAfter", map.Label));
            _map = EventMap.Load(_project); // flags, scripts e planta precisam refletir o NPC novo
            LoadLayout();
            Status = Loc.T("newev.created", result.ScriptId) + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : "");
            _log(Status);
            TalkText = BeforeText = AfterText = PokemonText = AskText = ThanksText = Nickname = "";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.InvalidDataException or NotSupportedException or System.IO.IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            Status = Loc.T("common.saveError", ex.Message);
        }
    }
}
