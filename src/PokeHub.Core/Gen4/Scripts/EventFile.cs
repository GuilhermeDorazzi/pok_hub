namespace PokeHub.Core.Gen4.Scripts;

/// <summary>Placa ou objeto de fundo: script disparado ao interagir com um tile (20 bytes).</summary>
public sealed record BgEvent(int Index, int ScriptId, int Type, int X, int Z, int Y, int Dir);
/// <summary>NPC/objeto (32 bytes): a flag de evento esconde o objeto quando ligada.</summary>
public sealed record ObjectEvent(int Index, int Id, int SpriteId, int Movement, int Type, int EventFlag, int ScriptId, int Facing, int X, int Z, int Y = 0, int XRange = 0, int ZRange = 0, int Sight = 0);
/// <summary>Warp (12 bytes): porta para outro mapa (header) e âncora de chegada.</summary>
public sealed record WarpEvent(int Index, int X, int Z, int TargetHeader, int Anchor);
/// <summary>Trigger de piso (16 bytes): roda o script quando o jogador pisa na área e a variável vale Val.</summary>
public sealed record CoordEvent(int Index, int ScriptId, int X, int Z, int W, int H, int Val, int Var);

/// <summary>Arquivo de eventos de um mapa (a/0/3/2 no HGSS): quatro listas, cada uma com contagem u32 à frente.</summary>
public sealed class EventFile
{
    public List<BgEvent> Bg { get; } = [];
    public List<ObjectEvent> Objects { get; } = [];
    public List<WarpEvent> Warps { get; } = [];
    public List<CoordEvent> Coords { get; } = [];

    public EventFile(byte[] d)
    {
        int p = 0;
        int Count() { if (p + 4 > d.Length) return 0; int n = BitConverter.ToInt32(d, p); p += 4; return Math.Max(0, n); }
        int n = Count();
        for (int i = 0; i < n && p + 20 <= d.Length; i++, p += 20)
            Bg.Add(new(i, BitConverter.ToUInt16(d, p), BitConverter.ToUInt16(d, p + 2), BitConverter.ToInt32(d, p + 4), BitConverter.ToInt32(d, p + 8), BitConverter.ToInt32(d, p + 12), BitConverter.ToUInt16(d, p + 16)));
        n = Count();
        for (int i = 0; i < n && p + 32 <= d.Length; i++, p += 32)
            Objects.Add(new(i, BitConverter.ToUInt16(d, p), BitConverter.ToUInt16(d, p + 2), BitConverter.ToUInt16(d, p + 4), BitConverter.ToUInt16(d, p + 6),
                BitConverter.ToUInt16(d, p + 8), BitConverter.ToUInt16(d, p + 10), BitConverter.ToInt16(d, p + 12), BitConverter.ToUInt16(d, p + 24), BitConverter.ToUInt16(d, p + 26), BitConverter.ToInt32(d, p + 28), BitConverter.ToUInt16(d, p + 20), BitConverter.ToUInt16(d, p + 22), BitConverter.ToUInt16(d, p + 14)));
        n = Count();
        for (int i = 0; i < n && p + 12 <= d.Length; i++, p += 12)
            Warps.Add(new(i, BitConverter.ToUInt16(d, p), BitConverter.ToUInt16(d, p + 2), BitConverter.ToUInt16(d, p + 4), BitConverter.ToUInt16(d, p + 6)));
        n = Count();
        for (int i = 0; i < n && p + 16 <= d.Length; i++, p += 16)
            Coords.Add(new(i, BitConverter.ToUInt16(d, p), BitConverter.ToInt16(d, p + 2), BitConverter.ToInt16(d, p + 4), BitConverter.ToUInt16(d, p + 6), BitConverter.ToUInt16(d, p + 8),
                BitConverter.ToUInt16(d, p + 12), BitConverter.ToUInt16(d, p + 14)));
    }

    private const int BgRecord = 20, ObjectRecord = 32;

    private static (int CountAt, int Start, int Count) ObjectList(byte[] raw)
    {
        int countAt = 4 + BitConverter.ToInt32(raw, 0) * BgRecord;
        return (countAt, countAt + 4, BitConverter.ToInt32(raw, countAt));
    }

    private static int IndexOfObject(byte[] raw, int id)
    {
        var (_, start, count) = ObjectList(raw);
        for (int i = 0; i < count; i++)
            if (BitConverter.ToUInt16(raw, start + i * ObjectRecord) == id) return i;
        throw new InvalidDataException($"NPC {id}");
    }

    private static void WriteBg(byte[] result, int at, BgEvent b)
    {
        BitConverter.TryWriteBytes(result.AsSpan(at), (ushort)b.ScriptId);
        BitConverter.TryWriteBytes(result.AsSpan(at + 2), (ushort)b.Type);
        BitConverter.TryWriteBytes(result.AsSpan(at + 4), b.X);
        BitConverter.TryWriteBytes(result.AsSpan(at + 8), b.Z);
        BitConverter.TryWriteBytes(result.AsSpan(at + 12), b.Y);
        BitConverter.TryWriteBytes(result.AsSpan(at + 16), (ushort)b.Dir);
    }

    /// <summary>Acrescenta um evento de tile no fim da lista deles; o que vem depois é copiado como está.</summary>
    public static byte[] AddBg(byte[] raw, BgEvent b)
    {
        int count = BitConverter.ToInt32(raw, 0), at = 4 + count * BgRecord;
        var result = new byte[raw.Length + BgRecord];
        Buffer.BlockCopy(raw, 0, result, 0, at);
        Buffer.BlockCopy(raw, at, result, at + BgRecord, raw.Length - at);
        BitConverter.TryWriteBytes(result.AsSpan(0), count + 1);
        WriteBg(result, at, b);
        return result;
    }

    private static int IndexOfBg(byte[] raw, int scriptId)
    {
        int count = BitConverter.ToInt32(raw, 0);
        for (int i = 0; i < count; i++)
            if (BitConverter.ToUInt16(raw, 4 + i * BgRecord) == scriptId) return i;
        throw new InvalidDataException($"Bg {scriptId}");
    }

    public static byte[] MoveBg(byte[] raw, BgEvent b)
    {
        var result = raw.ToArray();
        WriteBg(result, 4 + IndexOfBg(raw, b.ScriptId) * BgRecord, b);
        return result;
    }

    public static byte[] RemoveBg(byte[] raw, int scriptId)
    {
        int at = 4 + IndexOfBg(raw, scriptId) * BgRecord;
        var result = new byte[raw.Length - BgRecord];
        Buffer.BlockCopy(raw, 0, result, 0, at);
        Buffer.BlockCopy(raw, at + BgRecord, result, at, raw.Length - at - BgRecord);
        BitConverter.TryWriteBytes(result.AsSpan(0), BitConverter.ToInt32(raw, 0) - 1);
        return result;
    }

    /// <summary>Tira um NPC da lista. Os outros mudam de posição na lista, mas não de número, que é o que scripts usam.</summary>
    public static byte[] RemoveObject(byte[] raw, int id)
    {
        var (countAt, start, count) = ObjectList(raw);
        int at = start + IndexOfObject(raw, id) * ObjectRecord;
        var result = new byte[raw.Length - ObjectRecord];
        Buffer.BlockCopy(raw, 0, result, 0, at);
        Buffer.BlockCopy(raw, at + ObjectRecord, result, at, raw.Length - at - ObjectRecord);
        BitConverter.TryWriteBytes(result.AsSpan(countAt), count - 1);
        return result;
    }

    /// <summary>Grava os dados de um NPC que já existe, procurado pelo número dele. O que não tem campo aqui continua como estava.</summary>
    public static byte[] ReplaceObject(byte[] raw, ObjectEvent o)
    {
        var (_, start, _) = ObjectList(raw);
        var result = raw.ToArray();
        WriteObject(result, start + IndexOfObject(raw, o.Id) * ObjectRecord, o);
        return result;
    }

    private static void WriteObject(byte[] result, int at, ObjectEvent o)
    {
        void U16(int offset, int value) => BitConverter.TryWriteBytes(result.AsSpan(at + offset), (ushort)value);
        U16(0, o.Id);
        U16(2, o.SpriteId);
        U16(4, o.Movement);
        U16(6, o.Type);
        U16(8, o.EventFlag);
        U16(10, o.ScriptId);
        U16(12, o.Facing);
        U16(14, o.Sight);
        U16(20, o.XRange);
        U16(22, o.ZRange);
        U16(24, o.X);
        U16(26, o.Z);
        BitConverter.TryWriteBytes(result.AsSpan(at + 28), o.Y);
    }

    /// <summary>Devolve o arquivo com um NPC a mais no fim da lista de objetos. O resto, inclusive o que o hub não interpreta, é copiado como está.</summary>
    public static byte[] AddObject(byte[] raw, ObjectEvent o)
    {
        const int BgSize = 20, ObjectSize = 32;
        int countAt = 4 + BitConverter.ToInt32(raw, 0) * BgSize;
        int objects = BitConverter.ToInt32(raw, countAt);
        int insertAt = countAt + 4 + objects * ObjectSize;
        var result = new byte[raw.Length + ObjectSize];
        Buffer.BlockCopy(raw, 0, result, 0, insertAt);
        Buffer.BlockCopy(raw, insertAt, result, insertAt + ObjectSize, raw.Length - insertAt);
        BitConverter.TryWriteBytes(result.AsSpan(countAt), objects + 1);
        WriteObject(result, insertAt, o);
        return result;
    }
}
