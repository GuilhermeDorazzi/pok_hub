using PokeHub.Core.Graphics;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Sprites frontais de treinador: 5 arquivos por classe no NARC (NCGR, NCLR, NCER, NANR e um NCGR "NCBR" com a imagem inteira).
/// Usa o NCBR, que não precisa montar células: uma faixa de quadros quadrados, 4 bits por pixel em linhas, cifrada com o LCRNG do jogo.
/// O sentido da cifra muda entre jogos de Gen 4, então vence a leitura em que os pixels vizinhos mais se repetem.
/// </summary>
public sealed class TrainerSprites
{
    private const int FilesPerClass = 5;
    private readonly Narc _narc;

    private TrainerSprites(Narc narc) => _narc = narc;

    /// <summary>null se o jogo não tiver o arquivo: o editor segue funcionando, só sem a imagem.</summary>
    public static TrainerSprites? Load(Gen4Game game) =>
        File.Exists(game.TrainerGraphicsPath) ? new TrainerSprites(Narc.Read(File.ReadAllBytes(game.TrainerGraphicsPath))) : null;

    /// <summary>Primeiro quadro da classe, com a cor 0 transparente. null se a classe não tem sprite ou o arquivo não está no formato esperado.</summary>
    public RgbaImage? Render(int trainerClass)
    {
        int first = trainerClass * FilesPerClass;
        if (trainerClass < 0 || first + FilesPerClass > _narc.Files.Count) return null;
        try { return Decode(_narc.Files[first + 4], _narc.Files[first + 1]); }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    public static RgbaImage? Decode(byte[] ncbr, byte[] nclr)
    {
        var palette = NitroPalette.Read(nclr);
        var ch = NitroFile.Sections(ncbr, "NCGR")["CHAR"];
        int height = BitConverter.ToUInt16(ch, 0) * 8, width = BitConverter.ToUInt16(ch, 2) * 8;
        int size = (int)BitConverter.ToUInt32(ch, 16), offset = (int)BitConverter.ToUInt32(ch, 20);
        bool is4Bit = BitConverter.ToUInt32(ch, 4) == 3;
        if (!is4Bit || height == 0 || width < height || size < width * height / 2 || offset + size > ch.Length) return null;

        var raw = ch.AsSpan(offset, size).ToArray();
        byte[][] readings = [Decrypt(raw, fromEnd: true), Decrypt(raw, fromEnd: false), raw];
        var pixels = readings.MaxBy(p => Smoothness(p, width, height))!;

        var image = new RgbaImage(height, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < height; x++)
            {
                int c = Pixel(pixels, width, x, y);
                if (c == 0) { image.Set(x, y, 0, 0, 0, 0); continue; }
                var (r, g, b) = c < palette.Colors.Length ? palette.Colors[c] : ((byte)255, (byte)0, (byte)255);
                image.Set(x, y, r, g, b);
            }
        return image;
    }

    private static int Pixel(byte[] pixels, int width, int x, int y)
    {
        byte b = pixels[y * (width / 2) + x / 2];
        return (x & 1) != 0 ? b >> 4 : b & 0xF;
    }

    private static int Smoothness(byte[] pixels, int width, int height)
    {
        int same = 0;
        for (int y = 0; y < height; y++)
            for (int x = 1; x < width; x++)
                if (Pixel(pixels, width, x, y) == Pixel(pixels, width, x - 1, y)) same++;
        return same;
    }

    /// <summary>A semente é a primeira ou a última palavra, conforme o sentido em que os dados foram cifrados.</summary>
    public static byte[] Decrypt(byte[] data, bool fromEnd)
    {
        var output = new byte[data.Length];
        int count = data.Length / 2;
        if (count == 0) return output;
        uint seed = BitConverter.ToUInt16(data, fromEnd ? (count - 1) * 2 : 0);
        for (int n = 0; n < count; n++)
        {
            int i = fromEnd ? count - 1 - n : n;
            BitConverter.TryWriteBytes(output.AsSpan(i * 2), (ushort)(BitConverter.ToUInt16(data, i * 2) ^ (ushort)seed));
            seed = seed * 0x41C64E6D + 0x6073;
        }
        return output;
    }
}
