namespace PokeHub.Core.Rom;

/// <summary>CRC-16 usado pelo header NDS (poly 0xA001 refletido, init 0xFFFF, igual ao CRC-16/MODBUS).</summary>
public static class Crc16
{
    private static readonly ushort[] Table = BuildTable();

    private static ushort[] BuildTable()
    {
        var t = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            ushort c = (ushort)i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (ushort)((c >> 1) ^ 0xA001) : (ushort)(c >> 1);
            t[i] = c;
        }
        return t;
    }

    public static ushort Compute(ReadOnlySpan<byte> data, ushort initial = 0xFFFF)
    {
        ushort crc = initial;
        foreach (var b in data) crc = (ushort)((crc >> 8) ^ Table[(crc ^ b) & 0xFF]);
        return crc;
    }
}
