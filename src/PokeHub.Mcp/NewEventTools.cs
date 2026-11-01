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

    [McpServerTool(Name = "create_trade_npc"), Description("Cria um NPC que troca Pokémon (HG/SS e Platinum), usando uma vaga de troca que nenhum script usa; passadas as originais, cria vagas novas no arquivo de trocas. Falha se não houver vaga livre. Salva versão antes e depois.")]
    public static async Task<object> CreateTradeNpc(HubContext hub, string project, int mapId, int x, int z, int sprite, int askSpecies, int giveSpecies,
        string askText, string thanksText, string wrongText, string declineText, string? nickname = null, int facing = 1, [Description("Nível mínimo do Pokémon entregue; 0 aceita qualquer. Só HG/SS")] int minLevel = 0, string? tooLowText = null,
        [Description("fixed, look, wander, updown ou leftright")] string movement = "fixed", int range = 1)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var game = Gen4Game.For(p);
        var behavior = new TradeBehavior(askSpecies, giveSpecies, nickname, Wrap(game, askText), Wrap(game, thanksText), Wrap(game, wrongText), Wrap(game, declineText), minLevel, tooLowText is null ? null : Wrap(game, tooLowText));
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de criar troca no mapa {mapId}", log);
        var result = NewEventService.Create(p, EventMap.Load(p), new NewNpc(mapId, x, z, sprite, facing, behavior, Movement(movement), range));
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Troca criada no mapa {mapId}", log);
        return new { result.ObjectIndex, result.ScriptId, result.Flag, result.Warnings };
    }

    private static NpcCondition? Condition(Gen4Game game, string? text, bool afterElite, int minBadges, int? badge, int? afterFlag, int maxPartyLevel = 0) =>
        !afterElite && minBadges == 0 && badge is null && afterFlag is null && maxPartyLevel == 0 ? null
        : new NpcCondition(Wrap(game, text ?? throw new ArgumentException("Com condição, informe conditionText.")), afterElite, minBadges, badge, afterFlag, maxPartyLevel);

    private static async Task<object> CreateAsync(HubContext hub, string project, string what, int mapId, Func<Gen4Game, NewNpc> request)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de criar {what} no mapa {mapId}", log);
        var result = NewEventService.Create(p, EventMap.Load(p), request(Gen4Game.For(p)));
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"{what} criado no mapa {mapId}", log);
        return new { result.ObjectIndex, result.ScriptId, result.Flag, result.Warnings };
    }

    [McpServerTool(Name = "configure_post_elite"), Description("HG/SS: o que acontece ao vencer a Elite. resetTrainers libera os treinadores para lutarem de novo, com o mesmo time. scaleLevels multiplica o nível dos times em batalha depois do fim do jogo (só SoulSilver US em ds-rom; limite 100; floor é o nível mínimo). openIlexShrine faz o santuário de Ilex aceitar qualquer Celebi na frente do time, abrindo o evento do Giovanni. kantoBadges dá aos treinadores de Kanto o nível pelas insígnias de Kanto, com 9 valores em kantoTargets (0 a 8 insígnias), o nível do mais forte do líder, e kantoRouteGap níveis a menos nos de rota; eles ficam fora do multiplicador. Listas de fora aceitam 1, 2, 701-705. Não gera ROM.")]
    public static async Task<object> ConfigurePostElite(HubContext hub, string project, bool resetTrainers = false, string resetSkip = "", bool scaleLevels = false, int percent = 130, int floor = 0, string levelSkip = "", bool openIlexShrine = false, bool kantoBadges = false, string? kantoTargets = null, int kantoRouteGap = PostEliteService.DefaultRouteGap)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var resetIds = PostEliteService.ParseIds(resetSkip);
        var levelIds = PostEliteService.ParseIds(levelSkip);
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, "Antes de configurar o que vem depois da Elite", log);
        int count = PostEliteService.ApplyReset(p, resetTrainers, resetIds);
        PostEliteService.ApplyIlex(p, openIlexShrine);
        var targets = kantoTargets is null ? PostEliteService.DefaultKantoTargets : PostEliteService.ParseTargets(kantoTargets);
        if (scaleLevels || kantoBadges || PostEliteService.CanScaleLevels(p))
            await new PostEliteService(hub.Paths).ApplyLevelsAsync(p, scaleLevels, percent, floor, levelIds, new Progress<string>(_ => { }), default, kantoBadges, targets, kantoRouteGap);
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, "Depois da Elite configurado", log);
        return new { trainersReset = count, levelsScaled = scaleLevels, percent, floor };
    }

    [McpServerTool(Name = "create_ground_item"), Description("Coloca um item para achar no mapa (HG/SS): numa Poké Bola, ou escondido num tile com hidden=true, que entrega ao apertar A virado para ele. Serve para fósseis, por exemplo. A posição do escondido precisa ter por onde o jogador chegar.")]
    public static Task<object> CreateGroundItem(HubContext hub, string project, int mapId, int x, int z, int item, int quantity = 1, bool hidden = false) =>
        CreateAsync(hub, project, "item no mapa", mapId, _ => new NewNpc(mapId, x, z, 0, 0, new GroundItemBehavior(item, quantity, hidden)));

    [McpServerTool(Name = "create_guide_npc"), Description("NPC guia (HG/SS): percorre as etapas em ordem e diz a fala da primeira cuja flag esteja desligada; com todas feitas, diz finalText. flags e texts têm o mesmo tamanho, até 8. Use as flags de list_created_npcs ou devolvidas ao criar missões.")]
    public static Task<object> CreateGuideNpc(HubContext hub, string project, int mapId, int x, int z, int sprite, int[] flags, string[] texts, string finalText, int facing = 1) =>
        flags.Length != texts.Length ? throw new ArgumentException("flags e texts precisam ter o mesmo tamanho.") :
        CreateAsync(hub, project, "guia", mapId, game => new NewNpc(mapId, x, z, sprite, facing, new GuideBehavior(flags.Select((flag, i) => new GuideStep(flag, Wrap(game, texts[i]))).ToList(), Wrap(game, finalText))));

    [McpServerTool(Name = "create_quest_npc"), Description("Cria uma missão (HG/SS): pede um item (askItem) ou mostrar um Pokémon no time (askSpecies) e dá uma recompensa uma vez só (rewardItem, rewardSpecies ou rewardMoney). Aceita condições: depois da Elite, insígnias e flag de outra missão. Salva versão antes e depois.")]
    public static Task<object> CreateQuestNpc(HubContext hub, string project, int mapId, int x, int z, int sprite, string askText, string notYetText, string rewardText, string doneText, string fullText,
        int? askItem = null, int askQuantity = 1, bool takeItem = true, int? askSpecies = null, int? rewardItem = null, int rewardQuantity = 1, int? rewardSpecies = null, int rewardLevel = 10, int rewardMoney = 0, int facing = 1,
        string? conditionText = null, bool afterElite = false, int minBadges = 0, int? badge = null, int? afterFlag = null, int maxPartyLevel = 0) =>
        CreateAsync(hub, project, "missão", mapId, game => new NewNpc(mapId, x, z, sprite, facing,
            new QuestBehavior(askItem, askQuantity, takeItem, askSpecies, rewardItem, rewardQuantity, rewardSpecies, rewardLevel, rewardMoney, Wrap(game, askText), Wrap(game, notYetText), Wrap(game, rewardText), Wrap(game, doneText), Wrap(game, fullText)),
            Condition: Condition(game, conditionText, afterElite, minBadges, badge, afterFlag, maxPartyLevel)));

    [McpServerTool(Name = "create_seller_npc"), Description("Cria um NPC que vende um Pokémon por dinheiro (HG/SS). Mostra o saldo, pergunta sim ou não e confere dinheiro e espaço no time antes de cobrar. Com once=true, vende uma vez só. Salva versão antes e depois.")]
    public static Task<object> CreateSellerNpc(HubContext hub, string project, int mapId, int x, int z, int sprite, int species, int level, int price, string offerText, string thanksText, string noMoneyText, string declineText, string fullText, string soldOutText,
        bool once = true, int facing = 1, string? conditionText = null, bool afterElite = false, int minBadges = 0, int? badge = null, int? afterFlag = null, int maxPartyLevel = 0) =>
        CreateAsync(hub, project, "vendedor", mapId, game => new NewNpc(mapId, x, z, sprite, facing,
            new SellBehavior(species, level, price, once, Wrap(game, offerText), Wrap(game, thanksText), Wrap(game, noMoneyText), Wrap(game, declineText), Wrap(game, fullText), Wrap(game, soldOutText)),
            Condition: Condition(game, conditionText, afterElite, minBadges, badge, afterFlag, maxPartyLevel)));

    [McpServerTool(Name = "create_trainer_npc"), Description("Coloca um treinador num mapa (HG/SS). Com sight=0 ele não chama: a batalha começa ao falar, com falas próprias, flag própria, opção de sumir e rebatalha (rematch: none, always, elite). Com sight de 1 a 15, chama de longe e usa as falas e a flag de vencido do registro. Salva versão antes e depois.")]
    public static Task<object> CreateTrainerNpc(HubContext hub, string project, int mapId, int x, int z, int sprite, int trainer, string challengeText = "", string beatenText = "", string afterText = "",
        int sight = 0, bool vanish = false, string rematch = "none", int? rematchTrainer = null, string? rematchText = null, int facing = 1,
        string? conditionText = null, bool afterElite = false, int minBadges = 0, int? badge = null, int? afterFlag = null, int maxPartyLevel = 0) =>
        CreateAsync(hub, project, "treinador", mapId, game => new NewNpc(mapId, x, z, sprite, facing,
            new TrainerBehavior(trainer, Wrap(game, challengeText), Wrap(game, beatenText), Wrap(game, afterText), sight, vanish,
                rematch switch { "none" => TrainerRematch.None, "always" => TrainerRematch.Always, "elite" => TrainerRematch.AfterElite, _ => throw new ArgumentException("rematch: none, always ou elite.") },
                rematchTrainer, string.IsNullOrWhiteSpace(rematchText) ? null : Wrap(game, rematchText)),
            Condition: Condition(game, conditionText, afterElite, minBadges, badge, afterFlag, maxPartyLevel)));

    [McpServerTool(Name = "copy_trainer"), Description("Cria um treinador novo, copiado de outro, com outro nome, e devolve o número dele para usar em create_trainer_npc. Ele só serve em batalha iniciada por script (sight=0): as falas de quem chama de longe ficam noutra tabela. Salva versão antes e depois.")]
    public static async Task<object> CopyTrainer(HubContext hub, string project, int source, string name)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        var repo = TrainerRepository.Load(p);
        var from = repo.Trainers.FirstOrDefault(t => t.Id == source) ?? throw new ArgumentException($"Treinador {source} não existe.");
        var log = new LogCollector();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Antes de copiar o treinador {source}", log);
        var copy = repo.AddCopy(from, name);
        repo.Save();
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, $"Treinador {copy.Id} copiado de {source}", log);
        return new { trainer = copy.Id, name };
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
