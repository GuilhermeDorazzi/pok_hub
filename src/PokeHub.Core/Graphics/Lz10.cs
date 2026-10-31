using PokeHub.Core.Localization;

namespace PokeHub.Core.Graphics;

/// <summary>Descompressão LZ10 (tipo 0x10 do BIOS do DS), usada nos gráficos dentro dos NARCs.</summary>
public static class Lz10
{
    public static bool IsCompressed(ReadOnlySpan<byte> data) => data.Length >= 4 && data[0] == 0x10;

    /// <summary>Devolve os dados descomprimidos, ou os próprios bytes quando não estão comprimidos.</summary>
    public static byte[] DecompressIfNeeded(ReadOnlySpan<byte> data) => IsCompressed(data) ? Decompress(data) : data.ToArray();

    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (!IsCompressed(data)) throw new InvalidDataException(Loc.T("core.gfx.notLz10"));
        int size = (int)(BitConverter.ToUInt32(data[..4]) >> 8);
        var output = new byte[size];
        int i = 4, o = 0;
        while (o < size)
        {
            byte flags = data[i++];
            for (int bit = 0; bit < 8 && o < size; bit++)
            {
                if ((flags & (0x80 >> bit)) != 0)
                {
                    byte b1 = data[i], b2 = data[i + 1];
                    i += 2;
                    int length = (b1 >> 4) + 3;
                    int disp = (((b1 & 0xF) << 8) | b2) + 1;
                    for (int k = 0; k < length && o < size; k++) { output[o] = output[o - disp]; o++; }
                }
                else output[o++] = data[i++];
            }
        }
        return output;
    }
}
