namespace PokeHub.Core.Rom;

/// <summary>
/// Descompressão BLZ (LZ77 invertido) usada no arm9 e nos overlays do DS. Algoritmo do blz.c de CUE:
/// os últimos 8 bytes guardam o tamanho do cabeçalho, o tamanho da parte comprimida e o acréscimo de tamanho.
/// </summary>
public static class Blz
{
    /// <summary>true se o bloco termina com um rodapé BLZ plausível (acréscimo &gt; 0 e tamanhos coerentes).</summary>
    public static bool LooksCompressed(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8) return false;
        uint incLen = BitConverter.ToUInt32(data[^4..]);
        uint encLen = BitConverter.ToUInt32(data[^8..]) & 0x00FFFFFF;
        int hdrLen = data[^5];
        return incLen != 0 && encLen <= data.Length && hdrLen >= 8 && hdrLen <= encLen;
    }

    /// <summary>Descomprime; devolve a própria entrada se não estiver comprimida.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> pak)
    {
        if (!LooksCompressed(pak)) return pak.ToArray();
        int pakLen = pak.Length;
        uint incLen = BitConverter.ToUInt32(pak[^4..]);
        uint encLen = BitConverter.ToUInt32(pak[^8..]) & 0x00FFFFFF;
        int hdrLen = pak[^5];
        int rawLen = pakLen - (int)encLen;          // parte inicial não comprimida
        int decLen = pakLen + (int)incLen;

        var buffer = new byte[decLen];
        pak[..pakLen].CopyTo(buffer);

        // a região comprimida é armazenada invertida
        Array.Reverse(buffer, rawLen, (int)encLen);

        int p = rawLen + hdrLen;        // depois do cabeçalho (já invertido para o início)
        int pakEnd = rawLen + (int)encLen;
        var raw = new byte[decLen - rawLen];
        int r = 0, rawEnd = raw.Length;
        byte flags = 0; int mask = 0;
        while (r < rawEnd)
        {
            if ((mask >>= 1) == 0)
            {
                if (p >= pakEnd) break;
                flags = buffer[p++]; mask = 0x80;
            }
            if ((flags & mask) == 0)
            {
                if (p >= pakEnd) break;
                raw[r++] = buffer[p++];
            }
            else
            {
                if (p + 1 >= pakEnd) break;
                int pos = buffer[p++] << 8;
                pos |= buffer[p++];
                int len = (pos >> 12) + 3;
                pos = (pos & 0xFFF) + 3;
                if (r + len > rawEnd) len = rawEnd - r;
                while (len-- > 0) { raw[r] = raw[r - pos]; r++; }
            }
        }
        Array.Reverse(raw);
        // saída: início não comprimido (da entrada original) + região decodificada
        var result = new byte[decLen];
        pak[..rawLen].CopyTo(result);
        raw.CopyTo(result, rawLen);
        return result;
    }

    /// <summary>Descomprime um arm9 extraído pelo ndstool (com ou sem o rodapé nitrocode de 12 bytes).</summary>
    public static byte[] DecompressArm9(byte[] arm9)
    {
        var body = arm9.Length >= 12 && BitConverter.ToUInt32(arm9, arm9.Length - 12) == NdsHeader.NitroFooterMagic ? arm9[..^12] : arm9;
        bool wasCompressed = LooksCompressed(body);
        var result = Decompress(body);
        if (wasCompressed) ClearCompressedStaticEnd(result);
        return result;
    }

    /// <summary>
    /// Nos parâmetros do módulo arm9, o campo compressed_static_end (8 bytes antes da assinatura nitrocode 0xDEC00621)
    /// guarda o fim da região comprimida. ds-rom, ndstool e DSPRE o zeram após descomprimir; fazemos o mesmo.
    /// </summary>
    private static void ClearCompressedStaticEnd(byte[] arm9)
    {
        int limit = Math.Min(arm9.Length - 4, 0x4000);
        for (int i = 0x20; i < limit; i += 4)
        {
            if (BitConverter.ToUInt32(arm9, i) != NdsHeader.NitroFooterMagic || i < 8) continue;
            Array.Clear(arm9, i - 8, 4);
            return;
        }
    }
}
