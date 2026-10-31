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
    public static async Task<object> CreatePokemonEncounter(HubContext hub, string project, int mapId, int x, int z, int species, int level, bool vanish = true, string? text = null, int facing = 1)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var game = Gen4Game.For(p);
        var behavior = new PokemonBehavior(species, level, vanish, string.IsNullOrWhiteSpace(text) ? null : Wrap(game, text));
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de criar Pokémon no mapa {mapId}", log);
        var result = NewEventService.Create(p, EventMap.Load(p), new NewNpc(mapId, x, z, 0, facing, behavior));
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Pokémon criado no mapa {mapId}", log);
        return new { result.ObjectIndex, result.ScriptId, result.Flag, result.Warnings };
    }

    [McpServerTool(Name = "create_npc"), Description("Cria um NPC num mapa (HG/SS): com item=0 ele só fala 'text'; com item, fala 'text', dá o item uma vez e depois repete 'after'. Quebra as linhas para caber na caixa. Salva versão antes e depois.")]
    public static async Task<object> CreateNpc(HubContext hub, string project,
        [Description("Id do mapa, de list_events")] int mapId,
        [Description("Tile na matriz, como nas posições de list_events")] int x, int z,
        [Description("Sprite, como o SpriteId dos NPCs")] int sprite,
        string text, [Description("0 cima, 1 baixo, 2 esquerda, 3 direita")] int facing = 1, int item = 0, int quantity = 1, string? after = null)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var game = Gen4Game.For(p);
        NpcBehavior behavior = item == 0
            ? new TalkBehavior(Wrap(game, text))
            : new GiveItemBehavior(item, quantity, Wrap(game, text), Wrap(game, after ?? throw new ArgumentException("Com item, informe também 'after'.")));
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de criar NPC no mapa {mapId}", log);
        var result = NewEventService.Create(p, EventMap.Load(p), new NewNpc(mapId, x, z, sprite, facing, behavior));
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"NPC criado no mapa {mapId}", log);
        return new { result.ObjectIndex, result.ScriptId, result.Flag, result.Warnings };
    }
}
