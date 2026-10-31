using PokeHub.Core.Localization;

namespace PokeHub.Core.Graphics;

/// <summary>Imagem RGBA de 8 bits por canal, linha a linha (pronta para virar BitmapSource ou PNG).</summary>
public sealed class RgbaImage(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = new byte[width * height * 4];

    public void Set(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int o = (y * Width + x) * 4;
        Pixels[o] = r; Pixels[o + 1] = g; Pixels[o + 2] = b; Pixels[o + 3] = a;
    }

    /// <summary>Recorte retangular (para mostrar só a parte usada de um tilemap grande).</summary>
    public RgbaImage Crop(int x, int y, int w, int h)
    {
        var img = new RgbaImage(w, h);
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(Pixels, ((y + row) * Width + x) * 4, img.Pixels, row * w * 4, w * 4);
        return img;
    }
}

/// <summary>Cabeçalho comum dos formatos Nitro (NCLR, NCGR, NSCR): magic, seções nomeadas.</summary>
internal static class NitroFile
{
    public static Dictionary<string, byte[]> Sections(ReadOnlySpan<byte> d, string expectedMagic)
    {
        if (d.Length < 16) throw new InvalidDataException(Loc.T("core.gfx.badHeader", expectedMagic));
        var magic = new string([(char)d[3], (char)d[2], (char)d[1], (char)d[0]]);
        if (magic != expectedMagic) throw new InvalidDataException(Loc.T("core.gfx.badHeader", expectedMagic));
        int headerSize = BitConverter.ToUInt16(d[12..14]);
        int count = BitConverter.ToUInt16(d[14..16]);
        var result = new Dictionary<string, byte[]>();
        int p = headerSize;
        for (int i = 0; i < count && p + 8 <= d.Length; i++)
        {
            var name = new string([(char)d[p + 3], (char)d[p + 2], (char)d[p + 1], (char)d[p]]);
            int size = (int)BitConverter.ToUInt32(d[(p + 4)..(p + 8)]);
            result[name] = d[(p + 8)..Math.Min(p + size, d.Length)].ToArray();
            p += size;
        }
        return result;
    }
}

/// <summary>Paleta NCLR: cores BGR555 convertidas para RGB.</summary>
public sealed class NitroPalette
{
    public (byte R, byte G, byte B)[] Colors { get; }
    public bool Is8Bit { get; }

    private NitroPalette((byte, byte, byte)[] colors, bool is8Bit) { Colors = colors; Is8Bit = is8Bit; }

    public static NitroPalette Read(ReadOnlySpan<byte> data)
    {
        var sec = NitroFile.Sections(data, "NCLR");
        var pltt = sec["PLTT"];
        uint depth = BitConverter.ToUInt32(pltt, 0);
        int size = (int)BitConverter.ToUInt32(pltt, 8);
        int offset = (int)BitConverter.ToUInt32(pltt, 12);
        size = Math.Min(size, pltt.Length - offset);
        var colors = new (byte, byte, byte)[size / 2];
        for (int i = 0; i < colors.Length; i++)
        {
            ushort v = BitConverter.ToUInt16(pltt, offset + i * 2);
            colors[i] = ((byte)((v & 31) * 255 / 31), (byte)(((v >> 5) & 31) * 255 / 31), (byte)(((v >> 10) & 31) * 255 / 31));
        }
        return new NitroPalette(colors, depth == 4);
    }
}

/// <summary>Tiles NCGR de 8×8 (4 ou 8 bits por pixel), já desempacotados para um índice por pixel.</summary>
public sealed class NitroTiles
{
    public byte[][] Tiles { get; }
    public bool Is8Bit { get; }

    private NitroTiles(byte[][] tiles, bool is8Bit) { Tiles = tiles; Is8Bit = is8Bit; }

    public static NitroTiles Read(ReadOnlySpan<byte> data)
    {
        var sec = NitroFile.Sections(data, "NCGR");
        var ch = sec["CHAR"];
        uint depth = BitConverter.ToUInt32(ch, 4);
        int size = (int)BitConverter.ToUInt32(ch, 16);
        int offset = (int)BitConverter.ToUInt32(ch, 20);
        size = Math.Min(size, ch.Length - offset);
        bool is8 = depth == 4;
        int tileBytes = is8 ? 64 : 32;
        var tiles = new byte[size / tileBytes][];
        for (int t = 0; t < tiles.Length; t++)
        {
            var px = new byte[64];
            if (is8) Array.Copy(ch, offset + t * 64, px, 0, 64);
            else
                for (int k = 0; k < 32; k++)
                {
                    byte b = ch[offset + t * 32 + k];
                    px[k * 2] = (byte)(b & 0xF);
                    px[k * 2 + 1] = (byte)(b >> 4);
                }
            tiles[t] = px;
        }
        return new NitroTiles(tiles, is8);
    }
}

/// <summary>Tilemap NSCR: largura e altura em pixels e uma entrada de 16 bits por tile (índice, espelhos, banco de paleta).</summary>
public sealed class NitroScreen
{
    public int Width { get; }
    public int Height { get; }
    public ushort[] Entries { get; }

    private NitroScreen(int w, int h, ushort[] entries) { Width = w; Height = h; Entries = entries; }

    public static NitroScreen Read(ReadOnlySpan<byte> data)
    {
        var sec = NitroFile.Sections(data, "NSCR");
        var scrn = sec["SCRN"];
        int w = BitConverter.ToUInt16(scrn, 0), h = BitConverter.ToUInt16(scrn, 2);
        int size = (int)BitConverter.ToUInt32(scrn, 8);
        size = Math.Min(size, scrn.Length - 12);
        var entries = new ushort[size / 2];
        for (int i = 0; i < entries.Length; i++) entries[i] = BitConverter.ToUInt16(scrn, 12 + i * 2);
        return new NitroScreen(w, h, entries);
    }

    /// <summary>Desenha o tilemap com os tiles e a paleta. Índice de cor 0 é transparente quando transparentZero.</summary>
    public RgbaImage Render(NitroTiles tiles, NitroPalette palette, bool transparentZero = false)
    {
        var img = new RgbaImage(Width, Height);
        int tilesPerRow = Width / 8;
        for (int i = 0; i < Entries.Length; i++)
        {
            ushort e = Entries[i];
            int tile = e & 0x3FF;
            bool hflip = (e & 0x400) != 0, vflip = (e & 0x800) != 0;
            int bank = e >> 12;
            int tx = (i % tilesPerRow) * 8, ty = (i / tilesPerRow) * 8;
            if (ty + 8 > Height) break;
            var px = tile < tiles.Tiles.Length ? tiles.Tiles[tile] : new byte[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int sx = hflip ? 7 - x : x, sy = vflip ? 7 - y : y;
                    int c = px[sy * 8 + sx];
                    if (transparentZero && c == 0) { img.Set(tx + x, ty + y, 0, 0, 0, 0); continue; }
                    int ci = tiles.Is8Bit ? c : bank * 16 + c;
                    var (r, g, b) = ci < palette.Colors.Length ? palette.Colors[ci] : ((byte)255, (byte)0, (byte)255);
                    img.Set(tx + x, ty + y, r, g, b);
                }
        }
        return img;
    }
}
