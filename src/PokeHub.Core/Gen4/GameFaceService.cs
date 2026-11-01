using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using PokeHub.Core.Graphics;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Gen4;

/// <summary>O que aparece ao abrir o jogo: logo da tela de título, nome e ícone do cartão. A Lugia e o Ho-Oh são modelos 3D e ficam de fora.</summary>
public static class GameFaceService
{
    public const int LogoWidth = 256, LogoHeight = 120, IconSize = 32;

    // o logo usa os índices 1 a 127; do 128 em diante a paleta é do fundo da tela de título, que não muda
    private const int LogoColors = 127, IconColors = 15;
    private const int LogoScreen = 0, LogoTilesWide = 32, LogoTilesHigh = 15, LogoFirstRow = 5;

    public sealed record BannerText(string Name, string Subtitle, string RomTitle);

    /// <summary>Tela de título só é conhecida no HG/SS; o cartão, em qualquer jogo extraído em ds-rom.</summary>
    public static bool CanEditLogo(Project project) => Gen4Game.For(project).IsHgss && File.Exists(TitlePath(project));
    public static bool CanEditBanner(Project project) => DsRomTool.IsDsRomWorkspace(project.Paths.Workspace);

    private static string TitlePath(Project project) => Path.Combine(Gen4Game.For(project).FilesRoot, @"a\0\4\6");
    private static (int Tiles, int Palette) LogoFiles(Project project) =>
        Gen4Game.For(project).Game == BaseGame.HeartGold ? (3, 4) : (1, 2);

    public static RgbaImage? CurrentLogo(Project project) =>
        CanEditLogo(project) ? NarcBrowser.Open(TitlePath(project)).Render(LogoFiles(project).Tiles) : null;

    /// <summary>Troca o logo. Devolve quantas cores a imagem tinha, para avisar quando a redução mudou o desenho.</summary>
    public static int ImportLogo(Project project, RgbaImage image)
    {
        if (!CanEditLogo(project)) throw new InvalidOperationException(Loc.T("core.face.noLogo"));
        if (image.Width > LogoWidth || image.Height > LogoHeight) throw new ArgumentException(Loc.T("core.face.logoSize", LogoWidth, LogoHeight));

        var browser = NarcBrowser.Open(TitlePath(project));
        var (tilesFile, paletteFile) = LogoFiles(project);
        var reduced = ColorQuantizer.Reduce(Centered(image, LogoWidth, LogoHeight), LogoColors);
        var tiles = PatchLogoTiles(browser.Raw(tilesFile), reduced.Indices);
        var palette = PatchLogoPalette(browser.Raw(paletteFile), reduced.Palette);
        var screen = LogoScreenOnly(browser.Raw(LogoScreen));
        browser.Replace(tilesFile, tiles);
        browser.Replace(paletteFile, palette);
        browser.Replace(LogoScreen, screen);
        // o desenho novo não tem a palavra VERSION no lugar conhecido: daqui em diante a marca é a simples
        File.Delete(LogoBaselinePath(project));
        File.WriteAllText(CustomLogoMark(project), "");
        return reduced.OriginalColors;
    }

    private static RgbaImage Centered(RgbaImage image, int width, int height)
    {
        var canvas = new RgbaImage(width, height);
        int left = (width - image.Width) / 2, top = (height - image.Height) / 2;
        for (int y = 0; y < image.Height; y++)
            Array.Copy(image.Pixels, y * image.Width * 4, canvas.Pixels, ((y + top) * width + left) * 4, image.Width * 4);
        return canvas;
    }

    /// <summary>Grava os pixels de 8 bits, em tiles de 8×8, por cima dos do NCGR. O primeiro tile fica vazio, porque o resto da tela aponta para ele.</summary>
    public static byte[] PatchLogoTiles(byte[] ncgr, byte[] indices)
    {
        const int depthAt = 0x1C, sizeAt = 0x28, dataAt = 0x30, eightBit = 4;
        int size = LogoTilesWide * LogoTilesHigh * 64;
        if (Encoding.ASCII.GetString(ncgr, 0, 4) != "RGCN" || BitConverter.ToInt32(ncgr, depthAt) != eightBit || BitConverter.ToInt32(ncgr, sizeAt) != size)
            throw new InvalidDataException(Loc.T("core.face.badLogo"));
        var result = (byte[])ncgr.Clone();
        for (int tile = 0; tile < LogoTilesWide * LogoTilesHigh; tile++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int source = ((tile / LogoTilesWide * 8 + y) * LogoWidth) + tile % LogoTilesWide * 8 + x;
                    result[dataAt + tile * 64 + y * 8 + x] = tile == 0 ? (byte)0 : indices[source];
                }
        return result;
    }

    /// <summary>Grava as cores do logo a partir do índice 1 e mantém o resto da paleta.</summary>
    public static byte[] PatchLogoPalette(byte[] nclr, IReadOnlyList<ushort> colors)
    {
        const int colorsAt = 0x28;
        if (Encoding.ASCII.GetString(nclr, 0, 4) != "RLCN" || colors.Count > LogoColors || nclr.Length < colorsAt + (LogoColors + 1) * 2)
            throw new InvalidDataException(Loc.T("core.face.badLogo"));
        var result = (byte[])nclr.Clone();
        for (int i = 0; i < colors.Count; i++)
            BitConverter.TryWriteBytes(result.AsSpan(colorsAt + (i + 1) * 2), colors[i]);
        return result;
    }

    /// <summary>Deixa no mapa só a área do logo, em ordem. O original tem restos acima dela que apareceriam como pedaços da arte nova.</summary>
    public static byte[] LogoScreenOnly(byte[] nscr)
    {
        const int entriesAt = 0x24, rowWidth = 32;
        if (Encoding.ASCII.GetString(nscr, 0, 4) != "RCSN" || nscr.Length < entriesAt + rowWidth * (LogoFirstRow + LogoTilesHigh) * 2)
            throw new InvalidDataException(Loc.T("core.face.badLogo"));
        var result = (byte[])nscr.Clone();
        int entries = (nscr.Length - entriesAt) / 2;
        for (int i = 0; i < entries; i++)
        {
            int row = i / rowWidth - LogoFirstRow;
            ushort tile = row >= 0 && row < LogoTilesHigh ? (ushort)(row * rowWidth + i % rowWidth) : (ushort)0;
            BitConverter.TryWriteBytes(result.AsSpan(entriesAt + i * 2), tile);
        }
        return result;
    }

    // áreas livres nos dois logos originais, à direita de VERSION: uma alta, para letra grande, e uma baixa, para versões de mais dígitos
    private const int StampX = 182, StampTallY = 106, StampTallWidth = 18, StampWideY = 112, StampWideWidth = 26;
    private static readonly Dictionary<char, string[]> StampFont = new()
    {
        ['V'] = ["X...X", "X...X", "X...X", "X...X", ".X.X.", ".X.X.", "..X.."],
        ['0'] = ["XXX", "X.X", "X.X", "X.X", "X.X", "X.X", "XXX"],
        ['1'] = [".X.", "XX.", ".X.", ".X.", ".X.", ".X.", "XXX"],
        ['2'] = ["XXX", "..X", "..X", "XXX", "X..", "X..", "XXX"],
        ['3'] = ["XXX", "..X", "..X", "XXX", "..X", "..X", "XXX"],
        ['4'] = ["X.X", "X.X", "X.X", "XXX", "..X", "..X", "..X"],
        ['5'] = ["XXX", "X..", "X..", "XXX", "..X", "..X", "XXX"],
        ['6'] = ["XXX", "X..", "X..", "XXX", "X.X", "X.X", "XXX"],
        ['7'] = ["XXX", "..X", "..X", ".X.", ".X.", ".X.", ".X."],
        ['8'] = ["XXX", "X.X", "X.X", "XXX", "X.X", "X.X", "XXX"],
        ['9'] = ["XXX", "X.X", "X.X", "XXX", "..X", "..X", "XXX"],
    };

    /// <summary>A versão atual, lida do fim do nome do cartão.</summary>
    public static string CurrentVersion(Project project) =>
        CanEditBanner(project) && Regex.Match(ReadBanner(project).Name, @" (V\d+)$") is { Success: true } m ? m.Groups[1].Value : "";

    // números no estilo de VERSION: só o miolo, porque o contorno sai da distância até ele
    private static readonly Dictionary<char, string[]> StyledFont = new()
    {
        ['0'] = [".XXXXX.", "XXX.XXX", "XX...XX", "XX...XX", "XX...XX", "XX...XX", "XX...XX", "XXX.XXX", ".XXXXX."],
        ['1'] = [".XX", "XXX", ".XX", ".XX", ".XX", ".XX", ".XX", ".XX", ".XX"],
        ['2'] = [".XXXXX.", "XXX.XXX", ".....XX", "....XXX", "..XXXX.", ".XXX...", "XXX....", "XXXXXXX", "XXXXXXX"],
        ['3'] = [".XXXXX.", "XXX.XXX", ".....XX", "...XXX.", "...XXX.", ".....XX", ".....XX", "XXX.XXX", ".XXXXX."],
        ['4'] = ["...XXX.", "..XXXX.", ".XXXXX.", "XXX.XX.", "XX..XX.", "XXXXXXX", "XXXXXXX", "....XX.", "....XX."],
        ['5'] = ["XXXXXXX", "XXXXXXX", "XX.....", "XXXXXX.", "XXXXXXX", ".....XX", ".....XX", "XXX.XXX", ".XXXXX."],
        ['6'] = [".XXXXX.", "XXX.XXX", "XX.....", "XXXXXX.", "XXXXXXX", "XX...XX", "XX...XX", "XXX.XXX", ".XXXXX."],
        ['7'] = ["XXXXXXX", "XXXXXXX", ".....XX", "....XXX", "...XXX.", "..XXX..", "..XX...", "..XX...", "..XX..."],
        ['8'] = [".XXXXX.", "XXX.XXX", "XX...XX", "XXX.XXX", ".XXXXX.", "XXX.XXX", "XX...XX", "XXX.XXX", ".XXXXX."],
        ['9'] = [".XXXXX.", "XXX.XXX", "XX...XX", "XX...XX", "XXXXXXX", ".XXXXXX", ".....XX", "XXX.XXX", ".XXXXX."],
    };

    private const int WordLeft = 64, WordLeftTop = 69, WordRight = 177, WordTop = 101, WordTopRightFrom = 165, WordTopRight = 104, WordBottom = 119;
    private const int ArtVLeft = 69, ArtVWidth = 18, StyledBodyTop = 106, RampColumn = 138, EdgeColumn = 135;
    private const int StampGap = 2, StampRightLimit = 199;

    private static string CustomLogoMark(Project project) => Path.Combine(project.Paths.Workspace, "pokehub-logo-custom.txt");
    private static string LogoBaselinePath(Project project) => Path.Combine(project.Paths.Workspace, "pokehub-logo-original.ncgr");

    /// <summary>Marca a versão no logo e, quando há cartão, no fim do nome dele.</summary>
    public static void ApplyVersion(Project project, string version)
    {
        if (!CanEditLogo(project)) throw new InvalidOperationException(Loc.T("core.face.noLogo"));
        version = version.Trim().ToUpperInvariant();
        var browser = NarcBrowser.Open(TitlePath(project));
        var (tilesFile, paletteFile) = LogoFiles(project);
        byte[] tiles;
        if (File.Exists(CustomLogoMark(project)) || version.Length > 3)
            tiles = StampLogoVersion(browser.Raw(tilesFile), browser.Raw(paletteFile), version);
        else
        {
            // sempre a partir do desenho original, para que subir a versão não mova VERSION duas vezes
            if (!File.Exists(LogoBaselinePath(project))) File.WriteAllBytes(LogoBaselinePath(project), WithoutPlainStamp(browser.Raw(tilesFile)));
            var original = File.ReadAllBytes(LogoBaselinePath(project));
            // o logo do HeartGold tem outro recorte, então pode não caber
            try { tiles = StampStyledVersion(original, version); }
            catch (ArgumentException) when (Regex.IsMatch(version, @"^V[0-9]{1,2}$")) { tiles = StampLogoVersion(original, browser.Raw(paletteFile), version); }
        }
        browser.Replace(tilesFile, tiles);
        if (!CanEditBanner(project)) return;
        var banner = ReadBanner(project);
        WriteBanner(project, banner with { Name = WithVersion(banner.Name, version.Trim().ToUpperInvariant()) });
    }

    /// <summary>Troca ou acrescenta o V1...Vn no fim de um nome.</summary>
    public static string WithVersion(string name, string version) => Regex.Replace(name.Trim(), @" V\d+$", "") + " " + version;

    /// <summary>
    /// Escreve a versão como continuação de VERSION: o V vem do próprio desenho, e os números usam o degradê e o contorno da letra I.
    /// Move a palavra para a esquerda o quanto for preciso para caber antes da cauda da Lugia.
    /// </summary>
    public static byte[] StampStyledVersion(byte[] original, string version)
    {
        version = version.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(version, @"^V[0-9]{1,2}$")) throw new ArgumentException(Loc.T("core.face.badVersion"));
        const int tilesAt = 0x30;
        if (Encoding.ASCII.GetString(original, 0, 4) != "RGCN" || BitConverter.ToInt32(original, 0x1C) != 4 || BitConverter.ToInt32(original, 0x28) != LogoTilesWide * LogoTilesHigh * 64)
            throw new InvalidDataException(Loc.T("core.face.badLogo"));

        int At(int x, int y) => tilesAt + (y / 8 * LogoTilesWide + x / 8) * 64 + y % 8 * 8 + x % 8;
        byte Old(int x, int y) => original[At(x, y)];
        bool InWord(int x, int y) => x >= (y >= WordTopRight ? WordLeft : WordLeftTop) && x <= WordRight && y >= (x >= WordTopRightFrom ? WordTopRight : WordTop) && y <= WordBottom;

        const int ring = 2;
        var digits = version[1..].Select(c => StyledFont[c]).ToList();
        int width = StampGap + ArtVWidth + digits.Sum(g => g[0].Length + ring * 2 - 1);
        int shift = Math.Max(0, WordRight + width - StampRightLimit);

        var result = (byte[])original.Clone();
        var moved = new List<(int X, int Y, byte Value)>();
        for (int y = WordTop; y <= WordBottom; y++)
            for (int x = WordLeft; x <= WordRight; x++)
                if (InWord(x, y))
                {
                    if (Old(x, y) != 0) moved.Add((x - shift, y, Old(x, y)));
                    result[At(x, y)] = 0;
                }
        void Draw(int x, int y, byte value)
        {
            // nada pode cair em cima do resto do logo
            if (x < 0 || x >= LogoWidth || y < 0 || y >= LogoHeight || (Old(x, y) != 0 && !InWord(x, y))) throw new ArgumentException(Loc.T("core.face.versionNoRoom", version));
            result[At(x, y)] = value;
        }
        foreach (var (x, y, value) in moved) Draw(x, y, value);

        int left = WordRight - shift + 1 + StampGap;
        for (int y = WordTop; y <= WordBottom; y++)
            for (int x = 0; x < ArtVWidth; x++)
                if (y >= WordTopRight && Old(ArtVLeft + x, y) != 0) Draw(left + x, y, Old(ArtVLeft + x, y));
        left += ArtVWidth - 1;

        byte highlight = Old(EdgeColumn, 107), silver = Old(EdgeColumn, 110), edge = Old(EdgeColumn, 116);
        foreach (var glyph in digits)
        {
            int w = glyph[0].Length + ring * 2, h = glyph.Length + ring * 2;
            bool Body(int x, int y) => x >= ring && y >= ring && x < w - ring && y < h - ring && glyph[y - ring][x - ring] == 'X';
            int Near(int x, int y)
            {
                for (int r = 0; r <= ring; r++)
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                            if (Body(x + dx, y + dy)) return r;
                return ring + 1;
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int distance = Near(x, y), row = StyledBodyTop - ring + y;
                    if (distance == 0) Draw(left + x, row, Old(RampColumn, row));
                    else if (distance == 1) Draw(left + x, row, Near(x, y + 1) == 0 ? highlight : silver);
                    else if (distance == 2 && result[At(left + x, row)] == 0 && Old(left + x, row) == 0) Draw(left + x, row, edge);
                }
            left += w - 1;
        }
        return result;
    }

    /// <summary>Tira a marca simples do logo, que ficava em áreas vazias no original.</summary>
    public static byte[] WithoutPlainStamp(byte[] ncgr)
    {
        var result = (byte[])ncgr.Clone();
        for (int y = StampTallY; y < LogoHeight; y++)
            for (int x = StampX; x < StampX + (y < StampWideY ? StampTallWidth : StampWideWidth); x++)
                result[0x30 + (y / 8 * LogoTilesWide + x / 8) * 64 + y % 8 * 8 + x % 8] = 0;
        return result;
    }

    /// <summary>Desenha a versão à direita de VERSION, na cor mais clara do logo, e limpa a marca anterior. A paleta e o mapa não mudam.</summary>
    public static byte[] StampLogoVersion(byte[] ncgr, byte[] nclr, string version)
    {
        version = version.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(version, @"^V[0-9]{1,3}$")) throw new ArgumentException(Loc.T("core.face.badVersion"));
        const int tilesAt = 0x30, colorsAt = 0x28;
        if (Encoding.ASCII.GetString(ncgr, 0, 4) != "RGCN" || BitConverter.ToInt32(ncgr, 0x1C) != 4 || BitConverter.ToInt32(ncgr, 0x28) != LogoTilesWide * LogoTilesHigh * 64
            || Encoding.ASCII.GetString(nclr, 0, 4) != "RLCN")
            throw new InvalidDataException(Loc.T("core.face.badLogo"));

        byte bright = 1;
        int brightest = -1;
        for (int i = 1; i <= LogoColors; i++)
        {
            ushort c = BitConverter.ToUInt16(nclr, colorsAt + i * 2);
            int light = (c & 31) + ((c >> 5) & 31) + ((c >> 10) & 31);
            if (light > brightest) { brightest = light; bright = (byte)i; }
        }

        var result = (byte[])ncgr.Clone();
        void Put(int x, int y, byte value)
        {
            int tile = y / 8 * LogoTilesWide + x / 8;
            result[tilesAt + tile * 64 + y % 8 * 8 + x % 8] = value;
        }
        for (int y = StampTallY; y < LogoHeight; y++)
            for (int x = StampX; x < StampX + StampTallWidth; x++) Put(x, y, 0);
        for (int y = StampWideY; y < LogoHeight; y++)
            for (int x = StampX; x < StampX + StampWideWidth; x++) Put(x, y, 0);

        int Width(int size) => version.Sum(c => StampFont[c][0].Length * size) + version.Length - 1;
        int scale = Width(2) <= StampTallWidth ? 2 : 1;
        int left = StampX, top = scale == 2 ? StampTallY : StampWideY;
        foreach (var c in version)
        {
            var glyph = StampFont[c];
            for (int row = 0; row < glyph.Length; row++)
                for (int column = 0; column < glyph[row].Length; column++)
                    if (glyph[row][column] == 'X')
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                Put(left + column * scale + dx, top + row * scale + dy, bright);
            left += glyph[0].Length * scale + 1;
        }
        return result;
    }

    private static string BannerYaml(Project project) => Path.Combine(project.Paths.Workspace, "banner", "banner.yaml");
    private static string HeaderYaml(Project project) => Path.Combine(project.Paths.Workspace, "header.yaml");
    private static string IconPath(Project project) => Path.Combine(project.Paths.Workspace, "banner", "bitmap.png");

    public static string CurrentIconPath(Project project) => IconPath(project);

    public static BannerText ReadBanner(Project project)
    {
        var yaml = File.ReadAllText(BannerYaml(project));
        var block = Regex.Match(yaml, @"^  english: \|-\r?\n((?:    .*\r?\n?)+)", RegexOptions.Multiline);
        var lines = block.Groups[1].Value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var title = Regex.Match(File.ReadAllText(HeaderYaml(project)), @"^title: ""?(.*?)""?\s*$", RegexOptions.Multiline).Groups[1].Value;
        return new BannerText(lines.ElementAtOrDefault(0) ?? "", lines.ElementAtOrDefault(1) ?? "", title);
    }

    public static void WriteBanner(Project project, BannerText text)
    {
        if (!CanEditBanner(project)) throw new InvalidOperationException(Loc.T("core.face.noBanner"));
        var banner = File.ReadAllText(BannerYaml(project));
        var header = File.ReadAllText(HeaderYaml(project));
        File.WriteAllText(BannerYaml(project), WithBannerText(banner, text.Name, text.Subtitle));
        File.WriteAllText(HeaderYaml(project), WithRomTitle(header, text.RomTitle));
    }

    /// <summary>O nome é o mesmo nos seis idiomas do cartão. O DS guarda 128 caracteres em até três linhas.</summary>
    public static string WithBannerText(string yaml, string name, string subtitle)
    {
        name = name.Trim();
        subtitle = subtitle.Trim();
        if (name.Length == 0 || name.Contains('\n') || subtitle.Contains('\n') || name.Length + subtitle.Length + 1 > 128) throw new ArgumentException(Loc.T("core.face.badName"));
        string newline = yaml.Contains("\r\n") ? "\r\n" : "\n";
        string lines = subtitle.Length > 0 ? $"    {name}{newline}    {subtitle}" : $"    {name}";
        return Regex.Replace(yaml, @"^(  (?:japanese|english|french|german|italian|spanish): \|-\r?\n)(?:    .*(?:\r?\n|$))+?(?=  \w|images:|\Z)",
            m => m.Groups[1].Value + lines + newline, RegexOptions.Multiline);
    }

    /// <summary>O título interno da ROM tem até 12 caracteres ASCII maiúsculos.</summary>
    public static string WithRomTitle(string yaml, string title)
    {
        title = title.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(title, @"^[A-Z0-9 ._-]{1,12}$")) throw new ArgumentException(Loc.T("core.face.badRomTitle"));
        return Regex.Replace(yaml, @"^title: .*?(\r?)$", m => $"title: \"{title}\"{m.Groups[1].Value}", RegexOptions.Multiline);
    }

    /// <summary>Troca o ícone do cartão: 32×32, com 15 cores e o transparente.</summary>
    public static int ImportIcon(Project project, RgbaImage image)
    {
        if (!CanEditBanner(project)) throw new InvalidOperationException(Loc.T("core.face.noBanner"));
        if (image.Width != IconSize || image.Height != IconSize) throw new ArgumentException(Loc.T("core.face.iconSize", IconSize));
        var (bitmap, palette, colors) = BuildIcon(image);
        var folder = Path.Combine(project.Paths.Workspace, "banner");
        File.WriteAllBytes(Path.Combine(folder, "bitmap.png"), EncodePng(bitmap));
        File.WriteAllBytes(Path.Combine(folder, "palette.png"), EncodePng(palette));
        return colors;
    }

    /// <summary>O ds-rom procura cada pixel do ícone na paleta, então os dois saem das mesmas cores, com canais múltiplos de 8 como os que ele extrai.</summary>
    public static (RgbaImage Bitmap, RgbaImage Palette, int OriginalColors) BuildIcon(RgbaImage image)
    {
        var reduced = ColorQuantizer.Reduce(image, IconColors);
        var palette = new RgbaImage(16, 1);
        (byte, byte, byte) Rgb(ushort c) => ((byte)((c & 31) << 3), (byte)(((c >> 5) & 31) << 3), (byte)(((c >> 10) & 31) << 3));
        palette.Set(0, 0, 0, 0, 0, 0);
        for (int i = 1; i < 16; i++)
        {
            var (r, g, b) = i <= reduced.Palette.Length ? Rgb(reduced.Palette[i - 1]) : ((byte)0, (byte)0, (byte)0);
            palette.Set(i, 0, r, g, b);
        }
        var bitmap = new RgbaImage(IconSize, IconSize);
        for (int y = 0; y < IconSize; y++)
            for (int x = 0; x < IconSize; x++)
            {
                int index = reduced.Indices[y * IconSize + x];
                if (index == 0) { bitmap.Set(x, y, 0, 0, 0, 0); continue; }
                var (r, g, b) = Rgb(reduced.Palette[index - 1]);
                bitmap.Set(x, y, r, g, b);
            }
        return (bitmap, palette, reduced.OriginalColors);
    }

    /// <summary>PNG RGBA de 8 bits, sem filtro: é o que o ds-rom lê de volta.</summary>
    public static byte[] EncodePng(RgbaImage image)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)image.Width);
        WriteBigEndian(header, 4, (uint)image.Height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            for (int y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(image.Pixels, y * image.Width * 4, image.Width * 4);
            }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteBigEndian(byte[] target, int at, uint value)
    {
        target[at] = (byte)(value >> 24);
        target[at + 1] = (byte)(value >> 16);
        target[at + 2] = (byte)(value >> 8);
        target[at + 3] = (byte)value;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var word = new byte[4];
        WriteBigEndian(word, 0, (uint)data.Length);
        output.Write(word);
        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        output.Write(body);
        uint crc = 0xFFFFFFFF;
        foreach (var b in body)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        WriteBigEndian(word, 0, ~crc);
        output.Write(word);
    }
}
