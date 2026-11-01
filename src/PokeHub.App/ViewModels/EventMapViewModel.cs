using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Localization;

namespace PokeHub.App.ViewModels;

public sealed record MapRow(int Id, string Name, string Location, int Count)
{
    public string Title => Location.Length > 0 ? $"{Location}  ({Name})" : Name;
}

/// <summary>Linha de flag ou variável na tabela: id, nome da decomp, quantos eventos usam e onde.</summary>
public sealed record SlotRow(int Id, string? Name, int UsesCount, string Where, bool IsFree)
{
    public string IdHex => $"0x{Id:X}";
    public bool Matches(string search) =>
        IdHex.Contains(search, StringComparison.OrdinalIgnoreCase) || Id.ToString().Contains(search) || (Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) || Where.Contains(search, StringComparison.OrdinalIgnoreCase);
    public string NameOrDash => Name ?? "-";
    public string Status => IsFree ? Loc.T("ev.free") : UsesCount == 0 ? Loc.T("ev.namedOnly") : Loc.T("ev.usedBy", UsesCount);
}

public enum SlotFilter { All, Used, Free }
public sealed record SlotFilterOption(SlotFilter Filter) { public string Name => Loc.T("ev.filter." + Filter.ToString().ToLowerInvariant()); }

/// <summary>Mapa de eventos: mapas → eventos → o que o script faz; e tabelas de flags e variáveis com faixas livres.</summary>
public sealed partial class EventMapViewModel : ObservableObject
{
    private readonly EventMap _map;
    private readonly RealMapImages _realMap;
    private int _imageRequest;

    public EventMapViewModel(EventMap map)
    {
        _map = map;
        _realMap = new RealMapImages(map.Game);
        AllMaps = map.Events.GroupBy(e => e.MapId).Select(g => new MapRow(g.Key, g.First().MapName, g.First().LocationName, g.Count())).OrderBy(m => m.Id).ToList();
        Filters = [new(SlotFilter.All), new(SlotFilter.Used), new(SlotFilter.Free)];
        SelectedFlagFilter = Filters[2];
        SelectedVarFilter = Filters[2];
        FreeFlagsText = Loc.T("ev.freeRanges", map.FreeFlags().Sum(r => r.Count), string.Join(", ", map.FreeFlags().OrderByDescending(r => r.Count).Take(8).Select(Range)));
        FreeVarsText = Loc.T("ev.freeRanges", map.FreeVars().Sum(r => r.Count), string.Join(", ", map.FreeVars().OrderByDescending(r => r.Count).Take(8).Select(Range)));
        Summary = Loc.T("ev.summary", map.MapsWithEvents, map.Events.Count, map.FlagUsages.Count, map.Names.NumFlags - 1, map.VarUsages.Count, map.Names.NumVars);
        ApplyMapFilter();
        RebuildFlags();
        RebuildVars();
    }

    private static string Range(FreeRange r) => r.Count == 1 ? $"{r.From}" : $"{r.From}–{r.To} ({r.Count})";

    public string Summary { get; }
    public string FreeFlagsText { get; }
    public string FreeVarsText { get; }
    public IReadOnlyList<SlotFilterOption> Filters { get; }

    // ----- mapas e eventos -----
    public List<MapRow> AllMaps { get; }
    public ObservableCollection<MapRow> Maps { get; } = [];
    [ObservableProperty] private string _mapSearch = "";
    [ObservableProperty] private MapRow? _selectedMap;
    public ObservableCollection<EventEntry> Events { get; } = [];
    [ObservableProperty] private EventEntry? _selectedEvent;
    [ObservableProperty] private string _eventDetails = "";

    partial void OnMapSearchChanged(string value) => ApplyMapFilter();

    /// <summary>Filtra mapas por nome/local; um número (decimal ou 0x...) acha flag ou variável, e uma frase busca dentro dos eventos.</summary>
    private void ApplyMapFilter()
    {
        var q = MapSearch.Trim();
        Maps.Clear();
        IEnumerable<MapRow> rows = AllMaps;
        if (q.Length > 0)
        {
            HashSet<int> mapIds;
            if (FlagNames.ParseId(q) is { } id)
                mapIds = _map.Events.Where(e => e.Flags.Any(f => f.Id == id) || e.HideFlag == id || e.Vars.Any(v => v.Id == id) || e.ConditionVar == id).Select(e => e.MapId).ToHashSet();
            else
                mapIds = _map.Events.Where(e => e.Messages.Any(m => m.Contains(q, StringComparison.OrdinalIgnoreCase)) || e.Actions.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase))).Select(e => e.MapId).ToHashSet();
            rows = rows.Where(m => mapIds.Contains(m.Id) || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || m.Location.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var r in rows) Maps.Add(r);
        // os primeiros da lista podem ser mapas sem célula própria, que só existem como planta do mundo inteiro
        if (SelectedMap is null || !Maps.Contains(SelectedMap))
            SelectedMap = Maps.FirstOrDefault(m => _map.LayoutOf(m.Id) is { } layout && RealMapRenderer.CanRender(layout)) ?? Maps.FirstOrDefault();
    }

    partial void OnSelectedMapChanged(MapRow? value)
    {
        Events.Clear();
        if (value is null)
        {
            // busca sem resultado: não deixa na tela o mapa anterior
            Layout = null;
            _ = ShowLayoutImageAsync();
            return;
        }
        foreach (var e in _map.Events.Where(e => e.MapId == value.Id)) Events.Add(e);
        Layout = _map.LayoutOf(value.Id);
        LayoutStatus = Layout is null ? Loc.T("ev.layout.none") : "";
        _ = ShowLayoutImageAsync();
        SelectedEvent = Events.FirstOrDefault();
    }

    partial void OnSelectedEventChanged(EventEntry? value)
    {
        EventDetails = value is null ? "" : Describe(value);
        LayoutChanged?.Invoke();
    }

    // ----- planta do mapa -----
    /// <summary>Tiles por lado em pixels na tela: o mapa real é desenhado com mais detalhe que a planta.</summary>
    public int TileScale { get; private set; } = 8;
    public event Action? LayoutChanged;
    [ObservableProperty] private bool _showRealMap = true;
    [ObservableProperty] private bool _showsPlan = true;
    public bool RealMapAvailable => _realMap.Available;

    partial void OnShowRealMapChanged(bool value) => _ = ShowLayoutImageAsync();

    /// <summary>A planta aparece na hora; o mapa real é desenhado fora da tela e entra quando fica pronto, se o mapa ainda for o mesmo.</summary>
    private async Task ShowLayoutImageAsync()
    {
        int request = ++_imageRequest;
        var layout = Layout;
        var map = SelectedMap;
        var failed = Loc.T("newev.realMap.failed");
        if (LayoutStatus == failed) LayoutStatus = "";
        bool wantsReal = ShowRealMap && _realMap.Available && layout is not null && map is not null;
        if (wantsReal && _realMap.Known(map!.Id) is { } known)
        {
            SetLayoutImage(known, RealMapRenderer.PixelsPerTile);
            return;
        }
        SetLayoutImage(layout?.Render().ToBitmap(), 8);
        if (!wantsReal) return;

        var image = await _realMap.GetAsync(layout!, map!.Id, _map.Headers.First(h => h.Id == map.Id).AreaId);
        if (request != _imageRequest) return;
        if (image is null) LayoutStatus = failed;
        else SetLayoutImage(image, RealMapRenderer.PixelsPerTile);
    }

    private void SetLayoutImage(BitmapSource? image, int scale)
    {
        LayoutImage = image;
        TileScale = scale;
        ShowsPlan = scale != RealMapRenderer.PixelsPerTile;
        LayoutChanged?.Invoke();
    }
    public MapLayout? Layout { get; private set; }

    private Dictionary<int, System.Windows.Media.Imaging.BitmapSource?> _npcSprites = [];

    /// <summary>Sprite do NPC na planta, ou null para ficar só com o marcador.</summary>
    public System.Windows.Media.Imaging.BitmapSource? NpcSprite(LayoutMarker m)
    {
        if (m.SpriteId is not { } id || _map.Sprites is not { } sprites) return null;
        if (!_npcSprites.TryGetValue(id, out var bitmap)) _npcSprites[id] = bitmap = sprites.Render(id)?.ToBitmap();
        return bitmap;
    }
    [ObservableProperty] private BitmapSource? _layoutImage;
    [ObservableProperty] private string _layoutStatus = "";

    public bool IsSelected(LayoutMarker m) => SelectedEvent is { } e && e.Kind == m.Kind && e.Index == m.Index;

    public string MarkerTip(LayoutMarker m)
    {
        if (m.Kind == EventKind.Warp) return m.TargetMap is { } t ? Loc.T("ev.layout.warpTip", m.Index, _map.MapTitle(t)) : Loc.T("ev.layout.warpDynamic", m.Index);
        var e = EventOf(m);
        if (e is null) return Loc.T("ev.position", m.X, m.Y);
        var summary = e.Actions.FirstOrDefault() ?? e.Messages.FirstOrDefault();
        return summary is null ? e.Title : $"{e.Title}: {summary}";
    }

    private EventEntry? EventOf(LayoutMarker m) => Events.FirstOrDefault(e => e.Kind == m.Kind && e.Index == m.Index);

    /// <summary>Clique na planta: evento vai para os detalhes; warp abre o mapa de destino.</summary>
    public void ActivateMarker(LayoutMarker m)
    {
        if (m.Kind != EventKind.Warp) { if (EventOf(m) is { } e) SelectedEvent = e; return; }
        if (m.TargetMap is not { } target) { LayoutStatus = Loc.T("ev.layout.warpDynamic", m.Index); return; }
        var row = AllMaps.FirstOrDefault(r => r.Id == target);
        if (row is null) { LayoutStatus = Loc.T("ev.layout.noEvents", _map.MapTitle(target)); return; }
        if (!Maps.Contains(row)) MapSearch = "";
        SelectedMap = row;
    }

    public string Describe(EventEntry e)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{e.Title} · {Loc.T("ev.script", e.ScriptId)}{(e.IsStandardScript ? " " + Loc.T("ev.standard") : "")}");
        if (e.Kind is EventKind.Npc or EventKind.Trigger or EventKind.Sign) sb.AppendLine(Loc.T("ev.position", e.X, e.Y));
        if (e.HideFlag is { } hf) sb.AppendLine(Loc.T("ev.hideFlag", FlagNames.Label(hf, _map.Names.Flag(hf))));
        if (e.ConditionVar is { } cv) sb.AppendLine(Loc.T("ev.condition", FlagNames.Label(cv, _map.Names.Var(cv)), e.ConditionValue));
        if (e.Flags.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine(Loc.T("ev.flagsTitle"));
            foreach (var f in e.Flags) sb.AppendLine($"  {Loc.T("ev.action." + f.Action)}  {FlagNames.Label(f.Id, _map.Names.Flag(f.Id))}");
        }
        if (e.Vars.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine(Loc.T("ev.varsTitle"));
            foreach (var v in e.Vars) sb.AppendLine($"  {Loc.T("ev.action." + v.Action)}  {FlagNames.Label(v.Id, _map.Names.Var(v.Id))}{(v.Value is { } val ? " " + (v.Action == "compare" ? "?" : "=") + " " + val : "")}");
        }
        if (e.Actions.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine(Loc.T("ev.actionsTitle"));
            foreach (var a in e.Actions) sb.AppendLine("  " + a);
        }
        if (e.Messages.Count > 0)
        {
            sb.AppendLine(); sb.AppendLine(Loc.T("ev.messagesTitle"));
            foreach (var m in e.Messages) sb.AppendLine("  \"" + m + "\"");
        }
        if (e.InstructionCount > 0) { sb.AppendLine(); sb.AppendLine(Loc.T("ev.instructions", e.InstructionCount)); }
        return sb.ToString();
    }

    // ----- flags e variáveis -----
    public ObservableCollection<SlotRow> FlagRows { get; } = [];
    public ObservableCollection<SlotRow> VarRows { get; } = [];
    [ObservableProperty] private SlotFilterOption _selectedFlagFilter;
    [ObservableProperty] private SlotFilterOption _selectedVarFilter;
    [ObservableProperty] private string _flagSearch = "";
    [ObservableProperty] private string _varSearch = "";

    partial void OnSelectedFlagFilterChanged(SlotFilterOption value) => RebuildFlags();
    partial void OnSelectedVarFilterChanged(SlotFilterOption value) => RebuildVars();
    partial void OnFlagSearchChanged(string value) => RebuildFlags();
    partial void OnVarSearchChanged(string value) => RebuildVars();

    private string Where(FlagUsage? u) => u is null ? "" : string.Join("; ", u.Uses.Take(3).Select(x => $"{(x.Event.LocationName.Length > 0 ? x.Event.LocationName : x.Event.MapName)}: {x.Event.Title} ({Loc.T("ev.action." + x.Action)})")) + (u.Uses.Count > 3 ? " …" : "");

    private void RebuildFlags()
    {
        FlagRows.Clear();
        var names = _map.Names;
        foreach (var id in Enumerable.Range(1, names.NumFlags - 1))
        {
            var u = _map.FlagUsageOf(id); var name = names.Flag(id);
            bool free = u is null && names.IsUnidentified(name);
            if (!Pass(SelectedFlagFilter.Filter, u is not null, free)) continue;
            var row = new SlotRow(id, name, u?.Uses.Count ?? 0, Where(u), free);
            if (FlagSearch.Length > 0 && !row.Matches(FlagSearch)) continue;
            FlagRows.Add(row);
        }
    }

    private void RebuildVars()
    {
        VarRows.Clear();
        var names = _map.Names;
        foreach (var id in Enumerable.Range(names.VarBase, names.NumVars))
        {
            var u = _map.VarUsageOf(id); var name = names.Var(id);
            bool free = u is null && names.IsUnidentified(name);
            if (!Pass(SelectedVarFilter.Filter, u is not null, free)) continue;
            var row = new SlotRow(id, name, u?.Uses.Count ?? 0, Where(u), free);
            if (VarSearch.Length > 0 && !row.Matches(VarSearch)) continue;
            VarRows.Add(row);
        }
    }

    private static bool Pass(SlotFilter f, bool used, bool free) => f switch { SlotFilter.Used => used, SlotFilter.Free => free, _ => true };
}
