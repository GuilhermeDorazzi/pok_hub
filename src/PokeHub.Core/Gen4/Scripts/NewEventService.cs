using System.Reflection;
using System.Text.Json;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>O que o NPC novo faz ao falar com ele.</summary>
public abstract record NpcBehavior;
public sealed record TalkBehavior(string Text) : NpcBehavior;
/// <summary>Dá o item uma vez só: depois repete apenas a fala final.</summary>
public sealed record GiveItemBehavior(int Item, int Quantity, string BeforeText, string AfterText) : NpcBehavior;

/// <summary>Pokémon parado no mapa: grito e batalha. Com Vanish, some depois da batalha, a não ser que o jogador perca. Texto é opcional.</summary>
public sealed record PokemonBehavior(int Species, int Level, bool Vanish, string? Text) : NpcBehavior;

/// <summary>Troca: o NPC pede uma espécie e dá outra, usando uma vaga livre da tabela de trocas do jogo.</summary>
public sealed record TradeBehavior(int AskSpecies, int GiveSpecies, string? Nickname, string AskText, string ThanksText, string WrongText, string DeclineText) : NpcBehavior;

/// <summary>Como o NPC se move. Os códigos são MOVEMENT_TYPE nas decomps; andar usa um alcance em tiles a partir da posição dele.</summary>
public enum NpcMovement { Fixed, LookAround, Wander, UpDown, LeftRight }

/// <param name="X">Tile na matriz inteira, como guardado no arquivo de eventos.</param>
/// <param name="Facing">0 cima, 1 baixo, 2 esquerda, 3 direita.</param>
public sealed record NewNpc(int MapId, int X, int Z, int SpriteId, int Facing, NpcBehavior Behavior, NpcMovement Movement = NpcMovement.Fixed, int Range = 1);

public sealed record NewNpcResult(int ObjectIndex, int ScriptId, int? Flag, IReadOnlyList<string> Warnings);

/// <summary>Monta um script novo: comandos por nome, com rótulos para os saltos dentro dele.</summary>
public sealed class ScriptBuilder(ScriptCommandTable table, IReadOnlyDictionary<string, string>? rename = null)
{
    private readonly MemoryStream _code = new();
    private readonly Dictionary<string, int> _labels = [];
    private readonly List<(int At, string Label)> _jumps = [];

    public ScriptBuilder Emit(string command, params object[] args)
    {
        if (rename is not null && rename.TryGetValue(command, out var renamed)) command = renamed;
        int opcode = -1;
        for (int i = 0; i < table.Commands.Count && opcode < 0; i++)
            if (table.Commands[i].Name == command) opcode = i;
        if (opcode < 0) throw new ArgumentException(Loc.T("core.script.noCommand", command));
        var spec = table.Commands[opcode].Args;
        if (spec.Count != args.Length) throw new ArgumentException(Loc.T("core.script.argCount", command, spec.Count));
        _code.Write(BitConverter.GetBytes((ushort)opcode));
        for (int i = 0; i < args.Length; i++)
        {
            int size = ScriptCommandTable.ArgSize(spec[i]);
            if (args[i] is string label)
            {
                _jumps.Add(((int)_code.Length, label));
                _code.Write(new byte[size]);
            }
            else
            {
                long value = Convert.ToInt64(args[i]);
                // mensagem é um byte só: um banco com mais de 256 falas não cabe, e gravar cortado chamaria a fala errada
                if (size < 8 && (value < 0 || value >= 1L << (size * 8))) throw new ArgumentException(Loc.T("core.script.argRange", i + 1, (1L << (size * 8)) - 1));
                _code.Write(BitConverter.GetBytes(value).AsSpan(0, size));
            }
        }
        return this;
    }

    /// <summary>Posição do próximo comando dentro do script.</summary>
    public int Position => (int)_code.Length;

    public ScriptBuilder Label(string name)
    {
        _labels[name] = (int)_code.Length;
        return this;
    }

    public byte[] Build()
    {
        var bytes = _code.ToArray();
        foreach (var (at, label) in _jumps)
            BitConverter.TryWriteBytes(bytes.AsSpan(at), _labels[label] - (at + 4));
        return bytes;
    }
}

/// <summary>
/// Cria um NPC completo: mensagens no banco do mapa, script novo no arquivo do mapa, flag livre quando precisa e o objeto no arquivo de eventos.
/// O script segue o formato dos NPCs do próprio jogo, em HG/SS e Platinum: os comandos fazem o mesmo, com outros nomes nas tabelas de cada decomp.
/// </summary>
public static class NewEventService
{
    /// <summary>O mapa mais cheio do SoulSilver tem 51 NPCs. Passar disso é terreno não testado.</summary>
    public const int NpcWarningCount = 51;
    /// <summary>Banco com apelidos (vaga) e nomes de treinador (vaga + total original de vagas) das trocas.</summary>
    public const int TradeNamesBank = 200, PlatinumTradeNamesBank = 370;
    private const int TradeSlots = 13, PlatinumTradeSlots = 4, MaxRange = 8;
    /// <summary>O comando de script guarda a vaga em um byte.</summary>
    private const int MaxTradeSlot = 255;
    private const int TradeGiveOffset = 0, TradeAbilityOffset = 28, TradeHeldItemOffset = 60, TradeAskOffset = 0x4C;
    private const int PartyCancelled = 255, ConditionNotEqual = 5;
    private const int SelectSoundCommandSeen = 609, SpecialVar0 = 0x8004, SpecialVar1 = 0x8005, ResultVar = 0x800C;
    private const int GiveItemStd = 2033, BagFullStd = 2009, PlatinumGiveItem = 2016, PlatinumBagFull = 2017, ConditionEqual = 1, SelectSound = 1500, MaxSpecies = 493;
    /// <summary>Flags de treinador começam aqui e vão até o último treinador; as do sistema começam em 0x960.</summary>
    private const int TrainerFlagBase = 0x550, SystemFlagBase = 0x960;

    public static bool Supports(Gen4Game game) => game.IsHgss || game.IsPlatinum;

    /// <summary>Nome em HG/SS para o nome em Platinum.</summary>
    private static readonly Dictionary<string, string> PlatinumNames = new()
    {
        ["NPCMsg"] = "Message", ["CloseMsg"] = "CloseMessage", ["WaitFade"] = "WaitFadeScreen", ["SetVar"] = "SetVarFromValue", ["CopyVar"] = "SetVarFromVar",
        ["HasSpaceForItem"] = "CanFitItem", ["CallStd"] = "CallCommonScript", ["CheckBattleWon"] = "CheckWonBattle", ["WhiteOut"] = "BlackOutFromBattle",
        ["HidePerson"] = "RemoveObject", ["ScrCmd_566"] = "OpenPartyMenuForTrade", ["GetPartySelection"] = "GetSelectedPartySlot", ["RestoreOverworld"] = "ReturnToField",
        ["PartyCountNotEgg"] = "CountPartyNonEggs", ["LoadNPCTrade"] = "InitNPCTrade", ["NPCTradeGetReqSpecies"] = "GetNPCTradeRequestedSpecies", ["NPCTradeExec"] = "StartNPCTrade", ["NPCTradeEnd"] = "FinishNPCTrade",
    };

    private static bool IsPlatinum(ScriptCommandTable table) => ReferenceEquals(table, ScriptCommandTable.Platinum);
    private static ScriptBuilder NewBuilder(ScriptCommandTable table) => new(table, IsPlatinum(table) ? PlatinumNames : null);

    /// <summary>A flag livre mais alta da faixa segura: entre as de treinador e as do sistema, sem uso em script nenhum.</summary>
    public static int? PickFreeFlag(EventMap map, int trainerCount, IReadOnlySet<int>? taken = null)
    {
        int lowest = TrainerFlagBase + trainerCount;
        foreach (var range in map.FreeFlags().OrderByDescending(r => r.To))
            for (int flag = Math.Min(range.To, SystemFlagBase - 1); flag >= Math.Max(range.From, lowest); flag--)
                if (taken is null || !taken.Contains(flag)) return flag;
        return null;
    }

    /// <summary>Altura do NPC: a do objeto mais próximo, porque o terreno 3D não está lido. Sem vizinho, usa uma placa ou 0.</summary>
    public static int HeightFor(EventFile events, int x, int z)
    {
        // quem se move está no chão de verdade; placas e objetos parados às vezes têm altura diferente, como em Twinleaf
        var moving = events.Objects.Where(o => o.Movement != 0).ToList();
        var candidates = moving.Count > 0 ? moving : events.Objects;
        if (candidates.Count > 0)
            return candidates.OrderBy(o => Math.Abs(o.X - x) + Math.Abs(o.Z - z)).First().Y;
        return events.Bg.Count > 0 ? events.Bg.OrderBy(b => Math.Abs(b.X - x) + Math.Abs(b.Z - z)).First().Y : 0;
    }

    private static Dictionary<int, int> LoadSprites(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} embutido não encontrado.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("sprites").EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => p.Value.GetInt32());
    }

    private static readonly Lazy<Dictionary<int, int>> Followers = new(() => LoadSprites("follower-sprites-hgss.json"));
    private static readonly Lazy<Dictionary<int, int>> PlatinumSprites = new(() => LoadSprites("field-sprites-platinum.json"));

    /// <summary>Sprite de campo da espécie, ou null. HG/SS tem o de todas, por causa do Pokémon que segue o jogador; o Platinum só de algumas.</summary>
    public static int? SpriteForSpecies(int species, bool platinum = false) =>
        (platinum ? PlatinumSprites : Followers).Value.TryGetValue(species, out var sprite) ? sprite : null;

    public static int TradeNamesBankOf(Gen4Game game) => game.IsPlatinum ? PlatinumTradeNamesBank : TradeNamesBank;
    public static string TradesPathOf(Gen4Game game) => Path.Combine(game.FilesRoot, game.IsPlatinum ? PlatinumTradesRelativePath : TradesRelativePath);

    /// <summary>
    /// Vagas além das originais. O jogo não confere o limite (o GF_ASSERT não existe na versão final) e lê a vaga direto do arquivo de trocas.
    /// O que pede cuidado são os nomes: apelido em n e treinador em n + total. Por isso as vagas vêm em blocos alternados, a partir de 2 x total,
    /// para que o apelido de uma nunca caia no treinador de outra.
    /// </summary>
    public static IEnumerable<int> ExtraTradeSlots(int originalSlots)
    {
        for (int slot = originalSlots * 2; slot <= MaxTradeSlot; slot++)
            if (slot / originalSlots % 2 == 0) yield return slot;
    }

    /// <summary>Vagas de troca que nenhum script carrega: primeiro as originais sem uso, depois as novas. Usar uma delas não afeta nenhum NPC do jogo.</summary>
    public static List<int> FreeTradeSlots(Gen4Game game)
    {
        var used = new HashSet<long>();
        var table = game.IsPlatinum ? ScriptCommandTable.Platinum : ScriptCommandTable.Hgss;
        string load = game.IsPlatinum ? PlatinumNames["LoadNPCTrade"] : "LoadNPCTrade";
        foreach (var file in Narc.Read(File.ReadAllBytes(game.ScriptsPath)).Files)
        {
            var script = new ScriptFile(file, table);
            for (int id = 1; id <= script.EntryOffsets.Count; id++)
                foreach (var i in script.Routine(id)!.Instructions)
                    if (i.Name == load) used.Add(i.Args[0].Value);
        }
        int original = game.IsPlatinum ? PlatinumTradeSlots : TradeSlots;
        return Enumerable.Range(0, original).Concat(ExtraTradeSlots(original)).Where(slot => !used.Contains(slot)).ToList();
    }

    private static void BuildTrade(ScriptBuilder s, int slot, int flag, IReadOnlyList<int> m, bool platinum)
    {
        s.Emit("PlaySE", SelectSound).Emit("LockAll").Emit("FacePlayer")
            .Emit("CheckFlag", flag).Emit("GoToIf", ConditionEqual, "done")
            .Emit("NPCMsg", m[0]);
        if (platinum) s.Emit("ShowYesNoMenu", ResultVar);
        else s.Emit("TouchscreenMenuHide").Emit("GetMenuChoice", ResultVar).Emit("TouchscreenMenuShow");
        s.Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "yes")
            .Emit("GoTo", "decline")
            .Label("yes")
            .Emit("CloseMsg")
            .Emit("FadeScreen", 6, 1, 0, 0).Emit("WaitFade")
            .Emit("ScrCmd_566").Emit("GetPartySelection", ResultVar).Emit("RestoreOverworld")
            .Emit("FadeScreen", 6, 1, 1, 0).Emit("WaitFade")
            .Emit("CompareVarToValue", ResultVar, PartyCancelled).Emit("GoToIf", ConditionEqual, "decline")
            .Emit("LoadNPCTrade", slot)
            .Emit("CopyVar", SpecialVar0, ResultVar)
            .Emit("GetPartyMonSpecies", SpecialVar0, SpecialVar1)
            .Emit("NPCTradeGetReqSpecies", ResultVar)
            .Emit("CompareVarToVar", SpecialVar1, ResultVar).Emit("GoToIf", ConditionNotEqual, "wrong")
            .Emit("NPCTradeExec", SpecialVar0).Emit("NPCTradeEnd")
            .Emit("SetFlag", flag)
            .Label("done")
            .Emit("NPCMsg", m[1]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End")
            .Label("wrong")
            .Emit("NPCTradeEnd")
            .Emit("NPCMsg", m[2]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End")
            .Label("decline")
            .Emit("NPCMsg", m[3]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
    }

    public static byte[] BuildScript(ScriptCommandTable table, NpcBehavior behavior, IReadOnlyList<int> messages, int? flag, int objectId = 0, int tradeSlot = 0, int? engageFlag = null)
    {
        var s = NewBuilder(table);
        bool platinum = IsPlatinum(table);
        if (behavior is TradeBehavior)
        {
            BuildTrade(s, tradeSlot, flag!.Value, messages, platinum);
            return s.Build();
        }
        if (behavior is PokemonBehavior mon)
        {
            s.Emit("PlaySE", SelectSound).Emit("LockAll").Emit("FacePlayer").Emit("PlayCry", mon.Species, 0);
            if (messages.Count > 0) s.Emit("NPCMsg", messages[0]);
            s.Emit("WaitCry");
            if (messages.Count > 0) s.Emit("CloseMsg");
            // sem Pokémon para lutar, a batalha não pode começar: o jogador só ouve o grito
            s.Emit("PartyCountNotEgg", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "noTeam");
            // a flag de encontro avisa o script de retorno, que esconde o Pokémon antes de a tela voltar
            if (engageFlag is { } engage) s.Emit("SetFlag", engage);
            if (platinum) s.Emit("StartWildBattle", mon.Species, mon.Level);
            else s.Emit("WildBattle", mon.Species, mon.Level, 0);
            if (engageFlag is { } clear) s.Emit("ClearFlag", clear);
            s.Emit("CheckBattleWon", ResultVar)
                .Emit("CompareVarToValue", ResultVar, 0)
                .Emit("GoToIf", ConditionEqual, "lost");
            if (flag is { } hide) s.Emit("SetFlag", hide);
            return s.Emit("ReleaseAll").Emit("End")
                .Label("lost").Emit("WhiteOut").Emit("ReleaseAll").Emit("End")
                .Label("noTeam").Emit("ReleaseAll").Emit("End")
                .Build();
        }
        if (platinum) s.Emit("PlaySE", SelectSound);
        else s.Emit($"ScrCmd_{SelectSoundCommandSeen}");
        s.Emit("LockAll").Emit("FacePlayer");
        switch (behavior)
        {
            case TalkBehavior:
                s.Emit("NPCMsg", messages[0]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
                break;
            case GiveItemBehavior item:
                s.Emit("CheckFlag", flag!.Value)
                    .Emit("GoToIf", ConditionEqual, "after")
                    .Emit("NPCMsg", messages[0])
                    .Emit("SetVar", SpecialVar0, item.Item)
                    .Emit("SetVar", SpecialVar1, item.Quantity)
                    .Emit("HasSpaceForItem", SpecialVar0, SpecialVar1, ResultVar)
                    .Emit("CompareVarToValue", ResultVar, 0)
                    .Emit("GoToIf", ConditionEqual, "full")
                    .Emit("CallStd", platinum ? PlatinumGiveItem : GiveItemStd)
                    .Emit("SetFlag", flag.Value)
                    .Label("after")
                    .Emit("NPCMsg", messages[1]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End")
                    .Label("full")
                    .Emit("CallStd", platinum ? PlatinumBagFull : BagFullStd).Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
                break;
            default:
                throw new ArgumentException(behavior.GetType().Name);
        }
        return s.Build();
    }

    /// <summary>Valida tudo antes de gravar. Avisos não impedem a criação; erros viram exceção.</summary>
    public static NewNpcResult Create(Project project, EventMap map, NewNpc npc) => Apply(project, map, npc, null);

    /// <summary>Muda um NPC criado pelo hub. O número do script não muda, e as falas reaproveitam as mensagens que ele já tinha.</summary>
    public static NewNpcResult Update(Project project, EventMap map, CreatedNpc existing, NewNpc npc) => Apply(project, map, npc with { MapId = existing.MapId }, existing);

    /// <summary>
    /// Tira o NPC do mapa. O script fica no arquivo, porque os números dos outros dependem da posição dele, mas passa a só terminar:
    /// assim flags e vaga de troca voltam a ser livres. As mensagens ficam no banco, sem uso.
    /// </summary>
    public static void Remove(Project project, EventMap map, CreatedNpc existing)
    {
        var game = Gen4Game.For(project);
        var header = map.Headers.First(h => h.Id == existing.MapId);
        var table = TableOf(game);
        var eventsNarc = Narc.Read(File.ReadAllBytes(game.EventsPath));
        var scriptsNarc = Narc.Read(File.ReadAllBytes(game.ScriptsPath));
        var editor = new ScriptEditor(scriptsNarc.Files[header.ScriptsBank], table);
        var events = new EventFile(eventsNarc.Files[header.EventsBank]);
        if (events.Objects.All(o => o.Id != existing.ObjectId)) throw new InvalidDataException(Loc.T("core.newev.notFound"));

        editor.ReplaceEntry(existing.ScriptId, NewBuilder(table).Emit("End").Build());
        if (existing.LevelScriptId is { } level) WriteLevelScript(editor, table, level, null, 0, existing.ChainScriptId);
        scriptsNarc.Files[header.ScriptsBank] = editor.Raw;
        eventsNarc.Files[header.EventsBank] = EventFile.RemoveObject(eventsNarc.Files[header.EventsBank], existing.ObjectId);

        var ledger = NewEventLedger.Load(project);
        ledger.Npcs.RemoveAll(n => n.MapId == existing.MapId && n.ObjectId == existing.ObjectId);
        ledger.RetireFlag(existing.Flag);
        ledger.RetireFlag(existing.EngageFlag);

        WriteAtomic(game.ScriptsPath, scriptsNarc.Write());
        WriteAtomic(game.EventsPath, eventsNarc.Write());
        ledger.Save(project);
    }

    private static ScriptCommandTable TableOf(Gen4Game game) => game.IsPlatinum ? ScriptCommandTable.Platinum : ScriptCommandTable.Hgss;

    /// <summary>
    /// Script que o jogo roda ao voltar da batalha, como no Mewtwo: esconde o Pokémon antes de a tela aparecer.
    /// Se o mapa já tinha um script desse tipo, termina indo para ele. Sem objeto, só faz essa continuação.
    /// </summary>
    private static void WriteLevelScript(ScriptEditor editor, ScriptCommandTable table, int? entry, int? objectId, int engageFlag, int? chain, Action<int>? created = null)
    {
        var s = NewBuilder(table);
        if (objectId is { } id)
        {
            s.Emit("CheckFlag", engageFlag).Emit("GoToIf", ConditionEqual, "hide").Emit("GoTo", "tail")
                .Label("hide").Emit("HidePerson", id).Emit("ClearFlag", engageFlag)
                .Label("tail");
        }
        int tail = s.Position;
        s.Label("self");
        if (chain is null) s.Emit("End");
        else s.Emit("GoTo", "self"); // o destino é outro script: só dá para calcular depois de saber onde este ficou

        // sempre com espaço para um salto: outro script de retorno pode continuar neste endereço, e a versão nova precisa alcançar quem vier depois
        var code = s.Build();
        if (code.Length < JumpSize) Array.Resize(ref code, JumpSize);

        int number;
        if (entry is { } existing)
        {
            int old = editor.File.EntryOffsets[existing - 1];
            editor.ReplaceEntry(existing, code);
            var jump = NewBuilder(table).Label("self").Emit("GoTo", "self").Build();
            editor.Overwrite(old, jump);
            editor.PatchJump(old + 2, editor.File.EntryOffsets[existing - 1]);
            number = existing;
        }
        else
        {
            number = editor.AddEntry(code);
            created?.Invoke(number);
        }
        if (chain is { } target)
        {
            int at = editor.File.EntryOffsets[number - 1] + tail + 2;
            editor.PatchJump(at, editor.File.EntryOffsets[target - 1]);
        }
    }

    private static NewNpcResult Apply(Project project, EventMap map, NewNpc npc, CreatedNpc? existing)
    {
        var game = Gen4Game.For(project);
        if (!Supports(game)) throw new NotSupportedException(Loc.T("core.newev.hgssOnly"));
        var header = map.Headers.FirstOrDefault(h => h.Id == npc.MapId) ?? throw new ArgumentException(Loc.T("core.newev.noMap", npc.MapId));
        if (header.ScriptsBank < 0 || header.MsgBank < 0 || header.EventsBank < 0) throw new InvalidDataException(Loc.T("core.newev.noFiles", header.InternalName));
        if (npc.Facing is < 0 or > 3) throw new ArgumentException(Loc.T("core.newev.badFacing"));
        if (npc.Behavior is GiveItemBehavior { Quantity: < 1 or > 99 }) throw new ArgumentException(Loc.T("core.newev.badQuantity"));
        if (npc.Behavior is PokemonBehavior { Level: < 1 or > 100 }) throw new ArgumentException(Loc.T("core.newev.badLevel"));
        if (npc.Behavior is PokemonBehavior { Species: < 1 or > MaxSpecies }) throw new ArgumentException(Loc.T("core.newev.badSpecies"));
        if (npc.Behavior is PokemonBehavior mon && SpriteForSpecies(mon.Species, game.IsPlatinum) is null) throw new ArgumentException(Loc.T("core.newev.noFieldSprite"));
        if (npc.Behavior is TradeBehavior { AskSpecies: < 1 or > MaxSpecies } or TradeBehavior { GiveSpecies: < 1 or > MaxSpecies }) throw new ArgumentException(Loc.T("core.newev.badSpecies"));
        if (npc.Range is < 1 or > MaxRange) throw new ArgumentException(Loc.T("core.newev.badRange", MaxRange));

        var ledger = NewEventLedger.Load(project);
        int? tradeSlot = existing?.TradeSlot;
        if (npc.Behavior is TradeBehavior && tradeSlot is null)
        {
            var free = FreeTradeSlots(game);
            if (free.Count == 0) throw new InvalidDataException(Loc.T("core.newev.noTradeSlot"));
            tradeSlot = free[0];
        }

        var warnings = new List<string>();
        var eventsNarc = Narc.Read(File.ReadAllBytes(game.EventsPath));
        var scriptsNarc = Narc.Read(File.ReadAllBytes(game.ScriptsPath));
        var events = new EventFile(eventsNarc.Files[header.EventsBank]);
        var current = existing is null ? null : events.Objects.FirstOrDefault(o => o.Id == existing.ObjectId) ?? throw new InvalidDataException(Loc.T("core.newev.notFound"));

        if (events.Objects.Any(o => o.X == npc.X && o.Z == npc.Z && o.Id != current?.Id)) warnings.Add(Loc.T("core.newev.occupied"));
        if (existing is null && events.Objects.Count >= NpcWarningCount) warnings.Add(Loc.T("core.newev.tooMany", NpcWarningCount));
        var sharing = map.Headers.Where(h => h.Id != npc.MapId && (h.ScriptsBank == header.ScriptsBank || h.EventsBank == header.EventsBank)).Select(h => h.InternalName).Distinct().ToList();
        if (sharing.Count > 0) warnings.Add(Loc.T("core.newev.shared", string.Join(", ", sharing.Take(4))));
        if (map.LayoutOf(npc.MapId) is { } layout)
        {
            int lx = npc.X - layout.OriginX, ly = npc.Z - layout.OriginY;
            if (lx >= 0 && ly >= 0 && lx < layout.Width && ly < layout.Height && layout.IsBlocked(lx, ly)) warnings.Add(Loc.T("core.newev.blocked"));
        }

        bool needsFlag = npc.Behavior is GiveItemBehavior or TradeBehavior or PokemonBehavior { Vanish: true };
        bool vanishes = npc.Behavior is PokemonBehavior { Vanish: true };
        var taken = new HashSet<int>(ledger.RetiredFlags);
        int trainers = Narc.Read(File.ReadAllBytes(game.TrainerDataPath)).Files.Count;
        int Pick()
        {
            int picked = PickFreeFlag(map, trainers, taken) ?? throw new InvalidDataException(Loc.T("core.newev.noFlag"));
            taken.Add(picked);
            return picked;
        }
        int? flag = needsFlag ? existing?.Flag ?? Pick() : null;
        int? engage = vanishes ? existing?.EngageFlag ?? Pick() : null;

        // texto primeiro: é o que recusa caractere inválido, antes de qualquer gravação
        var texts = TextRepository.Load(project);
        string[] lines = npc.Behavior switch
        {
            TalkBehavior t => [t.Text],
            GiveItemBehavior i => [EndWithWait(i.BeforeText), i.AfterText],
            PokemonBehavior { Text: { Length: > 0 } text } => [EndWithWait(text)],
            TradeBehavior t => [t.AskText, t.ThanksText, t.WrongText, t.DeclineText],
            _ => [],
        };
        var messages = new List<int>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (existing is not null && i < existing.Messages.Count)
            {
                texts.SetText(header.MsgBank, existing.Messages[i], lines[i]);
                messages.Add(existing.Messages[i]);
            }
            else messages.Add(texts.AddText(header.MsgBank, lines[i]));
        }

        var table = TableOf(game);
        int objectId = current?.Id ?? (events.Objects.Count == 0 ? 0 : events.Objects.Max(o => o.Id) + 1);
        var code = BuildScript(table, npc.Behavior, messages, flag, objectId, tradeSlot ?? 0, engage);

        byte[]? tradeNarc = null;
        if (npc.Behavior is TradeBehavior trade)
        {
            int original = game.IsPlatinum ? PlatinumTradeSlots : TradeSlots;
            var trades = Narc.Read(File.ReadAllBytes(TradesPathOf(game)));
            // o arquivo precisa chegar até a vaga: as de permeio são cópias que nenhum script carrega
            while (trades.Files.Count <= tradeSlot!.Value) trades.Files.Add((byte[])trades.Files[0].Clone());
            var record = trades.Files[tradeSlot.Value];
            if (tradeSlot.Value >= original) BitConverter.TryWriteBytes(record.AsSpan(TradeHeldItemOffset), 0); // a cópia não leva o item da troca original
            BitConverter.TryWriteBytes(record.AsSpan(TradeGiveOffset), trade.GiveSpecies);
            BitConverter.TryWriteBytes(record.AsSpan(TradeAskOffset), trade.AskSpecies);
            // a habilidade guardada precisa ser uma que a espécie nova tenha
            var personal = PersonalRepository.Load(project).Get(trade.GiveSpecies);
            if (personal is not null) BitConverter.TryWriteBytes(record.AsSpan(TradeAbilityOffset), (int)personal.Ability1);
            tradeNarc = trades.Write();
            int namesBank = TradeNamesBankOf(game);
            var names = texts.Bank(namesBank);
            while (names.Texts.Count <= tradeSlot.Value + original) texts.AddText(namesBank, "");
            texts.SetText(namesBank, tradeSlot.Value, string.IsNullOrWhiteSpace(trade.Nickname) ? texts.Species(trade.GiveSpecies) : trade.Nickname);
            // vaga nova nasce sem treinador: usa o nome do treinador da primeira troca do jogo
            if (tradeSlot.Value >= original) texts.SetText(namesBank, tradeSlot.Value + original, names.Texts[original]);
        }

        var editor = new ScriptEditor(scriptsNarc.Files[header.ScriptsBank], table);
        int scriptId = existing?.ScriptId ?? 0;
        if (existing is null) scriptId = editor.AddEntry(code);
        else editor.ReplaceEntry(scriptId, code);

        int? levelScript = existing?.LevelScriptId, chain = existing?.ChainScriptId;
        if (vanishes && levelScript is null)
        {
            // quem já estava na lista de scripts de retorno do mapa continua rodando, depois do novo
            chain = new MapScriptHeader(scriptsNarc.Files[header.ScriptHeaderBank]).Entries.FirstOrDefault(e => e.Kind == ReturnScriptKind)?.A;
            WriteLevelScript(editor, table, null, objectId, engage!.Value, chain, created => levelScript = created);
            scriptsNarc.Files[header.ScriptHeaderBank] = MapScriptHeader.SetEntry(scriptsNarc.Files[header.ScriptHeaderBank], ReturnScriptKind, levelScript!.Value);
        }
        else if (levelScript is not null)
            WriteLevelScript(editor, table, levelScript, vanishes ? objectId : null, engage ?? 0, chain);
        scriptsNarc.Files[header.ScriptsBank] = editor.Raw;

        int sprite = npc.Behavior is PokemonBehavior p ? SpriteForSpecies(p.Species, game.IsPlatinum) ?? npc.SpriteId : npc.SpriteId;
        var updated = new ObjectEvent(current?.Index ?? events.Objects.Count,
            Id: objectId,
            SpriteId: sprite,
            Movement: npc.Movement switch
            {
                NpcMovement.LookAround => MovementLookAround,
                NpcMovement.Wander => MovementWander,
                NpcMovement.UpDown => MovementWander + 1,
                NpcMovement.LeftRight => MovementWander + 2,
                _ => FixedFacingMovement + npc.Facing,
            },
            Type: 0,
            // a flag do Pokémon que some esconde o objeto; a do item só marca a entrega, e o NPC continua lá
            EventFlag: vanishes ? flag!.Value : 0,
            ScriptId: scriptId,
            Facing: npc.Facing,
            X: npc.X,
            Z: npc.Z,
            Y: current is not null && current.X == npc.X && current.Z == npc.Z ? current.Y : HeightFor(events, npc.X, npc.Z),
            XRange: npc.Movement is NpcMovement.Wander or NpcMovement.LeftRight ? npc.Range : 0,
            ZRange: npc.Movement is NpcMovement.Wander or NpcMovement.UpDown ? npc.Range : 0);
        eventsNarc.Files[header.EventsBank] = current is null
            ? EventFile.AddObject(eventsNarc.Files[header.EventsBank], updated)
            : EventFile.ReplaceObject(eventsNarc.Files[header.EventsBank], updated);

        // o que a edição deixou de usar não volta para a lista de livres: num save, a flag pode já estar ligada
        if (existing?.Flag is { } oldFlag && oldFlag != flag) ledger.RetireFlag(oldFlag);
        if (existing?.EngageFlag is { } oldEngage && oldEngage != engage) ledger.RetireFlag(oldEngage);
        ledger.Npcs.RemoveAll(n => n.MapId == npc.MapId && n.ObjectId == objectId);
        ledger.Npcs.Add(CreatedNpc.From(npc, objectId, scriptId, messages, flag, engage, levelScript, chain, npc.Behavior is TradeBehavior ? tradeSlot : existing?.TradeSlot));

        texts.Save();
        WriteAtomic(game.ScriptsPath, scriptsNarc.Write());
        WriteAtomic(game.EventsPath, eventsNarc.Write());
        if (tradeNarc is not null) WriteAtomic(TradesPathOf(game), tradeNarc);
        ledger.Save(project);
        return new NewNpcResult(updated.Index, scriptId, flag, warnings);
    }

    /// <summary>Nos NPCs do jogo, 14 a 17 olham fixo para cima, baixo, esquerda e direita.</summary>
    private const int FixedFacingMovement = 14, MovementLookAround = 2, MovementWander = 3;
    /// <summary>Tipo de entrada do cabeçalho do mapa que os encontros parados do jogo usam, nos dois jogos.</summary>
    private const int ReturnScriptKind = 4;
    private const int JumpSize = 6;
    private const string TradesRelativePath = @"a\1\1\2", PlatinumTradesRelativePath = @"fielddata\pokemon_trade\fld_trade.narc";

    /// <summary>Sem esperar o botão, o próximo comando apagaria a fala antes de dar para ler.</summary>
    private static string EndWithWait(string text) => text.EndsWith("\\r", StringComparison.Ordinal) ? text : text + "\\r";

    private static void WriteAtomic(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Um NPC criado pelo hub, com tudo o que é preciso para editá-lo ou removê-lo depois.</summary>
public sealed class CreatedNpc
{
    public int MapId { get; set; }
    public int ObjectId { get; set; }
    public int ScriptId { get; set; }
    public string Kind { get; set; } = "";
    public int X { get; set; }
    public int Z { get; set; }
    public int SpriteId { get; set; }
    public int Facing { get; set; }
    public NpcMovement Movement { get; set; }
    public int Range { get; set; } = 1;
    public List<int> Messages { get; set; } = [];
    public List<string> Texts { get; set; } = [];
    public int? Flag { get; set; }
    public int? EngageFlag { get; set; }
    public int? LevelScriptId { get; set; }
    public int? ChainScriptId { get; set; }
    public int? TradeSlot { get; set; }
    public int Item { get; set; }
    public int Quantity { get; set; }
    public int Species { get; set; }
    public int Level { get; set; }
    public bool Vanish { get; set; }
    public int AskSpecies { get; set; }
    public int GiveSpecies { get; set; }
    public string? Nickname { get; set; }

    public NpcBehavior ToBehavior() => Kind switch
    {
        "item" => new GiveItemBehavior(Item, Quantity, Texts[0], Texts[1]),
        "pokemon" => new PokemonBehavior(Species, Level, Vanish, Texts.FirstOrDefault()),
        "trade" => new TradeBehavior(AskSpecies, GiveSpecies, Nickname, Texts[0], Texts[1], Texts[2], Texts[3]),
        _ => new TalkBehavior(Texts[0]),
    };

    public NewNpc ToNewNpc() => new(MapId, X, Z, SpriteId, Facing, ToBehavior(), Movement, Range);

    internal static CreatedNpc From(NewNpc npc, int objectId, int scriptId, List<int> messages, int? flag, int? engage, int? level, int? chain, int? slot)
    {
        var c = new CreatedNpc
        {
            MapId = npc.MapId, ObjectId = objectId, ScriptId = scriptId, X = npc.X, Z = npc.Z, SpriteId = npc.SpriteId, Facing = npc.Facing, Movement = npc.Movement, Range = npc.Range,
            Messages = messages, Flag = flag, EngageFlag = engage, LevelScriptId = level, ChainScriptId = chain, TradeSlot = slot,
        };
        switch (npc.Behavior)
        {
            case TalkBehavior t: c.Kind = "talk"; c.Texts = [t.Text]; break;
            case GiveItemBehavior i: c.Kind = "item"; c.Item = i.Item; c.Quantity = i.Quantity; c.Texts = [i.BeforeText, i.AfterText]; break;
            case PokemonBehavior p: c.Kind = "pokemon"; c.Species = p.Species; c.Level = p.Level; c.Vanish = p.Vanish; c.Texts = p.Text is { Length: > 0 } text ? [text] : []; break;
            case TradeBehavior t: c.Kind = "trade"; c.AskSpecies = t.AskSpecies; c.GiveSpecies = t.GiveSpecies; c.Nickname = t.Nickname; c.Texts = [t.AskText, t.ThanksText, t.WrongText, t.DeclineText]; break;
        }
        return c;
    }
}

/// <summary>
/// Lista dos NPCs criados pelo hub, em pokehub-events.json no workspace: fica junto do que ela descreve, então o histórico de versões
/// volta os dois ao mesmo tempo. Flags aposentadas não são reusadas, porque um save antigo pode tê-las ligadas.
/// </summary>
public sealed class NewEventLedger
{
    private const string FileName = "pokehub-events.json";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public List<CreatedNpc> Npcs { get; set; } = [];
    public List<int> RetiredFlags { get; set; } = [];

    public void RetireFlag(int? flag)
    {
        if (flag is { } f && !RetiredFlags.Contains(f)) RetiredFlags.Add(f);
    }

    public static NewEventLedger Load(Project project)
    {
        var path = Path.Combine(project.Paths.Workspace, FileName);
        return File.Exists(path) ? JsonSerializer.Deserialize<NewEventLedger>(File.ReadAllText(path), Options) ?? new() : new();
    }

    public void Save(Project project) => File.WriteAllText(Path.Combine(project.Paths.Workspace, FileName), JsonSerializer.Serialize(this, Options));
}
