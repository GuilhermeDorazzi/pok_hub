using PokeHub.Core.Gen4;

namespace PokeHub.Core.Graphics;

/// <summary>Um arquivo dentro de um NARC, com o tipo lido do conteúdo.</summary>
public sealed record NarcMember(int Index, string Kind, int Size);

/// <summary>Um NARC encontrado na ROM.</summary>
public sealed record NarcEntry(string RelativePath, string FullPath, long Size);

/// <summary>
/// Olhar e trocar o que está nos NARCs sem extrair tudo. Cada arquivo tem o tipo reconhecido pelo cabeçalho,
/// e gráficos viram miniatura usando a paleta (e os tiles, para um NSCR) mais próxima no mesmo NARC, que é como os jogos costumam agrupar.
/// </summary>
public sealed class NarcBrowser
{
    private readonly List<byte[]> _files;
    private readonly Narc _narc;
    public string Path { get; }
    public IReadOnlyList<NarcMember> Members { get; }

    private NarcBrowser(string path, Narc narc)
    {
        Path = path;
        _narc = narc;
        _files = narc.Files;
        Members = _files.Select((f, i) => new NarcMember(i, KindOf(f), f.Length)).ToList();
    }

    public static NarcBrowser Open(string path) => new(path, Narc.Read(File.ReadAllBytes(path)));

    /// <summary>Todos os NARCs abaixo de uma pasta, reconhecidos pelos 4 primeiros bytes, porque muitos não têm extensão.</summary>
    public static IReadOnlyList<NarcEntry> Scan(string filesRoot)
    {
        var result = new List<NarcEntry>();
        Span<byte> magic = stackalloc byte[4];
        foreach (var file in Directory.EnumerateFiles(filesRoot, "*", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(file);
            if (stream.Read(magic) == 4 && magic.SequenceEqual("NARC"u8))
                result.Add(new NarcEntry(System.IO.Path.GetRelativePath(filesRoot, file).Replace('\\', '/'), file, stream.Length));
        }
        return result.OrderBy(e => e.RelativePath, StringComparer.Ordinal).ToList();
    }

    /// <summary>Formatos Nitro têm a assinatura invertida no arquivo (RGCN = NCGR); o resto aparece como dados.</summary>
    public static string KindOf(byte[] data)
    {
        if (data.Length == 0) return "vazio";
        if (Lz10.IsCompressed(data))
        {
            try { return KindOf(Lz10.Decompress(data)) + " (LZ)"; }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException) { return "dados"; }
        }
        if (data.Length < 4) return "dados";
        var magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        return magic switch
        {
            "RGCN" => "NCGR",
            "RLCN" => "NCLR",
            "RCSN" => "NSCR",
            "RECN" => "NCER",
            "RNAN" => "NANR",
            "BMD0" or "BTX0" or "BCA0" or "BTA0" or "BTP0" or "BMA0" or "SDAT" or "NARC" => magic,
            _ => "dados",
        };
    }

    public byte[] Raw(int index) => _files[index];
    private byte[] Data(int index) => Lz10.DecompressIfNeeded(_files[index]);

    /// <summary>Miniatura de paleta, tiles ou tela. null para o que não é imagem ou não pôde ser lido.</summary>
    public RgbaImage? Render(int index)
    {
        try
        {
            return Members[index].Kind.Split(' ')[0] switch
            {
                "NCLR" => DrawPalette(NitroPalette.Read(Data(index))),
                "NCGR" => DrawTiles(index),
                "NSCR" => DrawScreen(index),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    /// <summary>O arquivo do tipo pedido mais perto deste, preferindo os anteriores, como NCLR logo antes do NCGR.</summary>
    private int? Nearest(int index, string kind)
    {
        for (int distance = 1; distance < Members.Count; distance++)
        {
            int before = index - distance, after = index + distance;
            if (before >= 0 && Members[before].Kind.StartsWith(kind, StringComparison.Ordinal)) return before;
            if (after < Members.Count && Members[after].Kind.StartsWith(kind, StringComparison.Ordinal)) return after;
        }
        return null;
    }

    private NitroPalette PaletteNear(int index) =>
        Nearest(index, "NCLR") is { } p ? NitroPalette.Read(Data(p)) : NitroPalette.Grayscale();

    private RgbaImage DrawScreen(int index)
    {
        var screen = NitroScreen.Read(Data(index));
        if (Nearest(index, "NCGR") is not { } t) throw new InvalidDataException("Tela sem tiles no NARC.");
        return screen.Render(NitroTiles.Read(Data(t)), PaletteNear(index));
    }

    private RgbaImage DrawTiles(int index)
    {
        var data = Data(index);
        var palette = PaletteNear(index);
        // sprite em linhas (Pokémon, treinadores) tem a própria leitura, com a cifra
        if (SpriteStrip.IsScanned(data) && SpriteStrip.TryDecode(data, palette) is { } sprite) return sprite;
        var tiles = NitroTiles.Read(data);
        if (tiles.Tiles.Length == 0) throw new InvalidDataException("NCGR sem tiles.");
        var (rows, columns) = NitroTiles.Dimensions(data);
        if (columns <= 0 || rows <= 0 || columns * rows > tiles.Tiles.Length)
        {
            columns = Math.Min(32, (int)Math.Ceiling(Math.Sqrt(tiles.Tiles.Length)));
            rows = (tiles.Tiles.Length + columns - 1) / columns;
        }
        var image = new RgbaImage(columns * 8, rows * 8);
        for (int t = 0; t < Math.Min(tiles.Tiles.Length, rows * columns); t++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int c = tiles.Tiles[t][y * 8 + x];
                    int px = t % columns * 8 + x, py = t / columns * 8 + y;
                    if (c == 0) { image.Set(px, py, 0, 0, 0, 0); continue; }
                    var (r, g, b) = c < palette.Colors.Length ? palette.Colors[c] : ((byte)255, (byte)0, (byte)255);
                    image.Set(px, py, r, g, b);
                }
        return image;
    }

    private static RgbaImage DrawPalette(NitroPalette palette)
    {
        const int Cell = 8, PerRow = 16;
        int rows = Math.Max(1, (palette.Colors.Length + PerRow - 1) / PerRow);
        var image = new RgbaImage(PerRow * Cell, rows * Cell);
        for (int i = 0; i < palette.Colors.Length; i++)
        {
            var (r, g, b) = palette.Colors[i];
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                    image.Set(i % PerRow * Cell + x, i / PerRow * Cell + y, r, g, b);
        }
        return image;
    }

    /// <summary>Troca o conteúdo de um arquivo e grava o NARC, mantendo os outros byte a byte. Recusa um tipo diferente do original.</summary>
    public void Replace(int index, byte[] data, bool allowDifferentKind = false)
    {
        if (!allowDifferentKind && KindOf(data) != KindOf(_files[index]))
            throw new InvalidDataException($"{KindOf(data)} != {KindOf(_files[index])}");
        _files[index] = data;
        var temp = Path + ".tmp";
        File.WriteAllBytes(temp, _narc.Write());
        File.Move(temp, Path, overwrite: true);
    }
}
