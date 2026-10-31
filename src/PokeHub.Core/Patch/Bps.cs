using PokeHub.Core.Localization;

namespace PokeHub.Core.Patch;

/// <summary>
/// Patches BPS (formato do beat, usado pela comunidade para distribuir hacks sem a ROM).
/// Create gera um patch "linear": percorre as duas ROMs e emite SourceRead para trechos iguais e TargetRead
/// para os diferentes; fica maior que o do beat em casos de dados movidos, mas é válido em qualquer aplicador.
/// Apply implementa o formato completo (SourceRead, TargetRead, SourceCopy, TargetCopy) com conferência de CRC32.
/// </summary>
public static class Bps
{
    private const int MinMatch = 16; // trechos iguais menores que isso entram no TargetRead vizinho

    public static byte[] Create(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, string? metadata = null)
    {
        var ms = new MemoryStream();
        ms.Write("BPS1"u8);
        WriteNumber(ms, (ulong)source.Length);
        WriteNumber(ms, (ulong)target.Length);
        var meta = metadata is null ? [] : System.Text.Encoding.UTF8.GetBytes(metadata);
        WriteNumber(ms, (ulong)meta.Length);
        ms.Write(meta);

        // Índice de blocos da origem (hash de Block bytes a cada Block bytes) para achar dados movidos de lugar.
        var index = BuildIndex(source);

        int pos = 0, common = Math.Min(source.Length, target.Length);
        int pending = 0;        // início do trecho ainda não emitido (vira TargetRead)
        int sourceRel = 0;      // posição relativa do SourceCopy, como o aplicador mantém
        ulong rolling = 0;      // hash deslizante dos últimos Block bytes de target terminando antes de pos
        int rolled = 0;

        while (pos < target.Length)
        {
            // 1) mesma posição nas duas ROMs: SourceRead, o comando mais barato
            if (pos < common && source[pos] == target[pos])
            {
                int eq = 1;
                while (pos + eq < common && source[pos + eq] == target[pos + eq]) eq++;
                if (eq >= MinMatch || pos + eq == target.Length)
                {
                    FlushPending(ms, target, pending, pos);
                    Action(ms, 0, eq);
                    pos += eq; pending = pos; rolled = 0;
                    continue;
                }
            }
            // 2) bloco igual em outra posição da origem: SourceCopy
            if (rolled < Block)
            {
                if (pos + Block <= target.Length) { rolling = Hash(target.Slice(pos, Block)); rolled = Block; }
            }
            else
            {
                rolling = Roll(rolling, target[pos - 1], target[pos + Block - 1 < target.Length ? pos + Block - 1 : pos - 1]);
            }
            if (rolled == Block && pos + Block <= target.Length && index.TryGetValue(rolling, out int src)
                && source.Slice(src, Block).SequenceEqual(target.Slice(pos, Block)))
            {
                int len = Block;
                while (pos + len < target.Length && src + len < source.Length && source[src + len] == target[pos + len]) len++;
                FlushPending(ms, target, pending, pos);
                long delta = (long)src - sourceRel;
                Action(ms, 2, len);
                WriteNumber(ms, ((ulong)Math.Abs(delta) << 1) | (delta < 0 ? 1UL : 0UL));
                sourceRel = src + len;
                pos += len; pending = pos; rolled = 0;
                continue;
            }
            pos++;
            if (rolled == Block && pos + Block > target.Length) rolled = 0;
        }
        FlushPending(ms, target, pending, pos);

        var sourceCrc = Crc32.HashToUInt32(source);
        var targetCrc = Crc32.HashToUInt32(target);
        ms.Write(BitConverter.GetBytes(sourceCrc));
        ms.Write(BitConverter.GetBytes(targetCrc));
        var patchCrc = Crc32.HashToUInt32(ms.ToArray());
        ms.Write(BitConverter.GetBytes(patchCrc));
        return ms.ToArray();
    }

    /// <summary>Emite o trecho [pending, upTo) ainda não coberto como TargetRead.</summary>
    private static void FlushPending(Stream ms, ReadOnlySpan<byte> target, int pending, int upTo)
    {
        if (upTo <= pending) return;
        Action(ms, 1, upTo - pending);
        ms.Write(target.Slice(pending, upTo - pending));
    }

    private const int Block = 64;
    private const ulong HashBase = 257;
    private static readonly ulong HashBasePow = Pow(HashBase, Block - 1);

    private static ulong Pow(ulong b, int e) { ulong r = 1; for (int i = 0; i < e; i++) r *= b; return r; }

    /// <summary>Hash polinomial dos Block bytes (aritmética módulo 2^64).</summary>
    private static ulong Hash(ReadOnlySpan<byte> block)
    {
        ulong h = 0;
        foreach (var b in block) h = h * HashBase + b;
        return h;
    }

    private static ulong Roll(ulong h, byte outgoing, byte incoming) => (h - outgoing * HashBasePow) * HashBase + incoming;

    private static Dictionary<ulong, int> BuildIndex(ReadOnlySpan<byte> source)
    {
        var index = new Dictionary<ulong, int>(source.Length / Block + 1);
        for (int i = 0; i + Block <= source.Length; i += Block)
            index.TryAdd(Hash(source.Slice(i, Block)), i); // primeira ocorrência basta
        return index;
    }

    public static byte[] Apply(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch)
    {
        if (patch.Length < 4 + 12 || !patch[..4].SequenceEqual("BPS1"u8)) throw new InvalidDataException(Loc.T("core.bps.notBps"));
        var body = patch[..^12];
        var footer = patch[^12..];
        uint sourceCrc = BitConverter.ToUInt32(footer[..4]), targetCrc = BitConverter.ToUInt32(footer[4..8]), patchCrc = BitConverter.ToUInt32(footer[8..]);
        if (Crc32.HashToUInt32(patch[..^4]) != patchCrc) throw new InvalidDataException(Loc.T("core.bps.corrupt"));
        if (Crc32.HashToUInt32(source) != sourceCrc) throw new InvalidDataException(Loc.T("core.bps.wrongSource"));

        int p = 4;
        ulong sourceSize = ReadNumber(body, ref p), targetSize = ReadNumber(body, ref p), metaSize = ReadNumber(body, ref p);
        p += (int)metaSize;
        if ((int)sourceSize != source.Length) throw new InvalidDataException(Loc.T("core.bps.wrongSource"));
        var target = new byte[targetSize];
        int outPos = 0, sourceRel = 0, targetRel = 0;
        while (p < body.Length)
        {
            ulong data = ReadNumber(body, ref p);
            int command = (int)(data & 3), length = (int)(data >> 2) + 1;
            switch (command)
            {
                case 0: // SourceRead
                    source.Slice(outPos, length).CopyTo(target.AsSpan(outPos)); outPos += length; break;
                case 1: // TargetRead
                    body.Slice(p, length).CopyTo(target.AsSpan(outPos)); p += length; outPos += length; break;
                case 2: // SourceCopy
                {
                    ulong d = ReadNumber(body, ref p);
                    sourceRel += ((d & 1) != 0 ? -1 : 1) * (int)(d >> 1);
                    source.Slice(sourceRel, length).CopyTo(target.AsSpan(outPos)); sourceRel += length; outPos += length; break;
                }
                case 3: // TargetCopy (pode se sobrepor: copia byte a byte)
                {
                    ulong d = ReadNumber(body, ref p);
                    targetRel += ((d & 1) != 0 ? -1 : 1) * (int)(d >> 1);
                    for (int i = 0; i < length; i++) target[outPos++] = target[targetRel++];
                    break;
                }
            }
        }
        if (Crc32.HashToUInt32(target) != targetCrc) throw new InvalidDataException(Loc.T("core.bps.badResult"));
        return target;
    }

    private static void Action(Stream s, int command, int length) => WriteNumber(s, ((ulong)(length - 1) << 2) | (uint)command);

    private static void WriteNumber(Stream s, ulong value)
    {
        while (true)
        {
            byte x = (byte)(value & 0x7F);
            value >>= 7;
            if (value == 0) { s.WriteByte((byte)(0x80 | x)); break; }
            s.WriteByte(x);
            value--;
        }
    }

    private static ulong ReadNumber(ReadOnlySpan<byte> d, ref int p)
    {
        ulong value = 0, shift = 1;
        while (true)
        {
            byte x = d[p++];
            value += (ulong)(x & 0x7F) * shift;
            if ((x & 0x80) != 0) return value;
            shift <<= 7;
            value += shift;
        }
    }
}
