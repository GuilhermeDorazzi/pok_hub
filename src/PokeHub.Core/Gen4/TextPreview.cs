using PokeHub.Core.Graphics;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Fonte do jogo (arquivo 1 do NARC de fontes é a das mensagens): cabeçalho, glifos de 16×16 em 2 bits por pixel e uma largura por glifo.
/// Cada glifo são 4 blocos de 8×8, com uma palavra de 16 bits por linha, lida do bit alto para o baixo. O glifo de um caractere é o código menos 1.
/// </summary>
public sealed class Gen4Font
{
    private const int MessageFontFile = 1, HeaderFields = 16, GlyphBytes = 64;
    private readonly byte[] _data;
    private readonly int _headerSize, _widths, _count;

    public Gen4Font(byte[] data)
    {
        if (data.Length < HeaderFields) throw new InvalidDataException("Fonte sem cabeçalho.");
        _data = data;
        _headerSize = BitConverter.ToInt32(data, 0);
        _widths = BitConverter.ToInt32(data, 4);
        _count = BitConverter.ToInt32(data, 8);
        if (_headerSize + (long)_count * GlyphBytes > data.Length || _widths + (long)_count > data.Length) throw new InvalidDataException("Fonte cortada.");
    }

    /// <summary>null se o jogo não tiver o arquivo: o editor segue funcionando, só sem a prévia.</summary>
    public static Gen4Font? Load(Gen4Game game)
    {
        if (!File.Exists(game.FontPath)) return null;
        var narc = Narc.Read(File.ReadAllBytes(game.FontPath));
        return narc.Files.Count > MessageFontFile ? new Gen4Font(narc.Files[MessageFontFile]) : null;
    }

    private static int Glyph(ushort code) => code - 1;

    public int WidthOf(ushort code) => Glyph(code) is var g && g >= 0 && g < _count ? _data[_widths + g] : 0;

    public void Draw(RgbaImage image, int x, int y, ushort code)
    {
        int glyph = Glyph(code);
        if (glyph < 0 || glyph >= _count) return;
        int start = _headerSize + glyph * GlyphBytes;
        for (int py = 0; py < 16; py++)
            for (int px = 0; px < 16; px++)
            {
                int block = py / 8 * 2 + px / 8;
                ushort row = BitConverter.ToUInt16(_data, start + block * 16 + py % 8 * 2);
                int color = (row >> (14 - px % 8 * 2)) & 3;
                int ox = x + px, oy = y + py;
                if (ox >= image.Width || oy >= image.Height) continue;
                if (color == 1) image.Set(ox, oy, 72, 72, 72);
                else if (color == 2) image.Set(ox, oy, 200, 200, 200);
            }
    }
}

/// <summary>Largura de uma linha da prévia, em pixels.</summary>
public sealed record PreviewLine(int Number, int Width, bool TooLong);

/// <summary>
/// Desenha uma mensagem como na caixa de diálogo do jogo: duas linhas por caixa, com \n quebrando a linha, \r rolando e \f limpando a caixa.
/// Nomes variáveis ({STRVAR...}) não têm largura conhecida: viram um bloco laranja do tamanho de um nome de 7 letras.
/// </summary>
public sealed class TextPreview
{
    /// <summary>Largura útil da caixa de diálogo do mapa: 27 tiles.</summary>
    public const int BoxWidth = 27 * 8;
    private const int LineHeight = 16, Margin = 4, LinesPerBox = 2, VariableWidth = 42, VariableCommandGroup = 0x01;

    private readonly record struct Item(ushort Code, bool IsVariable);

    public required RgbaImage Image { get; init; }
    public required IReadOnlyList<PreviewLine> Lines { get; init; }
    public bool HasOverflow => Lines.Any(l => l.TooLong);

    public static TextPreview Render(IReadOnlyList<ushort> codes, Gen4Font font, CharMap? map = null, int maxWidth = BoxWidth)
    {
        map ??= CharMap.Default;
        map.TryCode("\\n", out var newline);
        map.TryCode("\\r", out var scroll);
        map.TryCode("\\f", out var clear);

        var lines = new List<List<Item>> { new() };
        var startsBox = new HashSet<int> { 0 };
        for (int i = 0; i < codes.Count; i++)
        {
            ushort c = codes[i];
            if (c == CharMap.CommandMarker)
            {
                if (i + 2 >= codes.Count) break;
                ushort command = codes[++i];
                int args = codes[++i];
                i += args;
                if (command >> 8 == VariableCommandGroup) lines[^1].Add(new Item(0, true)); // cor, tamanho e pausa não ocupam espaço
                continue;
            }
            if (c == newline || c == scroll) { lines.Add([]); continue; }
            if (c == clear) { lines.Add([]); startsBox.Add(lines.Count - 1); continue; }
            lines[^1].Add(new Item(c, false));
        }

        int Width(List<Item> line) => line.Sum(it => it.IsVariable ? VariableWidth : font.WidthOf(it.Code));
        var widths = lines.Select(Width).ToList();

        // cada caixa começa num \f ou quando a anterior já tem duas linhas
        var tops = new int[lines.Count];
        int boxes = 1, inBox = 0;
        for (int l = 0; l < lines.Count; l++)
        {
            if (l > 0 && (startsBox.Contains(l) || inBox == LinesPerBox)) { boxes++; inBox = 0; }
            tops[l] = Margin + (boxes - 1) * (LinesPerBox * LineHeight + Margin * 3) + inBox * LineHeight;
            inBox++;
        }

        int imageWidth = Math.Max(maxWidth, widths.Max()) + Margin * 2;
        int imageHeight = boxes * (LinesPerBox * LineHeight + Margin * 3) + Margin;
        var image = new RgbaImage(imageWidth, imageHeight);
        for (int y = 0; y < imageHeight; y++)
            for (int x = 0; x < imageWidth; x++)
                image.Set(x, y, 255, 255, 255);
        // o que passa da caixa fica destacado
        if (imageWidth > maxWidth + Margin * 2)
            for (int y = 0; y < imageHeight; y++)
                for (int x = maxWidth + Margin; x < imageWidth; x++)
                    image.Set(x, y, 255, 210, 205);
        // separadores entre as caixas
        for (int b = 1; b < boxes; b++)
        {
            int y = b * (LinesPerBox * LineHeight + Margin * 3) - Margin;
            for (int x = 0; x < imageWidth; x++) image.Set(x, y, 190, 190, 190);
        }

        var result = new List<PreviewLine>();
        for (int l = 0; l < lines.Count; l++)
        {
            int x = Margin, y = tops[l];
            foreach (var item in lines[l])
            {
                if (item.IsVariable)
                {
                    for (int py = 3; py < 13; py++)
                        for (int px = 0; px < VariableWidth - 1; px++)
                            if (x + px < imageWidth && y + py < imageHeight) image.Set(x + px, y + py, 245, 170, 80);
                    x += VariableWidth;
                    continue;
                }
                font.Draw(image, x, y, item.Code);
                x += font.WidthOf(item.Code);
            }
            result.Add(new PreviewLine(l + 1, widths[l], widths[l] > maxWidth));
        }
        return new TextPreview { Image = image, Lines = result };
    }
}
