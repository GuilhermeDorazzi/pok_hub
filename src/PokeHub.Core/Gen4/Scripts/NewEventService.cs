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

/// <param name="X">Tile na matriz inteira, como guardado no arquivo de eventos.</param>
/// <param name="Facing">0 cima, 1 baixo, 2 esquerda, 3 direita.</param>
public sealed record NewNpc(int MapId, int X, int Z, int SpriteId, int Facing, NpcBehavior Behavior);

public sealed record NewNpcResult(int ObjectIndex, int ScriptId, int? Flag, IReadOnlyList<string> Warnings);

/// <summary>Monta um script novo: comandos por nome, com rótulos para os saltos dentro dele.</summary>
public sealed class ScriptBuilder(ScriptCommandTable table)
{
    private readonly MemoryStream _code = new();
    private readonly Dictionary<string, int> _labels = [];
    private readonly List<(int At, string Label)> _jumps = [];

    public ScriptBuilder Emit(string command, params object[] args)
    {
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
            else _code.Write(BitConverter.GetBytes(Convert.ToInt64(args[i])).AsSpan(0, size));
        }
        return this;
    }

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
/// O script segue o formato dos NPCs do próprio jogo. Por enquanto só HG/SS: nomes de comandos e faixas de flags mudam no Platinum.
/// </summary>
public static class NewEventService
{
    /// <summary>O mapa mais cheio do SoulSilver tem 51 NPCs. Passar disso é terreno não testado.</summary>
    public const int NpcWarningCount = 51;
    private const int SelectSoundCommandSeen = 609, SpecialVar0 = 0x8004, SpecialVar1 = 0x8005, ResultVar = 0x800C;
    private const int GiveItemStd = 2033, BagFullStd = 2009, ConditionEqual = 1, SelectSound = 1500, MaxSpecies = 493;
    /// <summary>Flags de treinador começam aqui e vão até o último treinador; as do sistema começam em 0x960.</summary>
    private const int TrainerFlagBase = 0x550, SystemFlagBase = 0x960;

    public static bool Supports(Gen4Game game) => game.IsHgss;

    /// <summary>A flag livre mais alta da faixa segura: entre as de treinador e as do sistema, sem uso em script nenhum.</summary>
    public static int? PickFreeFlag(EventMap map, int trainerCount)
    {
        int lowest = TrainerFlagBase + trainerCount;
        foreach (var range in map.FreeFlags().OrderByDescending(r => r.To))
        {
            int top = Math.Min(range.To, SystemFlagBase - 1);
            if (top >= Math.Max(range.From, lowest)) return top;
        }
        return null;
    }

    /// <summary>Altura do NPC: a do objeto mais próximo, porque o terreno 3D não está lido. Sem vizinho, usa uma placa ou 0.</summary>
    public static int HeightFor(EventFile events, int x, int z)
    {
        if (events.Objects.Count > 0)
            return events.Objects.OrderBy(o => Math.Abs(o.X - x) + Math.Abs(o.Z - z)).First().Y;
        return events.Bg.Count > 0 ? events.Bg.OrderBy(b => Math.Abs(b.X - x) + Math.Abs(b.Z - z)).First().Y : 0;
    }

    private static readonly Lazy<Dictionary<int, int>> Followers = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("follower-sprites-hgss.json")
            ?? throw new InvalidOperationException("follower-sprites-hgss.json embutido não encontrado.");
        using var doc = JsonDocument.Parse(stream);
        return doc.RootElement.GetProperty("sprites").EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => p.Value.GetInt32());
    });

    /// <summary>Sprite de campo da espécie, o mesmo do Pokémon que segue o jogador no HG/SS. Null se não houver.</summary>
    public static int? SpriteForSpecies(int species) => Followers.Value.TryGetValue(species, out var sprite) ? sprite : null;

    public static byte[] BuildScript(ScriptCommandTable table, NpcBehavior behavior, IReadOnlyList<int> messages, int? flag, int objectId = 0)
    {
        var s = new ScriptBuilder(table);
        if (behavior is PokemonBehavior mon)
        {
            s.Emit("PlaySE", SelectSound).Emit("LockAll").Emit("FacePlayer").Emit("PlayCry", mon.Species, 0);
            if (messages.Count > 0) s.Emit("NPCMsg", messages[0]);
            s.Emit("WaitCry");
            if (messages.Count > 0) s.Emit("CloseMsg");
            s.Emit("WildBattle", mon.Species, mon.Level, 0)
                .Emit("CheckBattleWon", ResultVar)
                .Emit("CompareVarToValue", ResultVar, 0)
                .Emit("GoToIf", ConditionEqual, "lost");
            if (flag is { } hide) s.Emit("SetFlag", hide).Emit("HidePerson", objectId);
            return s.Emit("ReleaseAll").Emit("End")
                .Label("lost").Emit("WhiteOut").Emit("ReleaseAll").Emit("End")
                .Build();
        }
        s.Emit($"ScrCmd_{SelectSoundCommandSeen}").Emit("LockAll").Emit("FacePlayer");
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
                    .Emit("CallStd", GiveItemStd)
                    .Emit("SetFlag", flag.Value)
                    .Label("after")
                    .Emit("NPCMsg", messages[1]).Emit("WaitButton").Emit("CloseMsg").Emit("ReleaseAll").Emit("End")
                    .Label("full")
                    .Emit("CallStd", BagFullStd).Emit("CloseMsg").Emit("ReleaseAll").Emit("End");
                break;
            default:
                throw new ArgumentException(behavior.GetType().Name);
        }
        return s.Build();
    }

    /// <summary>Valida tudo antes de gravar. Avisos não impedem a criação; erros viram exceção.</summary>
    public static NewNpcResult Create(Project project, EventMap map, NewNpc npc)
    {
        var game = Gen4Game.For(project);
        if (!Supports(game)) throw new NotSupportedException(Loc.T("core.newev.hgssOnly"));
        var header = map.Headers.FirstOrDefault(h => h.Id == npc.MapId) ?? throw new ArgumentException(Loc.T("core.newev.noMap", npc.MapId));
        if (header.ScriptsBank < 0 || header.MsgBank < 0 || header.EventsBank < 0) throw new InvalidDataException(Loc.T("core.newev.noFiles", header.InternalName));
        if (npc.Facing is < 0 or > 3) throw new ArgumentException(Loc.T("core.newev.badFacing"));
        if (npc.Behavior is GiveItemBehavior { Quantity: < 1 or > 99 }) throw new ArgumentException(Loc.T("core.newev.badQuantity"));
        if (npc.Behavior is PokemonBehavior { Level: < 1 or > 100 }) throw new ArgumentException(Loc.T("core.newev.badLevel"));
        if (npc.Behavior is PokemonBehavior { Species: < 1 or > MaxSpecies }) throw new ArgumentException(Loc.T("core.newev.badSpecies"));

        var warnings = new List<string>();
        var eventsNarc = Narc.Read(File.ReadAllBytes(game.EventsPath));
        var scriptsNarc = Narc.Read(File.ReadAllBytes(game.ScriptsPath));
        var events = new EventFile(eventsNarc.Files[header.EventsBank]);

        if (events.Objects.Any(o => o.X == npc.X && o.Z == npc.Z)) warnings.Add(Loc.T("core.newev.occupied"));
        if (events.Objects.Count >= NpcWarningCount) warnings.Add(Loc.T("core.newev.tooMany", NpcWarningCount));
        var sharing = map.Headers.Where(h => h.Id != npc.MapId && (h.ScriptsBank == header.ScriptsBank || h.EventsBank == header.EventsBank)).Select(h => h.InternalName).Distinct().ToList();
        if (sharing.Count > 0) warnings.Add(Loc.T("core.newev.shared", string.Join(", ", sharing.Take(4))));
        if (map.LayoutOf(npc.MapId) is { } layout)
        {
            int lx = npc.X - layout.OriginX, ly = npc.Z - layout.OriginY;
            if (lx >= 0 && ly >= 0 && lx < layout.Width && ly < layout.Height && layout.IsBlocked(lx, ly)) warnings.Add(Loc.T("core.newev.blocked"));
        }

        int? flag = null;
        if (npc.Behavior is GiveItemBehavior or PokemonBehavior { Vanish: true })
        {
            int trainers = Narc.Read(File.ReadAllBytes(game.TrainerDataPath)).Files.Count;
            flag = PickFreeFlag(map, trainers) ?? throw new InvalidDataException(Loc.T("core.newev.noFlag"));
        }

        // texto primeiro: é o que recusa caractere inválido, antes de qualquer gravação
        var texts = TextRepository.Load(project);
        string[] lines = npc.Behavior switch
        {
            TalkBehavior t => [t.Text],
            GiveItemBehavior i => [EndWithWait(i.BeforeText), i.AfterText],
            PokemonBehavior { Text: { Length: > 0 } text } => [EndWithWait(text)],
            _ => [],
        };
        var messages = lines.Select(text => texts.AddText(header.MsgBank, text)).ToList();

        var table = ScriptCommandTable.Hgss;
        int objectId = events.Objects.Count == 0 ? 0 : events.Objects.Max(o => o.Id) + 1;
        var code = BuildScript(table, npc.Behavior, messages, flag, objectId);
        var editor = new ScriptEditor(scriptsNarc.Files[header.ScriptsBank], table);
        int scriptId = editor.AddEntry(code);
        scriptsNarc.Files[header.ScriptsBank] = editor.Raw;

        int index = events.Objects.Count;
        eventsNarc.Files[header.EventsBank] = EventFile.AddObject(eventsNarc.Files[header.EventsBank], new ObjectEvent(index,
            Id: objectId,
            SpriteId: npc.Behavior is PokemonBehavior p ? SpriteForSpecies(p.Species) ?? npc.SpriteId : npc.SpriteId,
            Movement: FixedFacingMovement + npc.Facing,
            Type: 0,
            // a flag do Pokémon que some esconde o objeto; a do item só marca a entrega, e o NPC continua lá
            EventFlag: npc.Behavior is PokemonBehavior && flag is { } hidden ? hidden : 0,
            ScriptId: scriptId,
            Facing: npc.Facing,
            X: npc.X,
            Z: npc.Z,
            Y: HeightFor(events, npc.X, npc.Z)));

        texts.Save();
        WriteAtomic(game.ScriptsPath, scriptsNarc.Write());
        WriteAtomic(game.EventsPath, eventsNarc.Write());
        return new NewNpcResult(index, scriptId, flag, warnings);
    }

    /// <summary>Nos NPCs do jogo, 14 a 17 olham fixo para cima, baixo, esquerda e direita.</summary>
    private const int FixedFacingMovement = 14;

    /// <summary>Sem esperar o botão, o próximo comando apagaria a fala antes de dar para ler.</summary>
    private static string EndWithWait(string text) => text.EndsWith("\\r", StringComparison.Ordinal) ? text : text + "\\r";

    private static void WriteAtomic(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }
}
