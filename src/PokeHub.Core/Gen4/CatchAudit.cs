using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

public enum CatchStatus { Found, Family, Known, Missing }

/// <summary>Uma espécie: se dá para obter, onde e o que a varredura sabe sobre ela. TradeOnly marca a evolução que só acontece em troca.</summary>
public sealed record CatchRow(int Species, string Name, CatchStatus Status, string Where, string Note, bool TradeOnly);

/// <summary>
/// Só leitura: cruza encontros, scripts, trocas e evoluções para dizer o que dá para pegar no HG/SS.
/// Enxerga o que está nos dados. O que o jogo sorteia em código (iniciais, fósseis, concurso) entra por lista fixa e fica como não confirmado.
/// </summary>
public static class CatchAudit
{
    private const int LastSpecies = 493, VarBase = 0x4000, MaxPlaces = 3, EvoTrade = 5, EvoTradeItem = 6;

    // o que precisa de script mas usa variável, ou que o jogo escolhe em código
    private static readonly Dictionary<int, string> Known = new()
    {
        [152] = "starter", [155] = "starter", [158] = "starter",
        [252] = "hoenn", [255] = "hoenn", [258] = "hoenn",
        [387] = "sinnoh", [390] = "sinnoh", [393] = "sinnoh",
        [243] = "roamer", [244] = "roamer", [249] = "roamer", [250] = "roamer",
        [138] = "fossil", [140] = "fossil", [142] = "fossil", [345] = "fossil", [347] = "fossil", [408] = "fossil", [410] = "fossil",
        [137] = "prize", [175] = "prize",
        [10] = "contest", [123] = "contest", [127] = "contest",
    };

    // versão exclusiva, conforme serebii.net/heartgoldsoulsilver/exclusives.shtml
    private static readonly HashSet<int> HeartGoldOnly = [56, 57, 58, 59, 167, 168, 207, 226, 231, 232, 302, 343, 344, 382, 458, 472];
    private static readonly HashSet<int> SoulSilverOnly = [37, 38, 52, 53, 165, 166, 216, 217, 225, 227, 303, 316, 317, 383];

    // só nos passeios do Pokéwalker, conforme serebii.net/heartgoldsoulsilver/pokewalker-area.shtml
    private static readonly HashSet<int> Walker = [300, 318, 320, 349, 351, 352, 357, 361, 422, 442, 456, 459];
    private static readonly HashSet<int> Events = [151, 251, 385, 386, 489, 490, 491, 492, 493];
    private static readonly HashSet<int> OtherGames = [377, 378, 379, 479, 480, 481, 482, 483, 484, 485, 486, 487, 488];

    public static IReadOnlyList<CatchRow> Build(Project project)
    {
        var game = Gen4Game.For(project);
        if (!game.IsHgss) throw new NotSupportedException(Loc.T("core.catch.hgssOnly"));

        var encounters = EncounterRepository.Load(project);
        var places = new Dictionary<int, List<string>>();
        void Add(int species, string place)
        {
            if (species is < 1 or > LastSpecies) return;
            if (!places.TryGetValue(species, out var list)) places[species] = list = [];
            if (!list.Contains(place)) list.Add(place);
        }

        foreach (var e in encounters.Entries)
        {
            if (e.Hgss is not { } h || e.Maps.Count == 0) continue;
            var place = Loc.T("catch.source.encounter", encounters.LocationName(e.Maps[0].LocationNameIndex));
            foreach (var s in h.Morning.Concat(h.Day).Concat(h.Night).Concat(h.HoennSound).Concat(h.SinnohSound).Concat(h.Swarm)) Add(s, place);
            foreach (var s in h.Surf.Concat(h.RockSmash).Concat(h.OldRod).Concat(h.GoodRod).Concat(h.SuperRod)) Add(s.Species, place);
        }

        var tables = GameTables.Load(project);
        void FromTable(string id, string column, string place)
        {
            if (tables.FirstOrDefault(t => t.Id == id) is not { } table) return;
            var c = table.Columns.First(k => k.Key == column);
            int owners = table.Owners?.Count ?? 1;
            for (int o = 0; o < owners; o++)
                foreach (var row in table.Rows(o)) Add(row.Get(c), place);
        }
        FromTable("headbutt", "species", Loc.T("catch.source.headbutt"));
        FromTable("safari", "species", Loc.T("catch.source.safari"));
        FromTable("trades", "giveSpecies", Loc.T("catch.source.trade"));

        var scripts = ScriptArchive.Load(project);
        for (int file = 0; file < scripts.Names.Count; file++)
        {
            ScriptEditor editor;
            try { editor = scripts.Open(file); }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { continue; }
            var name = scripts.Names[file];
            int space = name.IndexOf(' ');
            var place = Loc.T("catch.source.script", space > 0 ? name[(space + 1)..] : name);
            foreach (var i in editor.Instructions())
                if (i.Name is "GiveMon" or "GiveEgg" or "WildBattle" or "RocketTrapBattle" or "ScrCmd_452" or "ScrCmd_517"
                    && i.Arg("species") is { } a && a.Value < VarBase)
                    Add((int)a.Value, place);
        }

        // famílias: evolução para cima, filhote para baixo
        var parent = Enumerable.Range(0, LastSpecies + 1).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        var tradeOnly = new HashSet<int>();
        var regular = new HashSet<int>();
        if (tables.FirstOrDefault(t => t.Id == "evolutions") is { } evolutions)
        {
            var method = evolutions.Columns.First(c => c.Key == "method");
            var target = evolutions.Columns.First(c => c.Key == "target");
            for (int o = 1; o <= LastSpecies && o < (evolutions.Owners?.Count ?? 0); o++)
                foreach (var row in evolutions.Rows(o))
                {
                    int to = row.Get(target), how = row.Get(method);
                    if (how == 0 || to is < 1 or > LastSpecies) continue;
                    parent[Find(o)] = Find(to);
                    (how is EvoTrade or EvoTradeItem ? tradeOnly : regular).Add(to);
                }
        }
        tradeOnly.ExceptWith(regular);

        var foundFamilies = places.Keys.Select(Find).ToHashSet();
        var knownFamilies = Known.Keys.Select(Find).ToHashSet();
        var rows = new List<CatchRow>();
        for (int s = 1; s <= LastSpecies; s++)
        {
            var name = TableNames.At(encounters.Species, s);
            bool trade = tradeOnly.Contains(s);
            string note = trade ? Loc.T("catch.note.tradeOnly") : "";
            if (places.TryGetValue(s, out var list))
            {
                var where = string.Join("; ", list.Take(MaxPlaces)) + (list.Count > MaxPlaces ? "; …" : "");
                rows.Add(new CatchRow(s, name, CatchStatus.Found, where, note, trade));
            }
            else if (Known.TryGetValue(s, out var known))
                rows.Add(new CatchRow(s, name, CatchStatus.Known, "", Loc.T($"catch.note.{known}"), trade));
            else if (foundFamilies.Contains(Find(s)))
                rows.Add(new CatchRow(s, name, CatchStatus.Family, "", note, trade));
            else if (knownFamilies.Contains(Find(s)))
                rows.Add(new CatchRow(s, name, CatchStatus.Known, "", note, trade));
            else
                rows.Add(new CatchRow(s, name, CatchStatus.Missing, "", note.Length > 0 ? note : MissingNote(s, Find), trade));
        }
        return rows;
    }

    /// <summary>A observação vale para a família inteira: Wailord falta pelo mesmo motivo que Wailmer.</summary>
    private static string MissingNote(int species, Func<int, int> family)
    {
        bool In(HashSet<int> list) => list.Any(s => family(s) == family(species));
        if (In(HeartGoldOnly)) return Loc.T("catch.note.hgOnly");
        if (In(SoulSilverOnly)) return Loc.T("catch.note.ssOnly");
        if (In(Walker)) return Loc.T("catch.note.walker");
        if (In(Events)) return Loc.T("catch.note.event");
        if (In(OtherGames)) return Loc.T("catch.note.otherGame");
        return "";
    }
}
