using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;

namespace PokeHub.Mcp;

/// <summary>Mapa de eventos (HGSS): eventos por mapa, uso de flags/variáveis e faixas livres, só leitura.</summary>
[McpServerToolType]
public static class EventTools
{
    private static EventMap Load(HubContext hub, string project)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        return EventMap.Load(p);
    }

    private static object Describe(EventEntry e, FlagNames names) => new
    {
        map = e.MapName, location = e.LocationName, kind = e.Kind.ToString(), index = e.Index, script = e.ScriptId, standardScript = e.IsStandardScript,
        x = e.X, y = e.Y, hideFlag = e.HideFlag is { } h ? $"0x{h:X}" : null, conditionVar = e.ConditionVar is { } c ? $"0x{c:X}" : null, conditionValue = e.ConditionValue,
        flags = e.Flags.Select(f => new { f.Action, id = $"0x{f.Id:X}", name = names.Flag(f.Id) }).ToList(),
        vars = e.Vars.Select(v => new { v.Action, id = $"0x{v.Id:X}", name = names.Var(v.Id), v.Value }).ToList(),
        actions = e.Actions, messages = e.Messages, instructions = e.InstructionCount,
    };

    [McpServerTool(Name = "list_events"), Description("Eventos (NPCs, triggers, placas, scripts de entrada) de um mapa, com o que cada script faz. Filtre por nome do mapa/local; sem filtro, lista os mapas com contagem.")]
    public static object ListEvents(HubContext hub, string project, [Description("Texto no nome do mapa ou do local (ex.: Route 29, New Bark)")] string? map = null, int limit = 200)
    {
        var em = Load(hub, project);
        if (string.IsNullOrWhiteSpace(map))
            return new { maps = em.Events.GroupBy(e => e.MapId).Select(g => new { id = g.Key, name = g.First().MapName, location = g.First().LocationName, events = g.Count() }).ToList() };
        var hits = em.Events.Where(e => e.MapName.Contains(map, StringComparison.OrdinalIgnoreCase) || e.LocationName.Contains(map, StringComparison.OrdinalIgnoreCase)).Take(limit).ToList();
        return new { total = hits.Count, events = hits.Select(e => Describe(e, em.Names)).ToList() };
    }

    [McpServerTool(Name = "find_events"), Description("Procura eventos pelo uso de uma flag/variável (ex.: 0x2A0) ou por um trecho de fala ou ação.")]
    public static object FindEvents(HubContext hub, string project, [Description("Id em hexadecimal (0x...) ou texto")] string query, int limit = 100)
    {
        var em = Load(hub, project);
        IEnumerable<EventEntry> q;
        if (query.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(query[2..], System.Globalization.NumberStyles.HexNumber, null, out var id))
            q = em.Events.Where(e => e.Flags.Any(f => f.Id == id) || e.HideFlag == id || e.Vars.Any(v => v.Id == id) || e.ConditionVar == id);
        else
            q = em.Events.Where(e => e.Messages.Any(m => m.Contains(query, StringComparison.OrdinalIgnoreCase)) || e.Actions.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase)));
        var hits = q.Take(limit).ToList();
        return new { total = hits.Count, events = hits.Select(e => Describe(e, em.Names)).ToList() };
    }

    [McpServerTool(Name = "free_flags"), Description("Flags e variáveis do save que nenhum script usa e a decomp não identificou: faixas livres para o hack, da maior para a menor.")]
    public static object FreeFlags(HubContext hub, string project)
    {
        var em = Load(hub, project);
        return new
        {
            flags = new { total = em.FreeFlags().Sum(r => r.Count), ranges = em.FreeFlags().OrderByDescending(r => r.Count).Select(r => new { from = $"0x{r.From:X}", to = $"0x{r.To:X}", r.Count }).ToList() },
            vars = new { total = em.FreeVars().Sum(r => r.Count), ranges = em.FreeVars().OrderByDescending(r => r.Count).Select(r => new { from = $"0x{r.From:X}", to = $"0x{r.To:X}", r.Count }).ToList() },
            note = "Flags 0x4000+ e variáveis 0x8000+ são temporárias (não salvas). Reserve números em sequência de uma faixa grande e anote no projeto.",
        };
    }

    [McpServerTool(Name = "flag_usage"), Description("Onde uma flag ou variável (0x...) é usada: eventos, mapas e ação (liga, desliga, confere, compara, esconde NPC).")]
    public static object FlagUsage(HubContext hub, string project, [Description("Id em hexadecimal, ex.: 0x2A0 ou 0x4072")] string id)
    {
        var em = Load(hub, project);
        if (!id.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || !int.TryParse(id[2..], System.Globalization.NumberStyles.HexNumber, null, out var n))
            throw new ArgumentException("Informe o id em hexadecimal, ex.: 0x2A0.");
        var u = n >= em.Names.VarBase ? em.VarUsageOf(n) : em.FlagUsageOf(n);
        var name = n >= em.Names.VarBase ? em.Names.Var(n) : em.Names.Flag(n);
        return new
        {
            id = $"0x{n:X}", name, isVariable = n >= em.Names.VarBase, usedByScripts = u is not null,
            identifiedByDecomp = !em.Names.IsUnidentified(name),
            uses = u?.Uses.Select(x => new { map = x.Event.MapName, location = x.Event.LocationName, @event = x.Event.Title, x.Action }).ToList(),
        };
    }
}
