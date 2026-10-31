namespace PokeHub.Core.Gen4.Scripts;

/// <summary>Placa ou objeto de fundo: script disparado ao interagir com um tile (20 bytes).</summary>
public sealed record BgEvent(int Index, int ScriptId, int Type, int X, int Z, int Y, int Dir);
/// <summary>NPC/objeto (32 bytes): a flag de evento esconde o objeto quando ligada.</summary>
public sealed record ObjectEvent(int Index, int Id, int SpriteId, int Movement, int Type, int EventFlag, int ScriptId, int Facing, int X, int Z, int Y = 0, int XRange = 0, int ZRange = 0);
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
                BitConverter.ToUInt16(d, p + 8), BitConverter.ToUInt16(d, p + 10), BitConverter.ToInt16(d, p + 12), BitConverter.ToUInt16(d, p + 24), BitConverter.ToUInt16(d, p + 26), BitConverter.ToInt32(d, p + 28), BitConverter.ToUInt16(d, p + 20), BitConverter.ToUInt16(d, p + 22)));
        n = Count();
        for (int i = 0; i < n && p + 12 <= d.Length; i++, p += 12)
            Warps.Add(new(i, BitConverter.ToUInt16(d, p), BitConverter.ToUInt16(d, p + 2), BitConverter.ToUInt16(d, p + 4), BitConverter.ToUInt16(d, p + 6)));
        n = Count();
        for (int i = 0; i < n && p + 16 <= d.Length; i++, p += 16)
            Coords.Add(new(i, BitConverter.ToUInt16(d, p), BitConverter.ToInt16(d, p + 2), BitConverter.ToInt16(d, p + 4), BitConverter.ToUInt16(d, p + 6), BitConverter.ToUInt16(d, p + 8),
                BitConverter.ToUInt16(d, p + 12), BitConverter.ToUInt16(d, p + 14)));
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
        void U16(int at, int value) => BitConverter.TryWriteBytes(result.AsSpan(insertAt + at), (ushort)value);
        U16(0, o.Id);
        U16(2, o.SpriteId);
        U16(4, o.Movement);
        U16(6, o.Type);
        U16(8, o.EventFlag);
        U16(10, o.ScriptId);
        U16(12, o.Facing);
        U16(20, o.XRange);
        U16(22, o.ZRange);
        U16(24, o.X);
        U16(26, o.Z);
        BitConverter.TryWriteBytes(result.AsSpan(insertAt + 28), o.Y);
        return result;
    }
}
