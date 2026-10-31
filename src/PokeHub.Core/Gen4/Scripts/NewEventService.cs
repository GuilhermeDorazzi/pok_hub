using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>O que o NPC novo faz ao falar com ele.</summary>
public abstract record NpcBehavior;
public sealed record TalkBehavior(string Text) : NpcBehavior;
/// <summary>Dá o item uma vez só: depois repete apenas a fala final.</summary>
public sealed record GiveItemBehavior(int Item, int Quantity, string BeforeText, string AfterText) : NpcBehavior;

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
    private const int GiveItemStd = 2033, BagFullStd = 2009, ConditionEqual = 1;
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

    public static byte[] BuildScript(ScriptCommandTable table, NpcBehavior behavior, IReadOnlyList<int> messages, int? flag)
    {
        var s = new ScriptBuilder(table);
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
        if (npc.Behavior is GiveItemBehavior)
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
            _ => [],
        };
        var messages = lines.Select(text => texts.AddText(header.MsgBank, text)).ToList();

        var table = ScriptCommandTable.Hgss;
        var code = BuildScript(table, npc.Behavior, messages, flag);
        var editor = new ScriptEditor(scriptsNarc.Files[header.ScriptsBank], table);
        int scriptId = editor.AddEntry(code);
        scriptsNarc.Files[header.ScriptsBank] = editor.Raw;

        int index = events.Objects.Count;
        eventsNarc.Files[header.EventsBank] = EventFile.AddObject(eventsNarc.Files[header.EventsBank], new ObjectEvent(index,
            Id: events.Objects.Count == 0 ? 0 : events.Objects.Max(o => o.Id) + 1,
            SpriteId: npc.SpriteId,
            Movement: FixedFacingMovement + npc.Facing,
            Type: 0,
            EventFlag: 0,
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
