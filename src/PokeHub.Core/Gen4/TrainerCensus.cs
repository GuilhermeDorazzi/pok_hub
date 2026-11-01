using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>Um mapa com selvagens ou treinadores: quantos treinadores tem e em que nível.</summary>
public sealed record TrainerCensusRow(string Place, string Map, string Region, int Trainers, int AverageLevel, int TopLevel);

/// <summary>Só leitura: mostra onde faltam treinadores, contando os de visão nos objetos e as batalhas nos scripts de cada mapa.</summary>
public static class TrainerCensus
{
    private const int VarBase = 0x4000, StandardTrainer = 3000, DoubleTrainer = 5000, LastTrainerScript = 7000;

    public static IReadOnlyList<TrainerCensusRow> Build(Project project)
    {
        var game = Gen4Game.For(project);
        if (!game.IsHgss) throw new NotSupportedException(Loc.T("core.census.hgssOnly"));
        var repo = TrainerRepository.Load(project);
        var locations = EncounterRepository.Load(project);
        var events = Narc.Read(File.ReadAllBytes(game.EventsPath));
        var archive = ScriptArchive.Load(project);
        var rows = new List<TrainerCensusRow>();
        foreach (var h in MapHeaders.Load(game))
        {
            var ids = new HashSet<int>();
            if (h.EventsBank >= 0 && h.EventsBank < events.Files.Count)
                foreach (var o in new EventFile(events.Files[h.EventsBank]).Objects)
                {
                    if (o.ScriptId >= DoubleTrainer && o.ScriptId < LastTrainerScript) ids.Add(o.ScriptId - DoubleTrainer + 1);
                    else if (o.ScriptId >= StandardTrainer && o.ScriptId < DoubleTrainer) ids.Add(o.ScriptId - StandardTrainer + 1);
                }
            if (h.ScriptsBank >= 0)
                foreach (var i in archive.Open(h.ScriptsBank).Instructions())
                    if (i.Name == "TrainerBattle" && i.Args[0].Value is > 0 and < VarBase) ids.Add((int)i.Args[0].Value);
            ids.RemoveWhere(id => id < 1 || id >= repo.Trainers.Count);
            if (ids.Count == 0 && h.EncounterFile is null) continue;
            var levels = ids.SelectMany(id => repo.Trainers[id].Party).Select(m => (int)m.Level).ToList();
            rows.Add(new TrainerCensusRow(locations.LocationName(h.LocationNameIndex), h.InternalName, Loc.T(h.Region == 1 ? "batch.region.kanto" : "batch.region.johto"), ids.Count,
                levels.Count == 0 ? 0 : (int)Math.Round(levels.Average()), levels.Count == 0 ? 0 : levels.Max()));
        }
        return rows.OrderBy(r => r.Trainers).ThenBy(r => r.Region).ThenBy(r => r.Map).ToList();
    }
}
