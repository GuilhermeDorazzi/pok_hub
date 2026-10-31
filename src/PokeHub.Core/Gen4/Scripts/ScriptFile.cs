namespace PokeHub.Core.Gen4.Scripts;

/// <summary>Uma instrução desmontada: posição, comando e argumentos (tipo, valor; alvos de salto já absolutos).</summary>
public sealed record ScriptInstruction(int Offset, int Opcode, string Name, IReadOnlyList<ScriptArg> Args)
{
    public ScriptArg? Arg(string type) => Args.FirstOrDefault(a => a.Type == type);
    public IEnumerable<ScriptArg> ArgsOf(string type) => Args.Where(a => a.Type == type);
}

/// <summary>At e Size dizem onde o valor está no arquivo, para quem for editá-lo no lugar.</summary>
public sealed record ScriptArg(string Type, long Value, int At = 0, int Size = 0);

/// <summary>Um script (ponto de entrada ou sub-rotina): instruções em ordem de leitura, seguindo saltos e chamadas.</summary>
public sealed class ScriptRoutine
{
    public int Offset { get; init; }
    public List<ScriptInstruction> Instructions { get; } = [];
    /// <summary>Offsets das rotinas chamadas (Call/GoTo e condicionais), para quem quiser agregar.</summary>
    public HashSet<int> Targets { get; } = [];
}

/// <summary>
/// Arquivo de scripts de um mapa (a/0/1/2 no HGSS): tabela de offsets u32 (relativos ao fim de cada entrada) terminada
/// por 0xFD13, depois o bytecode. O script n (1-based, como os eventos referenciam) é a entrada n-1 da tabela.
/// </summary>
public sealed class ScriptFile
{
    private readonly byte[] _raw;
    private readonly ScriptCommandTable _table;
    private readonly Dictionary<int, ScriptRoutine> _cache = [];

    public IReadOnlyList<int> EntryOffsets { get; }
    public int HeaderEnd { get; }
    public int Length => _raw.Length;

    public ScriptFile(byte[] raw, ScriptCommandTable table)
    {
        _raw = raw; _table = table;
        var entries = new List<int>();
        int i = 0;
        for (; i + 4 <= raw.Length; i += 4)
        {
            if (raw[i] == 0x13 && raw[i + 1] == 0xFD) { i += 2; break; }
            int off = BitConverter.ToInt32(raw, i) + i + 4;
            if (off < 0 || off >= raw.Length) break;
            entries.Add(off);
        }
        EntryOffsets = entries;
        HeaderEnd = i;
    }

    /// <summary>Verdadeiro para ids que não são do arquivo (scripts padrão do jogo, chamados por CallStd).</summary>
    public bool IsStandardScript(int scriptId) => scriptId < 1 || scriptId > EntryOffsets.Count;

    public ScriptRoutine? Routine(int scriptId) => IsStandardScript(scriptId) ? null : RoutineAt(EntryOffsets[scriptId - 1]);

    /// <summary>Desmonta a partir de um offset, seguindo a execução em linha reta e as sub-rotinas alcançadas (sem repetir).</summary>
    public ScriptRoutine RoutineAt(int offset)
    {
        if (_cache.TryGetValue(offset, out var cached)) return cached;
        var routine = new ScriptRoutine { Offset = offset };
        _cache[offset] = routine;
        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(offset);
        int guard = 0;
        while (pending.Count > 0 && guard++ < 5000)
        {
            int pc = pending.Pop();
            while (pc >= HeaderEnd && pc + 2 <= _raw.Length && visited.Add(pc))
            {
                int opcode = BitConverter.ToUInt16(_raw, pc);
                var cmd = _table.Get(opcode);
                if (cmd is null) { routine.Instructions.Add(new(pc, opcode, $"?{opcode:X4}", [])); break; }
                int p = pc + 2;
                var args = new List<ScriptArg>();
                bool ok = ReadArgs(cmd.Args, ref p, args);
                if (ok && cmd.Cases is not null && cmd.SwitchArg is { } sw && sw < args.Count && cmd.Cases.TryGetValue(args[sw].Value.ToString(), out var extra))
                    ok = ReadArgs(extra, ref p, args);
                routine.Instructions.Add(new(pc, opcode, cmd.Name, args));
                if (!ok) break;
                foreach (var a in args)
                    if (a.Type is "script" && a.Value >= HeaderEnd && a.Value < _raw.Length)
                    {
                        routine.Targets.Add((int)a.Value);
                        pending.Push((int)a.Value);
                    }
                if (cmd.IsReturn || cmd.IsAbsBranch) break; // End/Return/GoTo: a execução não continua na linha seguinte
                pc = p;
            }
        }
        return routine;
    }

    private bool ReadArgs(List<System.Text.Json.JsonElement> specs, ref int p, List<ScriptArg> args)
    {
        foreach (var spec in specs)
        {
            int size = ScriptCommandTable.ArgSize(spec);
            if (p + size > _raw.Length) return false;
            long value = size switch { 1 => _raw[p], 2 => BitConverter.ToUInt16(_raw, p), _ => BitConverter.ToUInt32(_raw, p) };
            int at = p;
            p += size;
            var type = ScriptCommandTable.ArgType(spec);
            if (type is "script" or "movement" or "addr") value = (int)(value + p); // relativo ao fim do argumento
            args.Add(new ScriptArg(type, value, at, size));
        }
        return true;
    }
}

/// <summary>
/// Cabeçalho de scripts do mapa (level scripts): entradas de 5 bytes (tipo, u16, u16) terminadas por tipo 0.
/// Tipo 1 aponta para uma tabela de (variável, valor, script) de 6 bytes terminada por variável 0: "ao entrar no mapa,
/// se a variável valer X, rode o script"; os outros tipos (2 a 4) rodam o script indicado em momentos fixos do carregamento.
/// </summary>
public sealed class MapScriptHeader
{
    public sealed record Entry(int Kind, int A, int B);
    public sealed record VarTrigger(int Variable, int Value, int ScriptId);

    public List<Entry> Entries { get; } = [];
    public List<VarTrigger> Triggers { get; } = [];

    public MapScriptHeader(byte[] raw)
    {
        int initOffset = -1;
        for (int i = 0; i + 5 <= raw.Length; i += 5)
        {
            if (raw[i] == 0) break;
            if (raw[i] == 1)
            {
                initOffset = i + 5 + BitConverter.ToInt32(raw, i + 1);
                Entries.Add(new(1, -1, -1));
            }
            else Entries.Add(new(raw[i], BitConverter.ToUInt16(raw, i + 1), BitConverter.ToUInt16(raw, i + 3)));
        }
        if (initOffset < 0) return;
        for (int i = initOffset; i + 6 <= raw.Length; i += 6)
        {
            int v = BitConverter.ToUInt16(raw, i);
            if (v == 0) break;
            Triggers.Add(new(v, BitConverter.ToUInt16(raw, i + 2), BitConverter.ToUInt16(raw, i + 4)));
        }
    }
}
