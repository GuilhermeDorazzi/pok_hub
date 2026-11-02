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

/// <summary>
/// Pokémon parado no mapa: grito e batalha. Com Vanish, some depois da batalha; só capturado some de vez, senão volta quando o mapa recarregar.
/// Com Blocks, sai de vez se vencido ou capturado e, ao fugir, fica no lugar, para não abrir o caminho. Texto é opcional.
/// </summary>
public sealed record PokemonBehavior(int Species, int Level, bool Vanish, string? Text, bool Blocks = false) : NpcBehavior;

/// <summary>Troca: o NPC pede uma espécie e dá outra, usando uma vaga livre da tabela de trocas do jogo.</summary>
public sealed record TradeBehavior(int AskSpecies, int GiveSpecies, string? Nickname, string AskText, string ThanksText, string WrongText, string DeclineText, int MinLevel = 0, string? TooLowText = null) : NpcBehavior;

/// <summary>
/// Missão: pede um item ou mostrar um Pokémon e dá uma recompensa, uma vez só. O Pokémon pedido só é conferido no time, sem sair dele.
/// Recompensa em item, Pokémon ou dinheiro, com o que for diferente de zero ou null.
/// </summary>
public sealed record QuestBehavior(int? AskItem, int AskQuantity, bool TakeItem, int? AskSpecies, int? RewardItem, int RewardQuantity, int? RewardSpecies, int RewardLevel, int RewardMoney,
    string AskText, string NotYetText, string RewardText, string DoneText, string FullText) : NpcBehavior;

/// <summary>Vende um Pokémon por dinheiro. Com Once, vende uma vez só.</summary>
public sealed record SellBehavior(int Species, int Level, int Price, bool Once, string OfferText, string ThanksText, string NoMoneyText, string DeclineText, string FullText, string SoldOutText) : NpcBehavior;

public enum TrainerRematch { None, Always, AfterElite }

/// <summary>Item para achar no mapa: numa Poké Bola visível, ou escondido num tile, que entrega ao apertar A virado para ele.</summary>
public sealed record GroundItemBehavior(int Item, int Quantity, bool Hidden) : NpcBehavior;

/// <summary>Uma etapa do guia: enquanto a marca estiver desligada, o NPC diz esta fala.</summary>
public sealed record GuideStep(int Flag, string Text);

/// <summary>NPC que acompanha o progresso: diz a fala da primeira etapa não concluída e, com tudo feito, a fala final.</summary>
public sealed record GuideBehavior(IReadOnlyList<GuideStep> Steps, string FinalText) : NpcBehavior;

/// <summary>
/// Treinador. Com Sight 0 ele não chama: a batalha começa ao falar com ele, com as falas daqui e uma flag própria, sem mexer na do treinador original.
/// Com Sight maior que 0, ele chama de longe como os do jogo: usa o script padrão, as falas e a flag de vencido do próprio registro.
/// </summary>
public sealed record TrainerBehavior(int Trainer, string ChallengeText, string BeatenText, string AfterText, int Sight = 0, bool Vanish = false,
    TrainerRematch Rematch = TrainerRematch.None, int? RematchTrainer = null, string? RematchText = null) : NpcBehavior;

public enum SceneAction { Say, Walk, Wait, Face, Battle, Hide }

/// <summary>
/// Um passo de uma cena. Value é o número do treinador, os quadros da espera ou os passos da caminhada; Direction vale para a caminhada: 0 cima, 1 baixo, 2 esquerda, 3 direita.
/// Na batalha, Cyndaquil e Totodile, quando preenchidos, trocam o treinador conforme o inicial do jogador, como o rival do jogo: Value vale para quem escolheu Chikorita.
/// </summary>
public sealed record SceneStep(SceneAction Action, string? Text = null, int Value = 0, int Direction = 0, int Cyndaquil = 0, int Totodile = 0)
{
    public IEnumerable<int> Trainers() => Action != SceneAction.Battle ? [] : Cyndaquil > 0 ? [Value, Cyndaquil, Totodile] : [Value];
}

/// <summary>
/// Cena com o próprio NPC: fala, anda, espera, luta e some, na ordem dada, uma vez só. Perder a batalha recomeça a cena.
/// Sem sumir, ao falar de novo ele repete a última fala.
/// </summary>
public sealed record SceneBehavior(IReadOnlyList<SceneStep> Steps) : NpcBehavior;

/// <summary>O que precisa ser verdade para o NPC fazer o que faz. Sem isso, ele só diz o Text. Vale tudo o que estiver preenchido, ao mesmo tempo.</summary>
public sealed record NpcCondition(string Text, bool AfterElite = false, int MinBadges = 0, int? Badge = null, int? AfterFlag = null, int MaxPartyLevel = 0);

/// <summary>Como o NPC se move. Os códigos são MOVEMENT_TYPE nas decomps; andar usa um alcance em tiles a partir da posição dele.</summary>
public enum NpcMovement { Fixed, LookAround, Wander, UpDown, LeftRight }

/// <param name="X">Tile na matriz inteira, como guardado no arquivo de eventos.</param>
/// <param name="Facing">0 cima, 1 baixo, 2 esquerda, 3 direita.</param>
public sealed record NewNpc(int MapId, int X, int Z, int SpriteId, int Facing, NpcBehavior Behavior, NpcMovement Movement = NpcMovement.Fixed, int Range = 1, NpcCondition? Condition = null);

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

    /// <summary>Dados que não são comandos, como os passos de uma caminhada, alinhados de 4 em 4 bytes.</summary>
    public ScriptBuilder Data(string label, params ushort[] values)
    {
        while (_code.Length % 4 != 0) _code.WriteByte(0);
        _labels[label] = (int)_code.Length;
        foreach (var value in values) _code.Write(BitConverter.GetBytes(value));
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
    private const int OutcomeWin = 1, OutcomeCaught = 4;
    private const int GiveItemStd = 2033, BagFullStd = 2009, PlatinumGiveItem = 2016, PlatinumBagFull = 2017, ConditionEqual = 1, SelectSound = 1500, MaxSpecies = 493;
    /// <summary>Comparação do GoToIf: abaixo do valor. Depois de CheckFlag, quer dizer flag desligada.</summary>
    private const int ConditionLess = 0;
    private const int MaxParty = 6, MaxBadges = 16, ReceiveFanfare = 1187, MoneyBoxX = 20, MoneyBoxY = 2;
    /// <summary>FLAG_GAME_CLEAR, ligada ao terminar a Elite. O número 0x964 vem da decomp do HG/SS.</summary>
    public const int GameClearFlag = 0x964;
    private const int EnigmaStone = 536, EnigmaStoneVar = 0x4043, EnigmaStoneMark = 0x06F2;
    /// <summary>Script padrão de treinador com visão: o jogo tira o número do treinador do número do script (std_trainer na decomp).</summary>
    private const int TrainerStdScript = 3000, FirstTrainer = 1, MaxSight = 15;
    /// <summary>
    /// HG/SS: o script padrão dos treinadores com visão fica num arquivo só, com uma entrada por treinador do jogo. A última, a do treinador 740,
    /// é o script que roda quando um treinador avista o jogador, e o jogo chama esse número direto. Treinador novo depois dela precisa de entrada própria.
    /// </summary>
    public const int HgssTrainerScriptFile = 953, HgssApproachTrainer = 740;
    /// <summary>Banco de texto que o arquivo de scripts de treinador usa (msg_0040): a pergunta da revanche fica nele.</summary>
    public const int HgssTrainerScriptBank = 40;
    public const string DefaultRematchText = "Want a rematch?";
    private const int TrainerIntro = 0, TrainerAfter = 2;
    private const string YesNoPrompt = " {YESNO 0}";
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

    /// <summary>Shuckie e Kenya: não passam por LoadNPCTrade, e sim por GiveLoanMon e por números fixos no código do jogo (NPC_TRADE_SHUCKIE_SHUCKLE, NPC_TRADE_KENYA_SPEAROW).</summary>
    private static readonly long[] HgssLoanedTradeSlots = [6, 7];

    /// <summary>Vagas de troca que o jogo não usa: primeiro as originais sem uso, depois as novas. Usar uma delas não afeta nenhum NPC do jogo.</summary>
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
        if (!game.IsPlatinum) used.UnionWith(HgssLoanedTradeSlots);
        return Enumerable.Range(0, original).Concat(ExtraTradeSlots(original)).Where(slot => !used.Contains(slot)).ToList();
    }

    private static void BuildTrade(ScriptBuilder s, int slot, int flag, IReadOnlyList<int> m, bool platinum, int minLevel = 0)
    {
        s.Emit("PlaySE", SelectSound).Emit("LockAll").Emit("FacePlayer");
        BuildTradeBody(s, slot, flag, m, platinum, minLevel);
    }

    private static void BuildTradeBody(ScriptBuilder s, int slot, int flag, IReadOnlyList<int> m, bool platinum, int minLevel = 0)
    {
        s.Emit("CheckFlag", flag).Emit("GoToIf", ConditionEqual, "done")
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
            .Emit("CompareVarToVar", SpecialVar1, ResultVar).Emit("GoToIf", ConditionNotEqual, "wrong");
        if (minLevel > 0)
            s.Emit("MonGetLevel", ResultVar, SpecialVar0).Emit("CompareVarToValue", ResultVar, minLevel).Emit("GoToIf", ConditionLess, "tooLow");
        s.Emit("NPCTradeExec", SpecialVar0).Emit("NPCTradeEnd")
            .Emit("SetFlag", flag)
            .Label("done")
            .Emit("NPCMsg", m[1]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End")
            .Label("wrong")
            .Emit("NPCTradeEnd")
            .Emit("NPCMsg", m[2]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
        if (minLevel > 0)
            s.Label("tooLow").Emit("NPCTradeEnd").Emit("NPCMsg", m[4]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
        s
            .Label("decline")
            .Emit("NPCMsg", m[3]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
    }

    /// <summary>Script padrão do jogo para esse treinador, como nos NPCs com visão do próprio jogo.</summary>
    public static int TrainerScriptOf(int trainer) => trainer - FirstTrainer + TrainerStdScript;

    /// <summary>
    /// O arquivo de scripts de treinador com entradas até a do treinador pedido; as novas apontam para o script comum de falar com treinador,
    /// o da primeira entrada. Sem isso, falar com um treinador novo com visão roda o que estiver depois da tabela.
    /// </summary>
    public static byte[] WithTrainerScript(byte[] raw, ScriptCommandTable table, int trainer)
    {
        if (trainer == HgssApproachTrainer) throw new ArgumentException(Loc.T("core.newev.approachTrainer", HgssApproachTrainer));
        var editor = new ScriptEditor(raw, table);
        // cada entrada nova desloca o código em 4 bytes: a posição do script comum é lida de novo a cada volta
        while (editor.File.EntryOffsets.Count < trainer - FirstTrainer + 1) editor.AddEntryTo(editor.File.EntryOffsets[0]);
        return editor.Raw;
    }

    /// <summary>
    /// Falar com um treinador novo com visão roda um script próprio, que confere a vitória pelo número do treinador. O script comum do jogo
    /// passa pela agenda do Pokégear e, com treinadores novos, deixava lutar de novo a cada conversa. Já vencido, ele pergunta se o jogador quer revanche.
    /// Ser avistado continua no script do jogo.
    /// </summary>
    public static byte[] BuildTrainerTalk(ScriptCommandTable table, int trainer, int askMessage)
    {
        var s = new ScriptBuilder(table);
        s.Emit("PlaySE", SelectSound).Emit("LockAll").Emit("FacePlayer")
            .Emit("PartyCountNotEgg", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "after")
            .Emit("CheckTrainerFlag", trainer).Emit("GoToIf", ConditionEqual, "beaten")
            .Emit("EncounterMusic", trainer).Emit("OpenMsg").Emit("TrainerMessage", trainer, TrainerIntro).Emit("CloseMsg")
            .Emit("TrainerBattle", trainer, 0, 0, 0)
            .Emit("CheckBattleWon", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "lost")
            .Emit("SetTrainerFlag", trainer).Emit("ReleaseAll").Emit("End");
        s.Label("beaten");
        s.Emit("OpenMsg").Emit("TrainerMessage", trainer, TrainerAfter).Emit("WaitButton");
        AskYesNo(s, askMessage, "no");
        s.Emit("CloseMsg").Emit("EncounterMusic", trainer)
            .Emit("TrainerBattle", trainer, 0, 0, 0)
            .Emit("CheckBattleWon", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "lost")
            .Emit("ReleaseAll").Emit("End");
        s.Label("no").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
        s.Label("after").Emit("OpenMsg").Emit("TrainerMessage", trainer, TrainerAfter).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
        s.Label("lost").Emit("WhiteOut").Emit("ReleaseAll").Emit("End");
        return s.Build();
    }

    /// <summary>A entrada do treinador passa a apontar para o script próprio. Na primeira vez ele vai para o fim do arquivo; depois, é regravado no lugar.</summary>
    public static byte[] WithTrainerTalk(byte[] raw, ScriptCommandTable table, int trainer, int askMessage)
    {
        var editor = new ScriptEditor(WithTrainerScript(raw, table, trainer), table);
        int id = trainer - FirstTrainer + 1;
        var code = BuildTrainerTalk(table, trainer, askMessage);
        int at = editor.File.EntryOffsets[id - 1];
        bool own = at != editor.File.EntryOffsets[0] && editor.File.Routine(id)!.Instructions.Any(i => i.Name == "CheckTrainerFlag" && i.Args[0].Value == trainer);
        if (own) editor.Overwrite(at, code);
        else editor.ReplaceEntry(id, code);
        return editor.Raw;
    }

    /// <summary>A pergunta da revanche no banco comum: reaproveita a mesma fala em vez de repetir uma por treinador.</summary>
    private static int CommonText(TextRepository texts, string text)
    {
        var bank = texts.Bank(HgssTrainerScriptBank);
        int found = bank.Texts.IndexOf(text);
        return found >= 0 ? found : texts.AddText(HgssTrainerScriptBank, text);
    }

    /// <summary>Dá o script próprio de conversa a todos os treinadores com visão criados pelo hub. Serve para quem os criou antes desta correção.</summary>
    public static int UpdateSightTrainerTalks(Project project)
    {
        var game = Gen4Game.For(project);
        if (!game.IsHgss) return 0;
        var sighted = NewEventLedger.Load(project).Npcs.Select(n => n.ToBehavior()).OfType<TrainerBehavior>().Where(t => t.Sight > 0)
            .GroupBy(t => t.Trainer).Select(g => g.First()).ToList();
        if (sighted.Count == 0) return 0;
        var texts = TextRepository.Load(project);
        var scripts = Narc.Read(File.ReadAllBytes(game.ScriptsPath));
        foreach (var t in sighted)
            scripts.Files[HgssTrainerScriptFile] = WithTrainerTalk(scripts.Files[HgssTrainerScriptFile], ScriptCommandTable.Hgss, t.Trainer,
                CommonText(texts, WithYesNo(t.RematchText ?? DefaultRematchText)));
        texts.Save();
        File.WriteAllBytes(game.ScriptsPath, scripts.Write());
        return sighted.Count;
    }

    /// <summary>Garante as entradas de todos os treinadores do projeto. Serve para quem criou treinadores com visão antes desta correção.</summary>
    public static int EnsureTrainerScripts(Project project)
    {
        var game = Gen4Game.For(project);
        if (!game.IsHgss) return 0;
        int last = Narc.Read(File.ReadAllBytes(game.TrainerDataPath)).Files.Count - 1;
        var scripts = Narc.Read(File.ReadAllBytes(game.ScriptsPath));
        int before = new ScriptFile(scripts.Files[HgssTrainerScriptFile], ScriptCommandTable.Hgss).EntryOffsets.Count;
        scripts.Files[HgssTrainerScriptFile] = WithTrainerScript(scripts.Files[HgssTrainerScriptFile], ScriptCommandTable.Hgss, last == HgssApproachTrainer ? last - 1 : last);
        if (last == HgssApproachTrainer) return 0;
        File.WriteAllBytes(game.ScriptsPath, scripts.Write());
        return new ScriptFile(scripts.Files[HgssTrainerScriptFile], ScriptCommandTable.Hgss).EntryOffsets.Count - before;
    }

    private static void EmitConditionChecks(ScriptBuilder s, NpcCondition? c)
    {
        if (c is null) return;
        if (c.AfterElite) s.Emit("CheckFlag", GameClearFlag).Emit("GoToIf", ConditionLess, "locked");
        if (c.AfterFlag is { } after) s.Emit("CheckFlag", after).Emit("GoToIf", ConditionLess, "locked");
        if (c.Badge is { } badge) s.Emit("CheckBadge", badge, ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "locked");
        if (c.MaxPartyLevel > 0)
            s.Emit("PartyCountNotEgg", SpecialVar1).Emit("PartyCountMonsAtOrBelowLevel", ResultVar, c.MaxPartyLevel)
                .Emit("CompareVarToVar", ResultVar, SpecialVar1).Emit("GoToIf", ConditionLess, "locked");
        if (c.MinBadges > 0) s.Emit("CountBadges", ResultVar).Emit("CompareVarToValue", ResultVar, c.MinBadges).Emit("GoToIf", ConditionLess, "locked");
    }

    private static ScriptBuilder Say(ScriptBuilder s, int message) =>
        s.Emit("NPCMsg", message).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");

    private static ScriptBuilder AskYesNo(ScriptBuilder s, int message, string noLabel) =>
        s.Emit("NPCMsg", message).Emit("TouchscreenMenuHide").Emit("GetMenuChoice", ResultVar).Emit("TouchscreenMenuShow")
            .Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "yes").Emit("GoTo", noLabel).Label("yes");

    private static ScriptBuilder EmitGiveMon(ScriptBuilder s, int species, int level) =>
        s.Emit("PlayFanfare", ReceiveFanfare).Emit("WaitFanfare").Emit("GiveMon", species, level, 0, 0, 0, ResultVar);

    /// <summary>Ordem pensada para ninguém perder nada: confere o pedido e o espaço da recompensa antes de tirar o item.</summary>
    private static void BuildQuest(ScriptBuilder s, QuestBehavior q, IReadOnlyList<int> m, int flag)
    {
        s.Emit("CheckFlag", flag).Emit("GoToIf", ConditionEqual, "done");
        // sem pedido, é um presente: não há o que conferir antes da fala
        if (q.AskItem is { } item) s.Emit("HasItem", item, q.AskQuantity, ResultVar);
        else if (q.AskSpecies is { } asked) s.Emit("PlayerHasSpecies", ResultVar, asked);
        if (q.AskItem is not null || q.AskSpecies is not null) s.Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "notYet");
        s.Emit("NPCMsg", m[0]);
        if (q.RewardItem is { } reward)
            s.Emit("SetVar", SpecialVar0, reward).Emit("SetVar", SpecialVar1, q.RewardQuantity)
                .Emit("HasSpaceForItem", SpecialVar0, SpecialVar1, ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "full");
        if (q.RewardSpecies is not null)
            s.Emit("GetPartyCount", ResultVar).Emit("CompareVarToValue", ResultVar, MaxParty).Emit("GoToIf", ConditionEqual, "full");
        if (q.AskItem is { } taken && q.TakeItem) s.Emit("TakeItem", taken, q.AskQuantity, ResultVar);
        s.Emit("SetFlag", flag);
        s.Emit("NPCMsg", m[2]);
        // a Enigma Stone só libera Latias e Latios com a marca que o Presente Misterioso grava junto (MGGive_Item, na decomp)
        if (q.RewardItem == EnigmaStone) s.Emit("SetVar", EnigmaStoneVar, EnigmaStoneMark);
        if (q.RewardItem is not null) s.Emit("CallStd", GiveItemStd);
        if (q.RewardSpecies is { } species) EmitGiveMon(s, species, q.RewardLevel);
        if (q.RewardMoney > 0) s.Emit("AddMoney", q.RewardMoney);
        s.Label("done");
        Say(s, m[3]);
        s.Label("notYet");
        Say(s, m[1]);
        s.Label("full");
        Say(s, m[4]);
    }

    private static void BuildSell(ScriptBuilder s, SellBehavior v, IReadOnlyList<int> m, int? flag)
    {
        if (v.Once) s.Emit("CheckFlag", flag!.Value).Emit("GoToIf", ConditionEqual, "soldOut");
        s.Emit("ShowMoneyBox", MoneyBoxX, MoneyBoxY);
        AskYesNo(s, m[0], "decline");
        s.Emit("HasEnoughMoneyImmediate", ResultVar, v.Price).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "noMoney")
            .Emit("GetPartyCount", ResultVar).Emit("CompareVarToValue", ResultVar, MaxParty).Emit("GoToIf", ConditionEqual, "full")
            .Emit("SubMoneyImmediate", v.Price).Emit("UpdateMoneyBox");
        if (v.Once) s.Emit("SetFlag", flag!.Value);
        s.Emit("NPCMsg", m[1]);
        EmitGiveMon(s, v.Species, v.Level);
        s.Emit("WaitButton");
        void End(string label, int message)
        {
            s.Label(label).Emit("NPCMsg", message).Emit("WaitButton");
            s.Emit("CloseMsg").Emit("HideMoneyBox").Emit("ReleaseAll").Emit("End");
        }
        s.Emit("CloseMsg").Emit("HideMoneyBox").Emit("ReleaseAll").Emit("End");
        End("decline", m[3]);
        End("noMoney", m[2]);
        End("full", m[4]);
        if (v.Once)
        {
            s.Label("soldOut");
            Say(s, m[5]);
        }
    }

    private static void BuildTrainer(ScriptBuilder s, TrainerBehavior t, IReadOnlyList<int> m, int flag, int objectId)
    {
        s.Emit("CheckFlag", flag).Emit("GoToIf", ConditionEqual, "done");
        // sem Pokémon para lutar, a batalha não pode começar
        s.Emit("PartyCountNotEgg", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "after");
        s.Emit("NPCMsg", m[0]).Emit("CloseMsg")
            .Emit("TrainerBattle", t.Trainer, 0, 0, 0)
            .Emit("CheckBattleWon", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "lost")
            .Emit("SetFlag", flag)
            .Emit("NPCMsg", m[1]).Emit("WaitButton").Emit("CloseMsg");
        if (t.Vanish) s.Emit("HidePerson", objectId);
        s.Emit("ReleaseAll").Emit("End");
        s.Label("lost").Emit("WhiteOut").Emit("ReleaseAll").Emit("End");
        s.Label("done");
        if (!t.Vanish && t.Rematch != TrainerRematch.None)
        {
            if (t.Rematch == TrainerRematch.AfterElite) s.Emit("CheckFlag", GameClearFlag).Emit("GoToIf", ConditionLess, "after");
            s.Emit("PartyCountNotEgg", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "after");
            AskYesNo(s, m[3], "after");
            s.Emit("CloseMsg").Emit("TrainerBattle", t.RematchTrainer ?? t.Trainer, 0, 0, 0)
                .Emit("CheckBattleWon", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "lost")
                .Emit("NPCMsg", m[1]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
        }
        s.Label("after");
        Say(s, m[2]);
    }

    private static void BuildGroundItem(ScriptBuilder s, GroundItemBehavior g, int flag, int objectId)
    {
        s.Emit("CheckFlag", flag).Emit("GoToIf", ConditionEqual, "taken")
            .Emit("SetVar", SpecialVar0, g.Item).Emit("SetVar", SpecialVar1, g.Quantity)
            .Emit("HasSpaceForItem", SpecialVar0, SpecialVar1, ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "full")
            .Emit("SetFlag", flag).Emit("CallStd", GiveItemStd).Emit("CloseMsg");
        if (!g.Hidden) s.Emit("HidePerson", objectId);
        s.Label("taken").Emit("ReleaseAll").Emit("End");
        s.Label("full").Emit("CallStd", BagFullStd).Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
    }

    private static void BuildGuide(ScriptBuilder s, GuideBehavior g, IReadOnlyList<int> m)
    {
        for (int i = 0; i < g.Steps.Count; i++)
            s.Emit("CheckFlag", g.Steps[i].Flag).Emit("GoToIf", ConditionLess, $"step{i}");
        Say(s, m[g.Steps.Count]);
        for (int i = 0; i < g.Steps.Count; i++)
        {
            s.Label($"step{i}");
            Say(s, m[i]);
        }
    }

    private static bool IsNewKind(NpcBehavior b) => b is QuestBehavior or SellBehavior or TrainerBehavior or GroundItemBehavior or GuideBehavior or SceneBehavior;

    private const int FirstWalkMovement = 12, MovementEnd = 254, MaxSceneSteps = 32, MaxWalk = 32, MaxSceneWait = 600;
    private const int StarterChikorita = 152, StarterCyndaquil = 155;

    private static void BuildScene(ScriptBuilder s, SceneBehavior scene, IReadOnlyList<int> m, int flag, int objectId)
    {
        bool hides = scene.Steps.Any(step => step.Action == SceneAction.Hide);
        bool fights = scene.Steps.Any(step => step.Action == SceneAction.Battle);
        s.Emit("CheckFlag", flag).Emit("GoToIf", ConditionEqual, "done");
        // sem Pokémon para lutar, a batalha não pode começar
        if (fights) s.Emit("PartyCountNotEgg", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "done");
        var walks = new List<SceneStep>();
        int said = 0, fight = 0;
        foreach (var step in scene.Steps)
            switch (step.Action)
            {
                case SceneAction.Say:
                    s.Emit("NPCMsg", m[said++]).Emit("WaitButton").Emit("CloseMsg");
                    break;
                case SceneAction.Walk:
                    s.Emit("ApplyMovement", objectId, $"walk{walks.Count}").Emit("WaitMovement");
                    walks.Add(step);
                    break;
                case SceneAction.Wait:
                    s.Emit("Wait", step.Value, ResultVar);
                    break;
                case SceneAction.Face:
                    s.Emit("FacePlayer");
                    break;
                case SceneAction.Battle:
                    if (step.Cyndaquil > 0)
                    {
                        int n = fight++;
                        s.Emit("GetStarterChoice", ResultVar).Emit("CompareVarToValue", ResultVar, StarterChikorita).Emit("GoToIf", ConditionNotEqual, $"notChikorita{n}")
                            .Emit("TrainerBattle", step.Value, 0, 0, 0).Emit("GoTo", $"fought{n}")
                            .Label($"notChikorita{n}").Emit("CompareVarToValue", ResultVar, StarterCyndaquil).Emit("GoToIf", ConditionNotEqual, $"totodile{n}")
                            .Emit("TrainerBattle", step.Cyndaquil, 0, 0, 0).Emit("GoTo", $"fought{n}")
                            .Label($"totodile{n}").Emit("TrainerBattle", step.Totodile, 0, 0, 0)
                            .Label($"fought{n}");
                    }
                    else s.Emit("TrainerBattle", step.Value, 0, 0, 0);
                    s.Emit("CheckBattleWon", ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "lost");
                    break;
            }
        s.Emit("SetFlag", flag);
        if (hides) s.Emit("HidePerson", objectId);
        s.Emit("ReleaseAll").Emit("End");
        s.Label("lost").Emit("WhiteOut").Emit("ReleaseAll").Emit("End");
        s.Label("done");
        if (!hides && said > 0) s.Emit("NPCMsg", m[said - 1]).Emit("WaitButton").Emit("CloseMsg");
        s.Emit("ReleaseAll").Emit("End");
        for (int i = 0; i < walks.Count; i++)
            s.Data($"walk{i}", (ushort)(FirstWalkMovement + walks[i].Direction), (ushort)walks[i].Value, MovementEnd, 0);
    }

    /// <summary>Lê uma cena escrita uma ação por linha: say: fala, walk: down 3, wait: 30, face, battle: 760 e hide.</summary>
    public static List<SceneStep> ParseScene(string text)
    {
        var steps = new List<SceneStep>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            var verb = (colon < 0 ? line : line[..colon]).Trim().ToLowerInvariant();
            var rest = colon < 0 ? "" : line[(colon + 1)..].Trim();
            steps.Add(verb switch
            {
                "say" when rest.Length > 0 => new SceneStep(SceneAction.Say, rest),
                "walk" => ParseWalk(rest, line),
                "wait" when int.TryParse(rest, out int frames) => new SceneStep(SceneAction.Wait, Value: frames),
                "face" => new SceneStep(SceneAction.Face),
                "battle" => ParseBattle(rest, line),
                "hide" => new SceneStep(SceneAction.Hide),
                _ => throw new ArgumentException(Loc.T("core.newev.badSceneLine", line)),
            });
        }
        return steps;
    }

    private static SceneStep ParseBattle(string rest, string line)
    {
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ids = parts.Select(part => int.TryParse(part, out int id) ? id : -1).ToList();
        if (ids.Count is not (1 or 3) || ids.Contains(-1)) throw new ArgumentException(Loc.T("core.newev.badSceneLine", line));
        return ids.Count == 1 ? new SceneStep(SceneAction.Battle, Value: ids[0]) : new SceneStep(SceneAction.Battle, Value: ids[0], Cyndaquil: ids[1], Totodile: ids[2]);
    }

    private static SceneStep ParseWalk(string rest, string line)
    {
        var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int direction = parts.FirstOrDefault()?.ToLowerInvariant() switch { "up" => 0, "down" => 1, "left" => 2, "right" => 3, _ => -1 };
        if (direction < 0 || parts.Length != 2 || !int.TryParse(parts[1], out int count)) throw new ArgumentException(Loc.T("core.newev.badSceneLine", line));
        return new SceneStep(SceneAction.Walk, Value: count, Direction: direction);
    }

    /// <summary>O caminho de volta: a cena como texto, para mostrar na janela.</summary>
    public static string FormatScene(IEnumerable<SceneStep> steps, Func<string, string> unwrap) => string.Join("\n", steps.Select(step => step.Action switch
    {
        SceneAction.Say => $"say: {unwrap(step.Text ?? "")}",
        SceneAction.Walk => $"walk: {new[] { "up", "down", "left", "right" }[step.Direction]} {step.Value}",
        SceneAction.Wait => $"wait: {step.Value}",
        SceneAction.Face => "face",
        SceneAction.Battle => $"battle: {string.Join(' ', step.Trainers())}",
        _ => "hide",
    }));

    public static byte[] BuildScript(ScriptCommandTable table, NpcBehavior behavior, IReadOnlyList<int> messages, int? flag, int objectId = 0, int tradeSlot = 0, int? engageFlag = null, NpcCondition? condition = null)
    {
        var s = NewBuilder(table);
        bool platinum = IsPlatinum(table);
        if (IsNewKind(behavior) || condition is not null)
        {
            s.Emit("PlaySE", SelectSound).Emit("LockAll").Emit("FacePlayer");
            EmitConditionChecks(s, condition);
            switch (behavior)
            {
                case QuestBehavior q: BuildQuest(s, q, messages, flag!.Value); break;
                case SellBehavior v: BuildSell(s, v, messages, flag); break;
                case TrainerBehavior t: BuildTrainer(s, t, messages, flag!.Value, objectId); break;
                case GroundItemBehavior g: BuildGroundItem(s, g, flag!.Value, objectId); break;
                case GuideBehavior guide: BuildGuide(s, guide, messages); break;
                case SceneBehavior scene: BuildScene(s, scene, messages, flag!.Value, objectId); break;
                case TalkBehavior: Say(s, messages[0]); break;
                case GiveItemBehavior item:
                    s.Emit("CheckFlag", flag!.Value).Emit("GoToIf", ConditionEqual, "after")
                        .Emit("NPCMsg", messages[0])
                        .Emit("SetVar", SpecialVar0, item.Item).Emit("SetVar", SpecialVar1, item.Quantity)
                        .Emit("HasSpaceForItem", SpecialVar0, SpecialVar1, ResultVar).Emit("CompareVarToValue", ResultVar, 0).Emit("GoToIf", ConditionEqual, "full")
                        .Emit("CallStd", GiveItemStd).Emit("SetFlag", flag.Value)
                        .Label("after");
                    Say(s, messages[1]);
                    s.Label("full").Emit("CallStd", BagFullStd).Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
                    break;
                case TradeBehavior swap:
                    BuildTradeBody(s, tradeSlot, flag!.Value, messages, platinum, swap.MinLevel);
                    break;
                case PokemonBehavior mon:
                    EmitPokemonBody(s, mon, messages, flag, engageFlag, platinum, objectId);
                    break;
                default:
                    throw new ArgumentException(behavior.GetType().Name);
            }
            if (condition is not null)
            {
                s.Label("locked");
                Say(s, messages[^1]);
            }
            return s.Build();
        }
        if (behavior is TradeBehavior simple)
        {
            BuildTrade(s, tradeSlot, flag!.Value, messages, platinum, simple.MinLevel);
            return s.Build();
        }
        if (behavior is PokemonBehavior wild)
        {
            s.Emit("PlaySE", SelectSound).Emit("LockAll").Emit("FacePlayer");
            EmitPokemonBody(s, wild, messages, flag, engageFlag, platinum, objectId);
            return s.Build();
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

    private static void EmitPokemonBody(ScriptBuilder s, PokemonBehavior mon, IReadOnlyList<int> messages, int? flag, int? engageFlag, bool platinum, int objectId)
    {
        s.Emit("PlayCry", mon.Species, 0);
        bool talks = messages.Count > 0 && !string.IsNullOrEmpty(mon.Text);
        if (talks) s.Emit("NPCMsg", messages[0]);
        s.Emit("WaitCry");
        if (talks) s.Emit("CloseMsg");
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
        // no HG/SS, como nos lendários do jogo, só a captura resolve: nocauteado ou deixado para trás, volta quando o mapa recarregar. Quem bloqueia sai também ao ser vencido
        if (flag is not null && !platinum)
        {
            s.Emit("GetStaticEncounterOutcome", ResultVar);
            if (mon.Blocks) s.Emit("CompareVarToValue", ResultVar, OutcomeWin).Emit("GoToIf", ConditionEqual, "beaten");
            s.Emit("CompareVarToValue", ResultVar, OutcomeCaught).Emit("GoToIf", ConditionEqual, "beaten")
                .Emit("ReleaseAll").Emit("End")
                .Label("beaten");
        }
        if (flag is { } hide) s.Emit("SetFlag", hide);
        if (mon.Blocks) s.Emit("HidePerson", objectId);
        s.Emit("ReleaseAll").Emit("End")
            .Label("lost").Emit("WhiteOut").Emit("ReleaseAll").Emit("End")
            .Label("noTeam").Emit("ReleaseAll").Emit("End");
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
        if (existing.ObjectId >= 0 && events.Objects.All(o => o.Id != existing.ObjectId)) throw new InvalidDataException(Loc.T("core.newev.notFound"));

        // treinador com visão usa o script padrão do jogo: não há script próprio para esvaziar
        if (existing.ScriptId < TrainerStdScript) editor.ReplaceEntry(existing.ScriptId, NewBuilder(table).Emit("End").Build());
        if (existing.LevelScriptId is { } level) WriteLevelScript(editor, table, level, null, 0, existing.ChainScriptId);
        scriptsNarc.Files[header.ScriptsBank] = editor.Raw;
        eventsNarc.Files[header.EventsBank] = existing.ObjectId < 0
            ? EventFile.RemoveBg(eventsNarc.Files[header.EventsBank], existing.ScriptId)
            : EventFile.RemoveObject(eventsNarc.Files[header.EventsBank], existing.ObjectId);

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
        if (npc.Behavior is SceneBehavior && game.IsPlatinum) throw new ArgumentException(Loc.T("core.newev.badScene", MaxSceneSteps));
        if (npc.Behavior is PokemonBehavior { Blocks: true } && (game.IsPlatinum || npc.Behavior is PokemonBehavior { Vanish: false })) throw new ArgumentException(Loc.T("core.newev.badBlocks"));
        if (npc.Behavior is PokemonBehavior { Species: < 1 or > MaxSpecies }) throw new ArgumentException(Loc.T("core.newev.badSpecies"));
        if (npc.Behavior is PokemonBehavior mon && SpriteForSpecies(mon.Species, game.IsPlatinum) is null) throw new ArgumentException(Loc.T("core.newev.noFieldSprite"));
        if (npc.Behavior is TradeBehavior { AskSpecies: < 1 or > MaxSpecies } or TradeBehavior { GiveSpecies: < 1 or > MaxSpecies }) throw new ArgumentException(Loc.T("core.newev.badSpecies"));
        if (npc.Range is < 1 or > MaxRange) throw new ArgumentException(Loc.T("core.newev.badRange", MaxRange));
        if ((IsNewKind(npc.Behavior) || npc.Condition is not null) && !game.IsHgss) throw new NotSupportedException(Loc.T("core.newev.newKindsHgss"));
        ValidateNewKinds(npc);

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
        bool hiddenItem = npc.Behavior is GroundItemBehavior { Hidden: true };
        var current = existing is null || existing.ObjectId < 0 ? null : events.Objects.FirstOrDefault(o => o.Id == existing.ObjectId) ?? throw new InvalidDataException(Loc.T("core.newev.notFound"));

        if (events.Objects.Any(o => o.X == npc.X && o.Z == npc.Z && o.Id != current?.Id)) warnings.Add(Loc.T("core.newev.occupied"));
        if (existing is not null && (existing.ObjectId < 0) != hiddenItem) throw new ArgumentException(Loc.T("core.newev.groundSwitch"));
        if (existing is null && events.Objects.Count >= NpcWarningCount) warnings.Add(Loc.T("core.newev.tooMany", NpcWarningCount));
        var sharing = map.Headers.Where(h => h.Id != npc.MapId && (h.ScriptsBank == header.ScriptsBank || h.EventsBank == header.EventsBank)).Select(h => h.InternalName).Distinct().ToList();
        if (sharing.Count > 0) warnings.Add(Loc.T("core.newev.shared", string.Join(", ", sharing.Take(4))));
        if (map.LayoutOf(npc.MapId) is { } layout)
        {
            int lx = npc.X - layout.OriginX, ly = npc.Z - layout.OriginY;
            if (lx >= 0 && ly >= 0 && lx < layout.Width && ly < layout.Height && layout.IsBlocked(lx, ly)) warnings.Add(Loc.T("core.newev.blocked"));
        }

        var sighted = npc.Behavior as TrainerBehavior is { Sight: > 0 } seeing ? seeing : null;
        int trainerCount = Narc.Read(File.ReadAllBytes(game.TrainerDataPath)).Files.Count;
        foreach (var id in TrainersOf(npc.Behavior))
            if (id < 1 || id >= trainerCount) throw new ArgumentException(Loc.T("core.newev.badTrainer", id, trainerCount - 1));
        if (sighted is not null && events.Objects.Any(o => o.Id != current?.Id && o.ScriptId == TrainerScriptOf(sighted.Trainer))) warnings.Add(Loc.T("core.newev.sharedTrainer"));
        if (sighted is not null && game.IsHgss && sighted.Trainer == HgssApproachTrainer) throw new ArgumentException(Loc.T("core.newev.approachTrainer", HgssApproachTrainer));
        bool needsFlag = sighted is null && npc.Behavior is GiveItemBehavior or TradeBehavior or PokemonBehavior { Vanish: true } or QuestBehavior or SellBehavior { Once: true } or TrainerBehavior or GroundItemBehavior or SceneBehavior;
        bool vanishes = npc.Behavior is PokemonBehavior { Vanish: true, Blocks: false };
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
            // sem o {YESNO 0} na fala, o HG/SS não abre o menu de Sim e Não; o Platinum tem comando próprio
            TradeBehavior { MinLevel: > 0 } t => [WithYesNo(t.AskText), t.ThanksText, t.WrongText, t.DeclineText, t.TooLowText ?? ""],
            TradeBehavior t => [game.IsHgss ? WithYesNo(t.AskText) : t.AskText, t.ThanksText, t.WrongText, t.DeclineText],
            GuideBehavior g => [.. g.Steps.Select(step => step.Text), g.FinalText],
            SceneBehavior scene => [.. scene.Steps.Where(step => step.Action == SceneAction.Say).Select(step => step.Text ?? "")],
            QuestBehavior q => [EndWithWait(q.AskText), q.NotYetText, EndWithWait(q.RewardText), q.DoneText, q.FullText],
            SellBehavior v => [WithYesNo(v.OfferText), v.ThanksText, v.NoMoneyText, v.DeclineText, v.FullText, v.SoldOutText],
            TrainerBehavior { Sight: > 0 } => [],
            TrainerBehavior t => t.Rematch != TrainerRematch.None && !t.Vanish
                ? [EndWithWait(t.ChallengeText), t.BeatenText, t.AfterText, WithYesNo(t.RematchText ?? "")]
                : [EndWithWait(t.ChallengeText), t.BeatenText, t.AfterText],
            _ => [],
        };
        if (npc.Condition is { } condition && sighted is null) lines = [.. lines, condition.Text];
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

        // falas do próprio registro: com visão, são as que o jogo mostra; sem visão, a de derrota aparece no fim da batalha e não pode ficar vazia
        TrainerMessages? trainerMessages = null;
        // a fala de derrota aparece no fim da batalha e não pode ficar vazia: a cena usa a fala seguinte
        if (npc.Behavior is SceneBehavior scenic && game.IsHgss && !game.Japanese && scenic.Steps.Any(step => step.Action == SceneAction.Battle))
        {
            trainerMessages = TrainerMessages.Load(project);
            for (int i = 0; i < scenic.Steps.Count; i++)
            {
                var next = scenic.Steps.Skip(i + 1).FirstOrDefault(step => step.Action == SceneAction.Say)?.Text ?? scenic.Steps.Take(i).LastOrDefault(step => step.Action == SceneAction.Say)?.Text ?? "...";
                foreach (var id in scenic.Steps[i].Trainers())
                    if (!trainerMessages.Has(id, TrainerMessages.Lose)) trainerMessages.Set(texts, id, new Dictionary<int, string> { [TrainerMessages.Lose] = next });
            }
        }
        if (npc.Behavior is TrainerBehavior spoken && game.IsHgss && !game.Japanese)
        {
            trainerMessages = TrainerMessages.Load(project);
            var said = new Dictionary<int, string>();
            if (spoken.Sight > 0)
            {
                if (!string.IsNullOrWhiteSpace(spoken.ChallengeText)) said[TrainerMessages.Intro] = spoken.ChallengeText;
                if (!string.IsNullOrWhiteSpace(spoken.BeatenText)) said[TrainerMessages.Lose] = spoken.BeatenText;
                if (!string.IsNullOrWhiteSpace(spoken.AfterText)) said[TrainerMessages.After] = spoken.AfterText;
            }
            else if (!trainerMessages.Has(spoken.Trainer, TrainerMessages.Lose) && !string.IsNullOrWhiteSpace(spoken.BeatenText))
                said[TrainerMessages.Lose] = spoken.BeatenText;
            if (said.Count > 0) trainerMessages.Set(texts, spoken.Trainer, said);
            else trainerMessages = null;
        }

        var table = TableOf(game);
        int objectId = current?.Id ?? (events.Objects.Count == 0 ? 0 : events.Objects.Max(o => o.Id) + 1);
        var code = sighted is null ? BuildScript(table, npc.Behavior, messages, flag, objectId, tradeSlot ?? 0, engage, npc.Condition) : [];

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
        if (sighted is not null)
        {
            scriptId = TrainerScriptOf(sighted.Trainer);
            if (game.IsHgss)
            {
                int ask = CommonText(texts, WithYesNo(sighted.RematchText ?? DefaultRematchText));
                scriptsNarc.Files[HgssTrainerScriptFile] = WithTrainerTalk(scriptsNarc.Files[HgssTrainerScriptFile], table, sighted.Trainer, ask);
            }
        }
        else if (existing is null || scriptId >= TrainerStdScript) scriptId = editor.AddEntry(code);
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

        int sprite = npc.Behavior is GroundItemBehavior ? ItemBallSprite : npc.Behavior is PokemonBehavior p ? SpriteForSpecies(p.Species, game.IsPlatinum) ?? npc.SpriteId : npc.SpriteId;
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
            Type: sighted is not null ? 1 : 0,
            // a flag do Pokémon que some esconde o objeto; a do item só marca a entrega, e o NPC continua lá
            EventFlag: npc.Behavior is PokemonBehavior { Vanish: true } or TrainerBehavior { Vanish: true, Sight: 0 } or GroundItemBehavior || (npc.Behavior is SceneBehavior played && played.Steps.Any(step => step.Action == SceneAction.Hide)) ? flag!.Value : 0,
            ScriptId: scriptId,
            Facing: npc.Facing,
            X: npc.X,
            Z: npc.Z,
            Y: current is not null && current.X == npc.X && current.Z == npc.Z ? current.Y : HeightFor(events, npc.X, npc.Z),
            XRange: npc.Movement is NpcMovement.Wander or NpcMovement.LeftRight ? npc.Range : 0,
            ZRange: npc.Movement is NpcMovement.Wander or NpcMovement.UpDown ? npc.Range : 0,
            Sight: sighted?.Sight ?? 0);
        if (hiddenItem)
        {
            // escondido não é objeto: é um evento de tile, e o número negativo do script o identifica na lista
            objectId = -scriptId;
            var spot = new BgEvent(0, scriptId, BgNormal, npc.X, npc.Z, 0, BgAnyDirection);
            eventsNarc.Files[header.EventsBank] = existing is null ? EventFile.AddBg(eventsNarc.Files[header.EventsBank], spot) : EventFile.MoveBg(eventsNarc.Files[header.EventsBank], spot);
        }
        else
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
        trainerMessages?.Save();
        ledger.Save(project);
        return new NewNpcResult(updated.Index, scriptId, flag, warnings);
    }

    /// <summary>Nos NPCs do jogo, 14 a 17 olham fixo para cima, baixo, esquerda e direita.</summary>
    private const int FixedFacingMovement = 14, MovementLookAround = 2, MovementWander = 3;
    /// <summary>Sprite de Poké Bola dos itens do jogo; evento de tile comum, que aceita o jogador de qualquer lado.</summary>
    private const int ItemBallSprite = 87, BgNormal = 0, BgAnyDirection = 4, MaxGuideSteps = 8;
    /// <summary>Tipo de entrada do cabeçalho do mapa que os encontros parados do jogo usam, nos dois jogos.</summary>
    private const int ReturnScriptKind = 4;
    private const int JumpSize = 6;
    private const string TradesRelativePath = @"a\1\1\2", PlatinumTradesRelativePath = @"fielddata\pokemon_trade\fld_trade.narc";

    private static string WithYesNo(string text) => text.Contains("{YESNO", StringComparison.Ordinal) ? text : text + YesNoPrompt;

    private static IEnumerable<int> TrainersOf(NpcBehavior b)
    {
        if (b is SceneBehavior scene)
        {
            foreach (var id in scene.Steps.SelectMany(step => step.Trainers())) yield return id;
            yield break;
        }
        if (b is not TrainerBehavior t) yield break;
        yield return t.Trainer;
        if (t.RematchTrainer is { } rematch) yield return rematch;
    }

    private static void ValidateNewKinds(NewNpc npc)
    {
        static bool Species(int? s) => s is >= 1 and <= MaxSpecies;
        switch (npc.Behavior)
        {
            case TradeBehavior swap:
                if (swap.MinLevel is < 0 or > 100) throw new ArgumentException(Loc.T("core.newev.badLevel"));
                if (swap.MinLevel > 0 && string.IsNullOrWhiteSpace(swap.TooLowText)) throw new ArgumentException(Loc.T("core.newev.tooLowText"));
                break;
            case GroundItemBehavior g:
                if (g.Quantity is < 1 or > 99) throw new ArgumentException(Loc.T("core.newev.badQuantity"));
                break;
            case GuideBehavior guide:
                if (guide.Steps.Count is < 1 or > MaxGuideSteps || guide.Steps.Any(step => step.Flag <= 0 || string.IsNullOrWhiteSpace(step.Text)) || string.IsNullOrWhiteSpace(guide.FinalText))
                    throw new ArgumentException(Loc.T("core.newev.badGuide", MaxGuideSteps));
                break;
            case QuestBehavior q:
                if (q.AskItem is not null && q.AskSpecies is not null) throw new ArgumentException(Loc.T("core.newev.questAsk"));
                if (q.AskItem is not null && q.AskQuantity is < 1 or > 99) throw new ArgumentException(Loc.T("core.newev.badQuantity"));
                if (q.AskSpecies is not null && !Species(q.AskSpecies)) throw new ArgumentException(Loc.T("core.newev.badSpecies"));
                if (q.RewardItem is null && q.RewardSpecies is null && q.RewardMoney <= 0) throw new ArgumentException(Loc.T("core.newev.questReward"));
                if (q.RewardItem is not null && q.RewardQuantity is < 1 or > 99) throw new ArgumentException(Loc.T("core.newev.badQuantity"));
                if (q.RewardSpecies is not null && (!Species(q.RewardSpecies) || q.RewardLevel is < 1 or > 100)) throw new ArgumentException(Loc.T("core.newev.badLevel"));
                if (q.RewardMoney is < 0 or > MaxMoney) throw new ArgumentException(Loc.T("core.newev.badMoney", MaxMoney));
                break;
            case SceneBehavior scene:
                if (scene.Steps.Count is 0 or > MaxSceneSteps || scene.Steps.Any(step =>
                        (step.Action == SceneAction.Say && string.IsNullOrWhiteSpace(step.Text))
                        || (step.Action == SceneAction.Walk && (step.Value is < 1 or > MaxWalk || step.Direction is < 0 or > 3))
                        || (step.Action == SceneAction.Wait && step.Value is < 1 or > MaxSceneWait)))
                    throw new ArgumentException(Loc.T("core.newev.badScene", MaxSceneSteps));
                break;
            case SellBehavior v:
                if (!Species(v.Species)) throw new ArgumentException(Loc.T("core.newev.badSpecies"));
                if (v.Level is < 1 or > 100) throw new ArgumentException(Loc.T("core.newev.badLevel"));
                if (v.Price is < 1 or > MaxMoney) throw new ArgumentException(Loc.T("core.newev.badMoney", MaxMoney));
                break;
            case TrainerBehavior t:
                if (t.Sight is < 0 or > MaxSight) throw new ArgumentException(Loc.T("core.newev.badSight", MaxSight));
                if (t.Sight > 0 && npc.Condition is not null) throw new ArgumentException(Loc.T("core.newev.sightCondition"));
                if (t.Rematch != TrainerRematch.None && !t.Vanish && t.Sight == 0 && string.IsNullOrWhiteSpace(t.RematchText)) throw new ArgumentException(Loc.T("core.newev.rematchText"));
                break;
        }
        if (npc.Condition is { } c)
        {
            if (string.IsNullOrWhiteSpace(c.Text)) throw new ArgumentException(Loc.T("core.newev.conditionText"));
            if (c.MinBadges is < 0 or > MaxBadges) throw new ArgumentException(Loc.T("core.newev.badBadges", MaxBadges));
            if (c.Badge is < 0 or >= MaxBadges) throw new ArgumentException(Loc.T("core.newev.badBadges", MaxBadges));
        }
    }

    /// <summary>O dinheiro do jogador vai até 999.999.</summary>
    private const int MaxMoney = 999999;

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
    public bool Blocks { get; set; }
    public int AskSpecies { get; set; }
    public int GiveSpecies { get; set; }
    public string? Nickname { get; set; }
    public int AskItem { get; set; }
    public int AskQuantity { get; set; }
    public bool TakeItem { get; set; }
    public int RewardItem { get; set; }
    public int RewardQuantity { get; set; }
    public int RewardSpecies { get; set; }
    public int RewardLevel { get; set; }
    public int RewardMoney { get; set; }
    public int Price { get; set; }
    public bool Once { get; set; }
    public int Trainer { get; set; }
    public int RematchTrainer { get; set; }
    public TrainerRematch Rematch { get; set; }
    public int Sight { get; set; }
    public bool ConditionAfterElite { get; set; }
    public int ConditionMinBadges { get; set; }
    public int? ConditionBadge { get; set; }
    public int? ConditionAfterFlag { get; set; }
    public int ConditionMaxPartyLevel { get; set; }
    public int MinLevel { get; set; }
    public bool Hidden { get; set; }
    public List<int> GuideFlags { get; set; } = [];
    public List<SceneStep> Scene { get; set; } = [];
    public string? ConditionText { get; set; }

    public NpcBehavior ToBehavior() => Kind switch
    {
        "item" => new GiveItemBehavior(Item, Quantity, Texts[0], Texts[1]),
        "pokemon" => new PokemonBehavior(Species, Level, Vanish, Texts.FirstOrDefault(), Blocks),
        "trade" => new TradeBehavior(AskSpecies, GiveSpecies, Nickname, Texts[0], Texts[1], Texts[2], Texts[3], MinLevel, Texts.ElementAtOrDefault(4)),
        "ground" => new GroundItemBehavior(Item, Quantity, Hidden),
        "guide" => new GuideBehavior(GuideFlags.Select((flag, i) => new GuideStep(flag, Texts[i])).ToList(), Texts[^1]),
        "scene" => new SceneBehavior(Scene),
        "quest" => new QuestBehavior(AskItem > 0 ? AskItem : null, AskQuantity, TakeItem, AskSpecies > 0 ? AskSpecies : null, RewardItem > 0 ? RewardItem : null, RewardQuantity,
            RewardSpecies > 0 ? RewardSpecies : null, RewardLevel, RewardMoney, Texts[0], Texts[1], Texts[2], Texts[3], Texts[4]),
        "sell" => new SellBehavior(Species, Level, Price, Once, Texts[0], Texts[1], Texts[2], Texts[3], Texts[4], Texts[5]),
        "trainer" => new TrainerBehavior(Trainer, Texts.ElementAtOrDefault(0) ?? "", Texts.ElementAtOrDefault(1) ?? "", Texts.ElementAtOrDefault(2) ?? "", Sight, Vanish, Rematch,
            RematchTrainer > 0 ? RematchTrainer : null, Texts.ElementAtOrDefault(3)),
        _ => new TalkBehavior(Texts[0]),
    };

    public NpcCondition? ToCondition() => ConditionText is { Length: > 0 } text ? new NpcCondition(text, ConditionAfterElite, ConditionMinBadges, ConditionBadge, ConditionAfterFlag, ConditionMaxPartyLevel) : null;

    public NewNpc ToNewNpc() => new(MapId, X, Z, SpriteId, Facing, ToBehavior(), Movement, Range, ToCondition());

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
            case PokemonBehavior p: c.Kind = "pokemon"; c.Species = p.Species; c.Level = p.Level; c.Vanish = p.Vanish; c.Blocks = p.Blocks; c.Texts = p.Text is { Length: > 0 } text ? [text] : []; break;
            case GroundItemBehavior g: c.Kind = "ground"; c.Item = g.Item; c.Quantity = g.Quantity; c.Hidden = g.Hidden; c.Texts = []; break;
            case SceneBehavior scene: c.Kind = "scene"; c.Scene = [.. scene.Steps]; c.Texts = [.. scene.Steps.Where(step => step.Action == SceneAction.Say).Select(step => step.Text ?? "")]; break;
            case GuideBehavior guide: c.Kind = "guide"; c.GuideFlags = guide.Steps.Select(step => step.Flag).ToList(); c.Texts = [.. guide.Steps.Select(step => step.Text), guide.FinalText]; break;
            case TradeBehavior { MinLevel: > 0 } t: c.Kind = "trade"; c.AskSpecies = t.AskSpecies; c.GiveSpecies = t.GiveSpecies; c.Nickname = t.Nickname; c.MinLevel = t.MinLevel; c.Texts = [t.AskText, t.ThanksText, t.WrongText, t.DeclineText, t.TooLowText ?? ""]; break;
            case TradeBehavior t: c.Kind = "trade"; c.AskSpecies = t.AskSpecies; c.GiveSpecies = t.GiveSpecies; c.Nickname = t.Nickname; c.Texts = [t.AskText, t.ThanksText, t.WrongText, t.DeclineText]; break;
            case QuestBehavior q:
                c.Kind = "quest"; c.AskItem = q.AskItem ?? 0; c.AskQuantity = q.AskQuantity; c.TakeItem = q.TakeItem; c.AskSpecies = q.AskSpecies ?? 0;
                c.RewardItem = q.RewardItem ?? 0; c.RewardQuantity = q.RewardQuantity; c.RewardSpecies = q.RewardSpecies ?? 0; c.RewardLevel = q.RewardLevel; c.RewardMoney = q.RewardMoney;
                c.Texts = [q.AskText, q.NotYetText, q.RewardText, q.DoneText, q.FullText];
                break;
            case SellBehavior v:
                c.Kind = "sell"; c.Species = v.Species; c.Level = v.Level; c.Price = v.Price; c.Once = v.Once;
                c.Texts = [v.OfferText, v.ThanksText, v.NoMoneyText, v.DeclineText, v.FullText, v.SoldOutText];
                break;
            case TrainerBehavior t:
                c.Kind = "trainer"; c.Trainer = t.Trainer; c.Sight = t.Sight; c.Vanish = t.Vanish; c.Rematch = t.Rematch; c.RematchTrainer = t.RematchTrainer ?? 0;
                c.Texts = t.Rematch != TrainerRematch.None && !t.Vanish ? [t.ChallengeText, t.BeatenText, t.AfterText, t.RematchText ?? ""] : [t.ChallengeText, t.BeatenText, t.AfterText];
                break;
        }
        if (npc.Condition is { } condition)
        {
            c.ConditionText = condition.Text; c.ConditionAfterElite = condition.AfterElite; c.ConditionMinBadges = condition.MinBadges;
            c.ConditionBadge = condition.Badge; c.ConditionAfterFlag = condition.AfterFlag; c.ConditionMaxPartyLevel = condition.MaxPartyLevel;
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
