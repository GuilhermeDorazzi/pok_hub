using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4.Scripts;

public enum EventKind { Npc, Trigger, Sign, LevelScript, Warp }

/// <summary>Uma referência a flag ou variável dentro de um script: ação (set, clear, check, write, compare) e valor envolvido.</summary>
public sealed record FlagRef(int Id, string Action, long? Value);

/// <summary>Um evento de um mapa com o que o script dele faz, em termos de jogo.</summary>
public sealed class EventEntry
{
    public required int MapId { get; init; }
    public required string MapName { get; init; }
    public required string LocationName { get; init; }
    public required EventKind Kind { get; init; }
    public required int Index { get; init; }
    public int ScriptId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    /// <summary>NPC: flag que o esconde quando ligada. Trigger: variável e valor exigidos. Level script: variável e valor.</summary>
    public int? ConditionVar { get; init; }
    public int? ConditionValue { get; init; }
    public int? HideFlag { get; init; }
    public List<FlagRef> Flags { get; } = [];
    public List<FlagRef> Vars { get; } = [];
    public List<string> Messages { get; } = [];
    public List<string> Actions { get; } = [];
    public int InstructionCount { get; set; }
    public bool IsStandardScript { get; init; }

    public string Title => Kind switch
    {
        EventKind.Npc => Loc.T("ev.kind.npc", Index),
        EventKind.Trigger => Loc.T("ev.kind.trigger", Index),
        EventKind.Sign => Loc.T("ev.kind.sign", Index),
        EventKind.LevelScript => Loc.T("ev.kind.level", Index),
        _ => Loc.T("ev.kind.warp", Index),
    };
}

/// <summary>Onde uma flag ou variável é usada.</summary>
public sealed record FlagUsage(int Id, string? Name, List<(EventEntry Event, string Action)> Uses)
{
    public bool IsUsed => Uses.Count > 0;
}

public sealed record FreeRange(int From, int To) { public int Count => To - From + 1; }

/// <summary>
/// Mapa de eventos do jogo (HGSS): para cada mapa, NPCs, triggers, placas e level scripts com o script que chamam e o que
/// o script faz (flags, variáveis, mensagens, itens, Pokémon, batalhas, warps); e, por cima disso, o uso de cada flag e
/// variável do save, com as faixas livres para um hack usar sem colidir com o jogo.
/// </summary>
public sealed class EventMap
{
    public const string ScriptsNarcRelativePath = @"a\0\1\2";
    public const string EventsNarcRelativePath = @"a\0\3\2";

    public Gen4Game Game { get; }
    public FlagNames Names { get; }
    public List<EventEntry> Events { get; } = [];
    public IReadOnlyList<MapHeaderInfo> Headers { get; }
    public IReadOnlyList<string> LocationNames { get; }
    public int MapsWithEvents { get; private set; }

    private readonly Dictionary<int, FlagUsage> _flagUsage = [];
    private readonly Dictionary<int, FlagUsage> _varUsage = [];

    private EventMap(Gen4Game game, IReadOnlyList<MapHeaderInfo> headers, IReadOnlyList<string> locationNames)
    {
        Game = game; Headers = headers; LocationNames = locationNames; Names = FlagNames.Hgss;
    }

    public static EventMap Load(Project project, IProgress<string>? log = null)
    {
        var game = Gen4Game.For(project);
        if (!game.IsHgss) throw new NotSupportedException(Loc.T("core.ev.hgssOnly"));
        var scriptsPath = Path.Combine(game.FilesRoot, ScriptsNarcRelativePath);
        var eventsPath = Path.Combine(game.FilesRoot, EventsNarcRelativePath);
        foreach (var f in new[] { scriptsPath, eventsPath, game.TextArchivePath })
            if (!File.Exists(f)) throw new FileNotFoundException(Loc.T("core.ev.fileMissing"), f);

        var scripts = Narc.Read(File.ReadAllBytes(scriptsPath));
        var events = Narc.Read(File.ReadAllBytes(eventsPath));
        var text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
        List<string> Bank(int b) => b >= 0 && b < text.Files.Count ? Gen4TextArchive.Read(text.Files[b]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList() : [];
        var species = Bank(game.SpeciesBank); var items = Bank(game.ItemsBank); var trainers = Bank(game.TrainerNamesBank); var locations = Bank(game.LocationNamesBank);
        var headers = MapHeaders.Load(game);
        var map = new EventMap(game, headers, locations);
        var table = ScriptCommandTable.Hgss;
        var bankCache = new Dictionary<int, List<string>>();

        foreach (var h in headers)
        {
            if (h.EventsBank < 0 || h.EventsBank >= events.Files.Count) continue;
            var ev = new EventFile(events.Files[h.EventsBank]);
            var scr = h.ScriptsBank >= 0 && h.ScriptsBank < scripts.Files.Count ? new ScriptFile(scripts.Files[h.ScriptsBank], table) : null;
            var level = h.ScriptHeaderBank >= 0 && h.ScriptHeaderBank < scripts.Files.Count ? new MapScriptHeader(scripts.Files[h.ScriptHeaderBank]) : null;
            if (!bankCache.TryGetValue(h.MsgBank, out var msgs)) bankCache[h.MsgBank] = msgs = Bank(h.MsgBank);
            string loc = h.LocationNameIndex >= 0 && h.LocationNameIndex < locations.Count ? locations[h.LocationNameIndex] : "";
            bool any = false;

            EventEntry Make(EventKind kind, int index, int scriptId, int x, int y, int? condVar = null, int? condVal = null, int? hide = null)
            {
                var e = new EventEntry
                {
                    MapId = h.Id, MapName = h.InternalName, LocationName = loc, Kind = kind, Index = index, ScriptId = scriptId, X = x, Y = y,
                    ConditionVar = condVar, ConditionValue = condVal, HideFlag = hide,
                    IsStandardScript = scr is null || scr.IsStandardScript(scriptId),
                };
                if (scr is not null && !e.IsStandardScript) map.Summarize(e, scr.Routine(scriptId)!, scr, msgs, species, items, trainers);
                map.Events.Add(e);
                any = true;
                return e;
            }

            foreach (var o in ev.Objects) Make(EventKind.Npc, o.Index, o.ScriptId, o.X, o.Z, hide: o.EventFlag != 0 ? o.EventFlag : null);
            foreach (var c in ev.Coords) Make(EventKind.Trigger, c.Index, c.ScriptId, c.X, c.Z, c.Var != 0 ? c.Var : null, c.Var != 0 ? c.Val : null);
            foreach (var b in ev.Bg) Make(EventKind.Sign, b.Index, b.ScriptId, b.X, b.Z);
            if (level is not null)
            {
                int i = 0;
                foreach (var t in level.Triggers) Make(EventKind.LevelScript, i++, t.ScriptId, 0, 0, t.Variable, t.Value);
                foreach (var en in level.Entries.Where(en => en.Kind != 1)) Make(EventKind.LevelScript, i++, en.A, 0, 0);
            }
            if (any) map.MapsWithEvents++;
            log?.Report(Loc.T("core.ev.mapDone", h.InternalName, ev.Objects.Count, ev.Coords.Count, ev.Bg.Count));
        }
        map.BuildUsage();
        return map;
    }

    /// <summary>Resume o que uma rotina (e as sub-rotinas que ela chama) faz em termos de jogo.</summary>
    private void Summarize(EventEntry e, ScriptRoutine routine, ScriptFile file, List<string> msgs, List<string> species, List<string> items, List<string> trainers)
    {
        var seen = new HashSet<int> { routine.Offset };
        var queue = new Queue<ScriptRoutine>();
        queue.Enqueue(routine);
        string Sp(List<string> t, long i) => i >= 0 && i < t.Count ? t[(int)i] : $"#{i}";
        var flagSet = new HashSet<FlagRef>(); var varSet = new HashSet<FlagRef>(); var actSet = new HashSet<string>();
        void Flag(FlagRef r) { if (flagSet.Add(r)) e.Flags.Add(r); }
        void Var(FlagRef r) { if (varSet.Add(r)) e.Vars.Add(r); }
        void Act(string a) { if (actSet.Add(a)) e.Actions.Add(a); }
        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            foreach (var ins in r.Instructions)
            {
                e.InstructionCount++;
                switch (ins.Name)
                {
                    case "SetFlag": Flag(new((int)ins.Args[0].Value, "set", null)); break;
                    case "ClearFlag": Flag(new((int)ins.Args[0].Value, "clear", null)); break;
                    case "CheckFlag": Flag(new((int)ins.Args[0].Value, "check", null)); break;
                    case "SetVar": Var(new((int)ins.Args[0].Value, "set", ins.Args[1].Value)); break;
                    case "AddVar" or "SubVar" or "CopyVar" or "SetOrCopyVar": Var(new((int)ins.Args[0].Value, "write", ins.Args[1].Value)); break;
                    case "CompareVarToValue": Var(new((int)ins.Args[0].Value, "compare", ins.Args[1].Value)); break;
                    case "CompareVarToVar": Var(new((int)ins.Args[0].Value, "compare", null)); Var(new((int)ins.Args[1].Value, "compare", null)); break;
                    case "GiveItem": Act(Loc.T("ev.act.item", Sp(items, ins.Args[0].Value))); break;
                    case "TrainerBattle": Act(Loc.T("ev.act.trainer", Sp(trainers, ins.Args[0].Value))); break;
                    case "Warp": Act(Loc.T("ev.act.warp", ins.Args[0].Value)); break;
                    default:
                        if (ins.Name.StartsWith("Give") && ins.Arg("species") is { } sp) Act(Loc.T("ev.act.pokemon", Sp(species, sp.Value)));
                        break;
                }
                var msg = ins.Arg("message");
                if (msg is not null && msg.Value < msgs.Count && e.Messages.Count < 6) { var t = Short(msgs[(int)msg.Value]); if (!e.Messages.Contains(t)) e.Messages.Add(t); }
            }
            foreach (var t in r.Targets)
                if (seen.Add(t)) queue.Enqueue(file.RoutineAt(t));
        }
    }

    private static string Short(string s)
    {
        // o decodificador de texto devolve as quebras como "\r" e "\n" literais
        s = s.Replace("\\r", " ").Replace("\\n", " ").Replace("\r", " ").Replace('\n', ' ');
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Length <= 70 ? s : s[..67] + "...";
    }

    private void BuildUsage()
    {
        foreach (var e in Events)
        {
            foreach (var f in e.Flags) Use(_flagUsage, f.Id, Names.Flag(f.Id), e, f.Action);
            if (e.HideFlag is { } hf) Use(_flagUsage, hf, Names.Flag(hf), e, "hide");
            foreach (var v in e.Vars) Use(_varUsage, v.Id, Names.Var(v.Id), e, v.Action);
            if (e.ConditionVar is { } cv) Use(_varUsage, cv, Names.Var(cv), e, "condition");
        }
    }

    private static void Use(Dictionary<int, FlagUsage> dict, int id, string? name, EventEntry e, string action)
    {
        if (!dict.TryGetValue(id, out var u)) dict[id] = u = new FlagUsage(id, name, []);
        u.Uses.Add((e, action));
    }

    public IReadOnlyList<FlagUsage> FlagUsages => _flagUsage.Values.OrderBy(u => u.Id).ToList();
    public IReadOnlyList<FlagUsage> VarUsages => _varUsage.Values.OrderBy(u => u.Id).ToList();
    public FlagUsage? FlagUsageOf(int id) => _flagUsage.GetValueOrDefault(id);
    public FlagUsage? VarUsageOf(int id) => _varUsage.GetValueOrDefault(id);

    /// <summary>Flags salvas que nenhum script ou evento usa e que a decomp não identificou: candidatas para o hack.</summary>
    public List<FreeRange> FreeFlags() => Ranges(Enumerable.Range(1, Names.NumFlags - 1).Where(id => !_flagUsage.ContainsKey(id) && Names.IsUnidentified(Names.Flag(id))));

    /// <summary>Variáveis salvas que nenhum script ou evento usa e que a decomp não identificou.</summary>
    public List<FreeRange> FreeVars() => Ranges(Enumerable.Range(Names.VarBase, Names.NumVars).Where(id => !_varUsage.ContainsKey(id) && Names.IsUnidentified(Names.Var(id))));

    private static List<FreeRange> Ranges(IEnumerable<int> ids)
    {
        var result = new List<FreeRange>();
        int? start = null, prev = null;
        foreach (var id in ids)
        {
            if (prev is { } p && id == p + 1) { prev = id; continue; }
            if (start is { } s) result.Add(new(s, prev!.Value));
            start = prev = id;
        }
        if (start is { } s2) result.Add(new(s2, prev!.Value));
        return result;
    }
}
