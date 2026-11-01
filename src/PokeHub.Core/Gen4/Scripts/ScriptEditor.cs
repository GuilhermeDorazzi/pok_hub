using PokeHub.Core.Localization;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>
/// Insere e remove comandos num arquivo de scripts, recalculando a tabela de entradas e todos os saltos.
/// Só o que a desmontagem alcança é conhecido: bytes que nada referencia são copiados como estão, e por isso o desenho mexe só em posições de comando.
/// </summary>
public sealed class ScriptEditor
{
    private const int HeaderEntrySize = 4, MovementAlignment = 4;

    private sealed record Splice(int At, int Remove, byte[] Insert, bool TargetAfter = false);
    private sealed record Reference(int At, int Target);

    public byte[] Raw { get; private set; }
    public ScriptCommandTable Table { get; }
    public ScriptFile File { get; private set; }

    public ScriptEditor(byte[] raw, ScriptCommandTable table)
    {
        Raw = raw;
        Table = table;
        File = new ScriptFile(raw, table);
    }

    /// <summary>Instruções alcançadas a partir das entradas, em ordem de posição.</summary>
    public List<ScriptInstruction> Instructions()
    {
        var found = new Dictionary<int, ScriptInstruction>();
        for (int id = 1; id <= File.EntryOffsets.Count; id++)
            foreach (var i in File.Routine(id)!.Instructions) found[i.Offset] = i;
        return found.Values.OrderBy(i => i.Offset).ToList();
    }

    /// <summary>Os bytes de um comando. Saltos recebem a posição absoluta no arquivo atual; o relativo é calculado ao gravar.</summary>
    public byte[] Encode(int opcode, IReadOnlyList<long> args, out List<(int At, int Target)> jumps)
    {
        var command = Table.Get(opcode) ?? throw new ArgumentException(Loc.T("core.script.noCommand", opcode));
        if (args.Count != command.Args.Count) throw new ArgumentException(Loc.T("core.script.argCount", command.Name, command.Args.Count));
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((ushort)opcode));
        jumps = [];
        for (int i = 0; i < args.Count; i++)
        {
            int size = ScriptCommandTable.ArgSize(command.Args[i]);
            var type = ScriptCommandTable.ArgType(command.Args[i]);
            long max = size >= 4 ? uint.MaxValue : (1L << (size * 8)) - 1;
            if (type is "script" or "movement" or "addr")
            {
                if (args[i] < File.HeaderEnd || args[i] >= Raw.Length) throw new ArgumentException(Loc.T("core.script.badTarget", $"0x{args[i]:X}"));
                jumps.Add(((int)ms.Length, (int)args[i]));
                ms.Write(new byte[4]);
                continue;
            }
            if (args[i] < 0 || args[i] > max) throw new ArgumentException(Loc.T("core.script.argRange", i + 1, max));
            ms.Write(BitConverter.GetBytes(args[i]).AsSpan(0, size));
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Acrescenta um script novo no fim do arquivo e devolve o número dele, o que os eventos usam.
    /// A tabela ganha 4 bytes: o código antigo anda junto, então só as entradas antigas precisam ser ajustadas, e os saltos continuam certos.
    /// </summary>
    public int AddEntry(byte[] code)
    {
        const int EntrySize = 4;
        int count = File.EntryOffsets.Count;
        int tableEnd = count * EntrySize;
        int start = AlignedStart(Raw.Length + EntrySize);
        var output = new byte[start + code.Length];
        Buffer.BlockCopy(Raw, 0, output, 0, tableEnd);
        Buffer.BlockCopy(Raw, tableEnd, output, tableEnd + EntrySize, Raw.Length - tableEnd);
        code.CopyTo(output, start);
        for (int i = 0; i < count; i++)
            BitConverter.TryWriteBytes(output.AsSpan(i * EntrySize), BitConverter.ToInt32(Raw, i * EntrySize) + EntrySize);
        BitConverter.TryWriteBytes(output.AsSpan(tableEnd), start - (tableEnd + EntrySize));
        Raw = output;
        File = new ScriptFile(output, Table);
        return count + 1;
    }

    /// <summary>Troca o código de um script existente. O número não muda, então eventos e outros scripts continuam certos; o código antigo fica sem uso.</summary>
    public void ReplaceEntry(int id, byte[] code)
    {
        const int EntrySize = 4;
        int start = AlignedStart(Raw.Length);
        var output = new byte[start + code.Length];
        Raw.CopyTo(output, 0);
        code.CopyTo(output, start);
        int entry = (id - 1) * EntrySize;
        BitConverter.TryWriteBytes(output.AsSpan(entry), start - (entry + EntrySize));
        Raw = output;
        File = new ScriptFile(output, Table);
    }

    /// <summary>O jogo lê os passos de uma cena de 2 em 2 bytes: fora do alinhamento, leria outra coisa.</summary>
    private static int AlignedStart(int position) => (position + 3) & ~3;

    /// <summary>Grava bytes por cima de um trecho, sem mudar o tamanho do arquivo.</summary>
    public void Overwrite(int at, byte[] bytes)
    {
        bytes.CopyTo(Raw, at);
        File = new ScriptFile(Raw, Table);
    }

    /// <summary>Aponta um salto já gravado para outra posição do arquivo.</summary>
    public void PatchJump(int at, int target)
    {
        BitConverter.TryWriteBytes(Raw.AsSpan(at), target - (at + 4));
        File = new ScriptFile(Raw, Table);
    }

    /// <summary>Insere antes do comando em <paramref name="before"/>. Quem saltava para ele passa a executar o novo primeiro.</summary>
    public void Insert(int before, int opcode, IReadOnlyList<long> args)
    {
        RequireInstruction(before);
        var bytes = Encode(opcode, args, out var jumps);
        Apply(new Splice(before, 0, bytes), jumps.Select(j => new Reference(before + j.At, j.Target)).ToList());
    }

    /// <summary>Remove o comando. Quem saltava para ele passa a cair no que vem depois.</summary>
    public void Remove(int offset)
    {
        var instruction = RequireInstruction(offset);
        int end = EndOf(instruction);
        Apply(new Splice(offset, end - offset, []), []);
    }

    private ScriptInstruction RequireInstruction(int offset) =>
        Instructions().FirstOrDefault(i => i.Offset == offset) ?? throw new ArgumentException(Loc.T("core.script.notInstruction", $"0x{offset:X}"));

    private static int EndOf(ScriptInstruction i) => i.Args.Count == 0 ? i.Offset + 2 : i.Args[^1].At + i.Args[^1].Size;

    private void Apply(Splice edit, List<Reference> added)
    {
        var refs = new List<Reference>(added);
        for (int i = 0; i < File.EntryOffsets.Count; i++) refs.Add(new Reference(i * HeaderEntrySize, File.EntryOffsets[i]));
        var movements = new SortedSet<int>();
        foreach (var instruction in Instructions())
            foreach (var a in instruction.Args)
            {
                if (a.Type is not ("script" or "movement" or "addr")) continue;
                // o argumento do comando removido some junto com ele
                if (a.At >= edit.At && a.At < edit.At + edit.Remove) continue;
                refs.Add(new Reference(a.At, (int)a.Value));
                if (a.Type == "movement") movements.Add((int)a.Value);
            }

        // dados de movimento precisam continuar alinhados: o preenchimento fica logo antes deles, onde a execução não passa
        var splices = new List<Splice> { edit };
        foreach (var target in movements)
        {
            if (target % MovementAlignment != 0) continue;
            int moved = Map(target, splices);
            int gap = (MovementAlignment - moved % MovementAlignment) % MovementAlignment;
            if (gap != 0) splices.Add(new Splice(target, 0, new byte[gap], TargetAfter: true));
        }
        splices.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.TargetAfter.CompareTo(b.TargetAfter));

        using var ms = new MemoryStream();
        int from = 0;
        foreach (var s in splices)
        {
            ms.Write(Raw, from, s.At - from);
            ms.Write(s.Insert);
            from = s.At + s.Remove;
        }
        ms.Write(Raw, from, Raw.Length - from);
        var output = ms.ToArray();

        foreach (var r in refs)
        {
            bool inserted = added.Contains(r);
            // o argumento de um comando novo já está na posição final do bloco inserido, antes do deslocamento dos outros
            int at = inserted ? MapPosition(edit.At, splices.Where(s => s != edit)) + (r.At - edit.At) : MapPosition(r.At, splices);
            BitConverter.TryWriteBytes(output.AsSpan(at), Map(r.Target, splices) - (at + HeaderEntrySize));
        }
        Raw = output;
        File = new ScriptFile(output, Table);
    }

    /// <summary>Nova posição de um alvo de salto. Em ponto de inserção, fica antes do que entrou, salvo no preenchimento.</summary>
    private static int Map(int target, IEnumerable<Splice> splices)
    {
        int shift = 0;
        foreach (var s in splices)
        {
            if (s.Remove > 0 && target >= s.At && target < s.At + s.Remove) { shift += s.At - target; continue; }
            if (target > s.At || (target == s.At && s.TargetAfter)) shift += s.Insert.Length - s.Remove;
        }
        return target + shift;
    }

    /// <summary>Nova posição de um dado: o que está no ponto de inserção é empurrado para depois do que entrou.</summary>
    private static int MapPosition(int position, IEnumerable<Splice> splices)
    {
        int shift = 0;
        foreach (var s in splices)
            if (position >= s.At) shift += s.Insert.Length - s.Remove;
        return position + shift;
    }
}
