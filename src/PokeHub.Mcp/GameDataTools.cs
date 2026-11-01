using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;

namespace PokeHub.Mcp;

/// <summary>Leitura dos dados do jogo (Gen 4) pelos mesmos repositórios dos editores do hub.</summary>
[McpServerToolType]
public static class GameDataTools
{
    private static Project Extracted(HubContext hub, string project)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        return p;
    }

    private static string Name(IReadOnlyList<string> table, int index) => index >= 0 && index < table.Count ? table[index] : $"#{index}";

    [McpServerTool(Name = "trainers_by_place"), Description("HG/SS: quantos treinadores cada mapa tem, com nível médio e máximo, do que tem menos para o que tem mais. Mostra só mapas com selvagens ou treinadores.")]
    public static object TrainersByPlace(HubContext hub, string project, [Description("Só até esta quantidade de treinadores")] int maxTrainers = 999) =>
        TrainerCensus.Build(Extracted(hub, project)).Where(r => r.Trainers <= maxTrainers).ToList();

    [McpServerTool(Name = "catch_report"), Description("HG/SS: o que dá para obter no jogo, cruzando encontros, scripts, trocas e evoluções. status: Found, Family (por evolução ou filhote), Known (lista fixa, não confirmada), Missing.")]
    public static object CatchReport(HubContext hub, string project, [Description("Só as que faltam")] bool onlyMissing = false)
    {
        var rows = CatchAudit.Build(Extracted(hub, project));
        return new
        {
            found = rows.Count(r => r.Status == CatchStatus.Found),
            family = rows.Count(r => r.Status == CatchStatus.Family),
            known = rows.Count(r => r.Status == CatchStatus.Known),
            missing = rows.Count(r => r.Status == CatchStatus.Missing),
            rows = rows.Where(r => !onlyMissing || r.Status == CatchStatus.Missing).Select(r => new { r.Species, r.Name, status = r.Status.ToString(), r.Where, r.Note }),
        };
    }

    [McpServerTool(Name = "list_trainers"), Description("Lista treinadores do projeto: id, classe, nome e resumo do time. Filtro opcional por texto no nome ou na classe.")]
    public static object ListTrainers(HubContext hub, string project, [Description("Texto para filtrar (nome ou classe), sem distinção de maiúsculas")] string? filter = null,
        [Description("Máximo de resultados")] int limit = 100)
    {
        var repo = TrainerRepository.Load(Extracted(hub, project));
        var q = repo.Trainers.Select(t => new
        {
            id = t.Id,
            name = repo.NameOf(t),
            @class = repo.ClassOf(t),
            party = string.Join(", ", t.Party.Select(m => $"{Name(repo.Species, m.Species)} Lv{m.Level}")),
        });
        if (!string.IsNullOrWhiteSpace(filter))
            q = q.Where(t => t.name.Contains(filter, StringComparison.OrdinalIgnoreCase) || t.@class.Contains(filter, StringComparison.OrdinalIgnoreCase));
        var list = q.Take(limit).ToList();
        return new { total = repo.Trainers.Count, returned = list.Count, trainers = list };
    }

    [McpServerTool(Name = "get_trainer"), Description("Dados completos de um treinador: classe, itens, IA, tipo de batalha e cada Pokémon do time com golpes e item.")]
    public static object GetTrainer(HubContext hub, string project, [Description("Id do treinador (de list_trainers)")] int id)
    {
        var repo = TrainerRepository.Load(Extracted(hub, project));
        var t = repo.Trainers.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException($"Treinador {id} não existe (0..{repo.Trainers.Count - 1}).");
        return new
        {
            id = t.Id,
            name = repo.NameOf(t),
            @class = repo.ClassOf(t),
            classId = t.Class,
            doubleBattle = t.DoubleBattle,
            aiFlags = t.AiFlags,
            hasMoves = t.HasMoves,
            hasItems = t.HasItems,
            items = t.Items.Select(i => Name(repo.Items, i)).ToList(),
            party = t.Party.Select(m => new
            {
                species = Name(repo.Species, m.Species),
                speciesId = m.Species,
                level = m.Level,
                form = m.Form,
                difficulty = m.Difficulty,
                genderAbility = m.GenderAbility.ToString(),
                item = Name(repo.Items, m.Item),
                moves = t.HasMoves ? m.Moves.Select(mv => Name(repo.Moves, mv)).ToList() : null,
                ballSeal = m.BallSeal,
            }).ToList(),
        };
    }

    [McpServerTool(Name = "list_encounter_areas"), Description("Lista as tabelas de encontros selvagens do projeto com os mapas e locais que as usam.")]
    public static object ListEncounterAreas(HubContext hub, string project)
    {
        var repo = EncounterRepository.Load(Extracted(hub, project));
        return new
        {
            game = repo.Game.Game.ToString(),
            areas = repo.Entries.Select(e => new
            {
                id = e.Id,
                locations = e.Maps.Select(m => repo.LocationName(m.LocationNameIndex)).Distinct().ToList(),
                maps = e.Maps.Select(m => m.InternalName).ToList(),
            }).ToList(),
        };
    }

    [McpServerTool(Name = "get_encounters"), Description("Encontros selvagens de uma tabela: taxas, grama por horário (HGSS) ou slots (DP/Pt), surf, pesca, enxame, sons de GBA.")]
    public static object GetEncounters(HubContext hub, string project, [Description("Id da tabela (de list_encounter_areas)")] int id)
    {
        var repo = EncounterRepository.Load(Extracted(hub, project));
        var e = repo.Entries.FirstOrDefault(x => x.Id == id) ?? throw new ArgumentException($"Tabela {id} não existe.");
        string S(ushort sp) => Name(repo.Species, sp);
        object Slots(EncounterSlot[] slots, int[]? odds = null) => slots.Select((s, i) => new { species = S(s.Species), minLevel = s.MinLevel, maxLevel = s.MaxLevel, chance = odds is not null && i < odds.Length ? odds[i] : (int?)null }).ToList();
        var locations = e.Maps.Select(m => repo.LocationName(m.LocationNameIndex)).Distinct().ToList();

        if (e.Hgss is { } h)
            return new
            {
                id = e.Id, locations,
                rates = new { walking = h.WalkingRate, surf = h.SurfRate, rockSmash = h.RockSmashRate, oldRod = h.OldRodRate, goodRod = h.GoodRodRate, superRod = h.SuperRodRate },
                grass = Enumerable.Range(0, 12).Select(i => new { slot = i, chance = EncounterOdds.Walking[i], level = h.WalkingLevels[i], morning = S(h.Morning[i]), day = S(h.Day[i]), night = S(h.Night[i]) }).ToList(),
                hoennSound = h.HoennSound.Select(S).ToList(),
                sinnohSound = h.SinnohSound.Select(S).ToList(),
                surf = Slots(h.Surf, EncounterOdds.Surf),
                rockSmash = Slots(h.RockSmash),
                oldRod = Slots(h.OldRod),
                goodRod = Slots(h.GoodRod),
                superRod = Slots(h.SuperRod),
                swarm = h.Swarm.Select(S).ToList(),
            };
        if (e.Dppt is { } d)
            return new
            {
                id = e.Id, locations,
                rates = new { walking = d.WalkingRate, surf = d.SurfRate, oldRod = d.OldRodRate, goodRod = d.GoodRodRate, superRod = d.SuperRodRate },
                grass = Slots(d.Walking, EncounterOdds.Walking),
                swarm = d.Swarm.Select(S).ToList(), day = d.Day.Select(S).ToList(), night = d.Night.Select(S).ToList(), radar = d.Radar.Select(S).ToList(),
                ruby = d.Ruby.Select(S).ToList(), sapphire = d.Sapphire.Select(S).ToList(), emerald = d.Emerald.Select(S).ToList(), fireRed = d.FireRed.Select(S).ToList(), leafGreen = d.LeafGreen.Select(S).ToList(),
                surf = Slots(d.Surf, EncounterOdds.Surf), oldRod = Slots(d.OldRod), goodRod = Slots(d.GoodRod), superRod = Slots(d.SuperRod),
            };
        throw new InvalidOperationException("Tabela sem dados decodificados.");
    }
}
