using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Faz as áreas da Pokédex do HG/SS seguirem os encontros reais. Em manhã, dia e noite ficam grama, surfe, varas e Rock Smash, como no jogo.
/// O bloco "todos" é o das cabeçadas, que valem a qualquer hora. Locais sem par nos encontros ficam como estão, como a Safari.
/// </summary>
public static class PokedexAreaSync
{
    public sealed record Result(int Added, int Removed, int SpeciesChanged);

    private static readonly (PokedexAreaBlock Normal, PokedexAreaBlock Special, Func<HgssEncounters, ushort[]> Grass)[] Periods =
    [
        (PokedexAreaBlock.NormalMorning, PokedexAreaBlock.SpecialMorning, h => h.Morning),
        (PokedexAreaBlock.NormalDay, PokedexAreaBlock.SpecialDay, h => h.Day),
        (PokedexAreaBlock.NormalNight, PokedexAreaBlock.SpecialNight, h => h.Night),
    ];

    /// <summary>
    /// Nome do local no jogo para o id da Pokédex. Rotas e cidades ficam no bloco normal; o resto, no especial.
    /// Null para os locais que a Pokédex não tem.
    /// </summary>
    public static (uint Id, bool Special)? LocationOf(string mapName, string locationName)
    {
        var name = locationName.Replace('’', '\'');
        if (name.Equals("Mt. Silver Cave", StringComparison.OrdinalIgnoreCase)) name = "Mt. Silver";
        bool outdoor = mapName.Length > 0 && mapName[0] is 'R' or 'T' or 'W';
        PokedexLocation? Find(IEnumerable<PokedexLocation> list) => list.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var normal = Find(PokedexAreaRepository.NormalLocations);
        var special = Find(PokedexAreaRepository.SpecialLocations);
        if (outdoor && normal is not null) return (normal.Id, false);
        if (special is not null) return (special.Id, true);
        return normal is null ? null : (normal.Id, false);
    }

    /// <summary>
    /// A lista nova de um período: sai o local conhecido onde a espécie não está, entra o local onde ela está.
    /// Quem aparece só por enxame ou som continua, porque a Pokédex pode já citá-lo por isso.
    /// </summary>
    public static HashSet<uint> Merge(IEnumerable<uint> current, ISet<uint> known, ISet<uint> expected, ISet<uint> keep)
    {
        var result = current.Where(id => !known.Contains(id) || expected.Contains(id) || keep.Contains(id)).ToHashSet();
        result.UnionWith(expected);
        return result;
    }

    public static Result Apply(Project project, PokedexAreaRepository? open = null)
    {
        var dex = open ?? PokedexAreaRepository.Load(project);
        var encounters = EncounterRepository.Load(project);
        if (!dex.Game.IsHgss) throw new NotSupportedException();

        var known = new[] { new HashSet<uint>(), new HashSet<uint>() };
        // [período, normal/especial, local] -> espécies
        var found = new Dictionary<(int Period, bool Special, uint Id), HashSet<int>>();
        var extras = new Dictionary<(bool Special, uint Id), HashSet<int>>();
        foreach (var entry in encounters.Entries.Where(e => e.Hgss is not null))
        {
            var h = entry.Hgss!;
            var always = h.Surf.Concat(h.RockSmash).Concat(h.OldRod).Concat(h.GoodRod).Concat(h.SuperRod).Select(s => (int)s.Species).Where(s => s > 0).ToList();
            var other = h.Swarm.Concat(h.HoennSound).Concat(h.SinnohSound).Where(s => s > 0).Select(s => (int)s).ToList();
            foreach (var map in entry.Maps)
            {
                if (LocationOf(map.InternalName, encounters.LocationName(map.LocationNameIndex)) is not { } place) continue;
                known[place.Special ? 1 : 0].Add(place.Id);
                if (!extras.TryGetValue((place.Special, place.Id), out var more)) extras[(place.Special, place.Id)] = more = [];
                more.UnionWith(other);
                for (int period = 0; period < Periods.Length; period++)
                {
                    if (!found.TryGetValue((period, place.Special, place.Id), out var set)) found[(period, place.Special, place.Id)] = set = [];
                    set.UnionWith(always);
                    set.UnionWith(Periods[period].Grass(h).Where(s => s > 0).Select(s => (int)s));
                }
            }
        }

        // cabeçadas: um arquivo por mapa com árvores
        var treeKnown = new[] { new HashSet<uint>(), new HashSet<uint>() };
        var trees = new Dictionary<(bool Special, uint Id), HashSet<int>>();
        var headers = EventMap.Load(project).Headers;
        var headbutt = GameTables.Load(project).First(t => t.Id == "headbutt");
        var speciesColumn = headbutt.Columns.First(c => c.Key == "species");
        for (int owner = 0; owner < (headbutt.Owners?.Count ?? 0); owner++)
        {
            var mapName = headbutt.Owners![owner].Split(' ', 2)[1];
            if (headers.FirstOrDefault(h => h.InternalName == mapName) is not { } header) continue;
            if (LocationOf(mapName, encounters.LocationName(header.LocationNameIndex)) is not { } place) continue;
            treeKnown[place.Special ? 1 : 0].Add(place.Id);
            if (!trees.TryGetValue((place.Special, place.Id), out var set)) trees[(place.Special, place.Id)] = set = [];
            foreach (var row in headbutt.Rows(owner))
                if (row.Get(speciesColumn) > 0) set.Add(row.Get(speciesColumn));
        }

        int added = 0, removed = 0, changed = 0;
        for (int species = 1; species <= PokedexAreaRepository.SpeciesCount; species++)
        {
            bool touched = false;
            for (int period = 0; period < Periods.Length; period++)
                foreach (var special in new[] { false, true })
                {
                    var block = special ? Periods[period].Special : Periods[period].Normal;
                    var current = dex.GetAreas(species, block);
                    var expected = known[special ? 1 : 0].Where(id => found.TryGetValue((period, special, id), out var set) && set.Contains(species)).ToHashSet();
                    var keep = known[special ? 1 : 0].Where(id => extras[(special, id)].Contains(species)).ToHashSet();
                    var next = Merge(current, known[special ? 1 : 0], expected, keep);
                    int gained = next.Except(current).Count(), lost = current.Except(next).Count();
                    if (gained + lost == 0) continue;
                    added += gained;
                    removed += lost;
                    touched = true;
                    dex.SetAreas(species, block, next);
                }
            foreach (var special in new[] { false, true })
            {
                var block = special ? PokedexAreaBlock.SpecialAll : PokedexAreaBlock.NormalAll;
                var current = dex.GetAreas(species, block);
                var places = treeKnown[special ? 1 : 0];
                var expected = places.Where(id => trees[(special, id)].Contains(species)).ToHashSet();
                var next = Merge(current, places, expected, new HashSet<uint>());
                int gained = next.Except(current).Count(), lost = current.Except(next).Count();
                if (gained + lost == 0) continue;
                added += gained;
                removed += lost;
                touched = true;
                dex.SetAreas(species, block, next);
            }
            if (touched) changed++;
        }
        if (dex.HasChanges) dex.Save();
        return new Result(added, removed, changed);
    }
}
