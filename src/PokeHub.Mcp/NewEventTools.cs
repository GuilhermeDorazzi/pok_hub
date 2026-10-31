using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;

namespace PokeHub.Mcp;

/// <summary>Cria NPCs prontos, como o card "Novo evento".</summary>
[McpServerToolType]
public static class NewEventTools
{
    private static string Wrap(Gen4Game game, string text)
    {
        var font = Gen4Font.Load(game);
        return TextPreview.Wrap(text.Trim(), s => font is null ? s.Length * 6 : TextPreview.Render(Gen4TextArchive.Encode(s), font).Lines.Max(l => l.Width));
    }

    [McpServerTool(Name = "create_pokemon_encounter"), Description("Coloca um Pokémon parado num mapa (HG/SS): ao interagir toca o grito e começa a batalha. Com vanish=true, some depois da batalha, a não ser que o jogador perca. Salva versão antes e depois.")]
    public static async Task<object> CreatePokemonEncounter(HubContext hub, string project, int mapId, int x, int z, int species, int level, bool vanish = true, string? text = null, int facing = 1,
        [Description("fixed, look, wander, updown ou leftright")] string movement = "fixed", int range = 1)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var game = Gen4Game.For(p);
        var behavior = new PokemonBehavior(species, level, vanish, string.IsNullOrWhiteSpace(text) ? null : Wrap(game, text));
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de criar Pokémon no mapa {mapId}", log);
        var result = NewEventService.Create(p, EventMap.Load(p), new NewNpc(mapId, x, z, 0, facing, behavior, Movement(movement), range));
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Pokémon criado no mapa {mapId}", log);
        return new { result.ObjectIndex, result.ScriptId, result.Flag, result.Warnings };
    }

    [McpServerTool(Name = "list_created_npcs"), Description("Lista os NPCs criados pelo hub num projeto, com mapa, número do objeto, modelo, posição e falas.")]
    public static object ListCreatedNpcs(HubContext hub, string project)
    {
        var p = hub.OpenProject(project);
        return NewEventLedger.Load(p).Npcs.Select(n => new { n.MapId, n.ObjectId, n.ScriptId, n.Kind, n.X, n.Z, n.SpriteId, movement = n.Movement.ToString(), n.Flag, n.TradeSlot, n.Texts }).ToList();
    }

    private static CreatedNpc Find(Project p, int mapId, int objectId) =>
        NewEventLedger.Load(p).Npcs.FirstOrDefault(n => n.MapId == mapId && n.ObjectId == objectId) ?? throw new ArgumentException($"O NPC {objectId} do mapa {mapId} não foi criado pelo hub.");

    [McpServerTool(Name = "move_created_npc"), Description("Muda a posição de um NPC criado pelo hub. Salva versão antes e depois.")]
    public static async Task<object> MoveCreatedNpc(HubContext hub, string project, int mapId, int objectId, int x, int z)
    {
        var p = hub.OpenProject(project);
        var npc = Find(p, mapId, objectId);
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de mover o NPC {objectId} do mapa {mapId}", log);
        var result = NewEventService.Update(p, EventMap.Load(p), npc, npc.ToNewNpc() with { X = x, Z = z });
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"NPC {objectId} do mapa {mapId} movido", log);
        return new { objectId, x, z, result.Warnings };
    }

    [McpServerTool(Name = "remove_created_npc"), Description("Remove um NPC criado pelo hub: tira do mapa e desativa o script. Salva versão antes e depois.")]
    public static async Task<object> RemoveCreatedNpc(HubContext hub, string project, int mapId, int objectId)
    {
        var p = hub.OpenProject(project);
        var npc = Find(p, mapId, objectId);
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de remover o NPC {objectId} do mapa {mapId}", log);
        NewEventService.Remove(p, EventMap.Load(p), npc);
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"NPC {objectId} do mapa {mapId} removido", log);
        return new { removed = objectId, mapId };
    }

    [McpServerTool(Name = "create_trade_npc"), Description("Cria um NPC que troca Pokémon (HG/SS), usando uma vaga de troca que nenhum script usa. Falha se não houver vaga livre. Salva versão antes e depois.")]
    public static async Task<object> CreateTradeNpc(HubContext hub, string project, int mapId, int x, int z, int sprite, int askSpecies, int giveSpecies,
        string askText, string thanksText, string wrongText, string declineText, string? nickname = null, int facing = 1,
        [Description("fixed, look, wander, updown ou leftright")] string movement = "fixed", int range = 1)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var game = Gen4Game.For(p);
        var behavior = new TradeBehavior(askSpecies, giveSpecies, nickname, Wrap(game, askText), Wrap(game, thanksText), Wrap(game, wrongText), Wrap(game, declineText));
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de criar troca no mapa {mapId}", log);
        var result = NewEventService.Create(p, EventMap.Load(p), new NewNpc(mapId, x, z, sprite, facing, behavior, Movement(movement), range));
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Troca criada no mapa {mapId}", log);
        return new { result.ObjectIndex, result.ScriptId, result.Flag, result.Warnings };
    }

    private static NpcMovement Movement(string name) => name switch
    {
        "look" => NpcMovement.LookAround,
        "wander" => NpcMovement.Wander,
        "updown" => NpcMovement.UpDown,
        "leftright" => NpcMovement.LeftRight,
        "fixed" => NpcMovement.Fixed,
        _ => throw new ArgumentException("Movimento: fixed, look, wander, updown ou leftright."),
    };

    [McpServerTool(Name = "create_npc"), Description("Cria um NPC num mapa (HG/SS): com item=0 ele só fala 'text'; com item, fala 'text', dá o item uma vez e depois repete 'after'. Quebra as linhas para caber na caixa. Salva versão antes e depois.")]
    public static async Task<object> CreateNpc(HubContext hub, string project,
        [Description("Id do mapa, de list_events")] int mapId,
        [Description("Tile na matriz, como nas posições de list_events")] int x, int z,
        [Description("Sprite, como o SpriteId dos NPCs")] int sprite,
        string text, [Description("0 cima, 1 baixo, 2 esquerda, 3 direita")] int facing = 1, int item = 0, int quantity = 1, string? after = null,
        [Description("fixed, look, wander, updown ou leftright")] string movement = "fixed", int range = 1)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var game = Gen4Game.For(p);
        NpcBehavior behavior = item == 0
            ? new TalkBehavior(Wrap(game, text))
            : new GiveItemBehavior(item, quantity, Wrap(game, text), Wrap(game, after ?? throw new ArgumentException("Com item, informe também 'after'.")));
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de criar NPC no mapa {mapId}", log);
        var result = NewEventService.Create(p, EventMap.Load(p), new NewNpc(mapId, x, z, sprite, facing, behavior, Movement(movement), range));
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"NPC criado no mapa {mapId}", log);
        return new { result.ObjectIndex, result.ScriptId, result.Flag, result.Warnings };
    }
}
