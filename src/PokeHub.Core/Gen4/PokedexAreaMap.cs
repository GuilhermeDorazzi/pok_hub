using PokeHub.Core.Graphics;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>Forma de uma área no mapa da Pokédex, em tiles de 8 px: retângulo com máscara (áreas normais) ou ponto (especiais).</summary>
public sealed record PokedexAreaShape(int Id, int X, int Y, int Width, int Height, bool[] Mask)
{
    public bool Covers(int tileX, int tileY)
    {
        int dx = tileX - X, dy = tileY - Y;
        if (dx < 0 || dy < 0 || dx >= Width || dy >= Height) return false;
        int i = dy * Width + dx;
        return i < Mask.Length && Mask[i];
    }
}

/// <summary>
/// Mapa da tela de áreas da Pokédex do HGSS, lido da própria ROM: tilemap NSCR 86 + tiles NCGR 87 + paleta NCLR 18 de
/// a/0/6/8 (zukan_gra), e as formas das áreas nos dois primeiros arquivos de a/1/3/3 (gDungeons: x,y; gOverworlds: x,y,w,h + máscara de 32),
/// conforme o pokeheartgold. O jogo rola esse tilemap para x = 72 (Johto) e x = -104 (Kanto); as colunas 0..44 cobrem as duas regiões.
/// GEN5: BW usam outro formato de mapa.
/// </summary>
public sealed class PokedexAreaMap
{
    public const string GraphicsNarcRelativePath = @"a\0\6\8";
    public const int ScreenFile = 86, TilesFile = 87, PaletteFile = 18;
    public const int Columns = 45, Rows = 16, Tile = 8;
    /// <summary>Colunas a partir desta pertencem a Kanto (o jogo testa x >= 22).</summary>
    public const int KantoFirstColumn = 22;

    public RgbaImage Image { get; }
    public IReadOnlyList<PokedexAreaShape> Overworlds { get; }
    public IReadOnlyList<PokedexAreaShape> Dungeons { get; }

    private PokedexAreaMap(RgbaImage image, List<PokedexAreaShape> overworlds, List<PokedexAreaShape> dungeons)
    {
        Image = image; Overworlds = overworlds; Dungeons = dungeons;
    }

    public static PokedexAreaMap Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (!game.IsHgss) throw new NotSupportedException(Loc.T("core.dex.hgssOnly"));
        var gfxPath = Path.Combine(game.FilesRoot, GraphicsNarcRelativePath);
        var areasPath = Path.Combine(game.FilesRoot, PokedexAreaRepository.HgssNarcRelativePath);
        if (!File.Exists(gfxPath) || !File.Exists(areasPath)) throw new FileNotFoundException(Loc.T("core.dex.mapMissing"), gfxPath);

        var gfx = Narc.Read(File.ReadAllBytes(gfxPath));
        if (gfx.Files.Count <= ScreenFile) throw new InvalidDataException(Loc.T("core.dex.mapMissing"));
        var screen = NitroScreen.Read(Lz10.DecompressIfNeeded(gfx.Files[ScreenFile]));
        var tiles = NitroTiles.Read(Lz10.DecompressIfNeeded(gfx.Files[TilesFile]));
        var palette = NitroPalette.Read(Lz10.DecompressIfNeeded(gfx.Files[PaletteFile]));
        var full = screen.Render(tiles, palette);
        var image = full.Crop(0, 0, Math.Min(Columns * Tile, full.Width), Math.Min(Rows * Tile, full.Height));

        var areas = Narc.Read(File.ReadAllBytes(areasPath));
        var dungeons = new List<PokedexAreaShape>();
        var d = areas.Files[0];
        for (int i = 0; i + 4 <= d.Length; i += 4)
            dungeons.Add(new(i / 4, d[i], d[i + 1], 1, 1, [true]));
        var overworlds = new List<PokedexAreaShape>();
        var o = areas.Files[1];
        for (int i = 0; i + 36 <= o.Length; i += 36)
        {
            int w = Math.Max(1, (int)o[i + 2]), h = Math.Max(1, (int)o[i + 3]);
            var mask = new bool[w * h];
            for (int k = 0; k < mask.Length && k < 32; k++) mask[k] = o[i + 4 + k] != 0;
            overworlds.Add(new(i / 36, o[i], o[i + 1], w, h, mask));
        }
        return new PokedexAreaMap(image, overworlds, dungeons);
    }

    public PokedexAreaShape? Shape(uint areaId, bool special)
    {
        var list = special ? Dungeons : Overworlds;
        return areaId < list.Count ? list[(int)areaId] : null;
    }

    /// <summary>Área (id) sob um tile, dentro de um conjunto de ids candidatos; null se nenhuma.</summary>
    public uint? HitTest(int tileX, int tileY, bool special, IEnumerable<uint> candidates)
    {
        foreach (var id in candidates)
            if (Shape(id, special) is { } s && s.Covers(tileX, tileY)) return id;
        return null;
    }
}
