namespace PokeHub.Core.Gen4;

/// <summary>
/// Arquivo NARC (Nitro Archive): header, BTAF (tabela de offsets), BTNF (nomes, mantido como está) e GMIF (dados).
/// Os arquivos no GMIF ficam alinhados a 4 bytes com 0xFF.
/// </summary>
public sealed class Narc
{
    public List<byte[]> Files { get; } = [];
    /// <summary>Bloco BTNF inteiro, inclusive cabeçalho; os jogos Pokémon usam a forma mínima de 16 bytes.</summary>
    public byte[] Btnf { get; set; } = DefaultBtnf();

    private static byte[] DefaultBtnf() =>
        [(byte)'B', (byte)'T', (byte)'N', (byte)'F', 0x10, 0, 0, 0, 4, 0, 0, 0, 0, 0, 1, 0];

    public static Narc Read(byte[] data)
    {
        if (data.Length < 16 || data[0] != 'N' || data[1] != 'A' || data[2] != 'R' || data[3] != 'C')
            throw new InvalidDataException("Não é um NARC.");
        int pos = 16;
        var narc = new Narc();
        (uint Start, uint End)[] fat = [];
        byte[]? gmif = null;
        int blocks = BitConverter.ToUInt16(data, 14);
        for (int b = 0; b < blocks && pos + 8 <= data.Length; b++)
        {
            var magic = System.Text.Encoding.ASCII.GetString(data, pos, 4);
            int size = (int)BitConverter.ToUInt32(data, pos + 4);
            switch (magic)
            {
                case "BTAF":
                    int count = BitConverter.ToUInt16(data, pos + 8);
                    fat = new (uint, uint)[count];
                    for (int i = 0; i < count; i++)
                        fat[i] = (BitConverter.ToUInt32(data, pos + 12 + i * 8), BitConverter.ToUInt32(data, pos + 16 + i * 8));
                    break;
                case "BTNF":
                    narc.Btnf = data[pos..(pos + size)];
                    break;
                case "GMIF":
                    gmif = data[(pos + 8)..Math.Min(data.Length, pos + size)];
                    break;
            }
            pos += size;
        }
        if (gmif is null) throw new InvalidDataException("NARC sem bloco GMIF.");
        foreach (var (s, e) in fat)
        {
            if (e < s || e > gmif.Length) throw new InvalidDataException("Entrada da BTAF fora do GMIF.");
            narc.Files.Add(gmif[(int)s..(int)e]);
        }
        return narc;
    }

    public byte[] Write()
    {
        using var gmif = new MemoryStream();
        var fat = new (uint, uint)[Files.Count];
        for (int i = 0; i < Files.Count; i++)
        {
            uint start = (uint)gmif.Position;
            gmif.Write(Files[i]);
            fat[i] = (start, (uint)gmif.Position);
            while (gmif.Position % 4 != 0) gmif.WriteByte(0xFF);
        }
        int btafSize = 12 + Files.Count * 8;
        int gmifSize = 8 + (int)gmif.Length;
        uint total = (uint)(16 + btafSize + Btnf.Length + gmifSize);

        using var ms = new MemoryStream();
        ms.Write("NARC"u8);
        ms.Write(BitConverter.GetBytes((ushort)0xFFFE));
        ms.Write(BitConverter.GetBytes((ushort)0x0100));
        ms.Write(BitConverter.GetBytes(total));
        ms.Write(BitConverter.GetBytes((ushort)0x10));
        ms.Write(BitConverter.GetBytes((ushort)3));

        ms.Write("BTAF"u8);
        ms.Write(BitConverter.GetBytes((uint)btafSize));
        ms.Write(BitConverter.GetBytes((ushort)Files.Count));
        ms.Write(BitConverter.GetBytes((ushort)0));
        foreach (var (s, e) in fat) { ms.Write(BitConverter.GetBytes(s)); ms.Write(BitConverter.GetBytes(e)); }

        ms.Write(Btnf);

        ms.Write("GMIF"u8);
        ms.Write(BitConverter.GetBytes((uint)gmifSize));
        gmif.Position = 0;
        gmif.CopyTo(ms);
        return ms.ToArray();
    }
}
