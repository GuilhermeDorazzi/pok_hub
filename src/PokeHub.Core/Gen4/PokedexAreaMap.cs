using System.Reflection;
using System.Text.Json;
using PokeHub.Core.Graphics;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>Retângulo de uma área no mapa do Pokégear, em tiles de 8 px.</summary>
public sealed record PokedexAreaShape(int Id, int X, int Y, int Width, int Height)
{
    public bool Covers(int tileX, int tileY) => tileX >= X && tileY >= Y && tileX < X + Width && tileY < Y + Height;
}

/// <summary>
/// Mapa de Johto/Kanto para o editor de Pokédex do HGSS: o mapa do Pokégear lido da própria ROM
/// (NARC a/1/4/4 = pgmap_gra do pokeheartgold: NSCR 11 + NCGR 10 + NCLR 20, tilemap 512×512 do qual a parte de cima
/// é o mapa) e, para cada área da Pokédex, o retângulo que o Pokégear usa para esse local (tabela sLocationSpecs do
/// pokeheartgold, ligada às áreas por sOverworldMapIDs/sDungeonMapIDs; embutida em pokedex-areamap-hgss.json porque
/// no jogo ela fica dentro do overlay, não num arquivo de dados). GEN5: BW usam outro formato de mapa.
/// </summary>
public sealed class PokedexAreaMap
{
    public const string GraphicsNarcRelativePath = @"a\1\4\4";
    public const int ScreenFile = 11, TilesFile = 10, PaletteFile = 20;
    public const int Tile = 8;
    /// <summary>Linhas de tiles do tilemap que formam o mapa (o resto são peças de ícones).</summary>
    public const int Rows = 20;

    public RgbaImage Image { get; }
    public IReadOnlyList<PokedexAreaShape?> Overworlds { get; }
    public IReadOnlyList<PokedexAreaShape?> Dungeons { get; }

    private PokedexAreaMap(RgbaImage image, List<PokedexAreaShape?> overworlds, List<PokedexAreaShape?> dungeons)
    {
        Image = image; Overworlds = overworlds; Dungeons = dungeons;
    }

    private sealed class Table { public List<Rect?> Overworlds { get; set; } = []; public List<Rect?> Dungeons { get; set; } = []; }
    private sealed class Rect { public int X { get; set; } public int Y { get; set; } public int W { get; set; } public int H { get; set; } }

    private static (List<PokedexAreaShape?> Overworlds, List<PokedexAreaShape?> Dungeons) LoadTable()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("pokedex-areamap-hgss.json")
            ?? throw new InvalidOperationException("pokedex-areamap-hgss.json embutido não encontrado.");
        var t = JsonSerializer.Deserialize<Table>(stream, HubConfig.JsonOptions)!;
        List<PokedexAreaShape?> Convert(List<Rect?> rects) => rects.Select((r, i) => r is null ? null : new PokedexAreaShape(i, r.X, r.Y, r.W, r.H)).ToList();
        return (Convert(t.Overworlds), Convert(t.Dungeons));
    }

    public static PokedexAreaMap Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (!game.IsHgss) throw new NotSupportedException(Loc.T("core.dex.mapHgssOnly"));
        var gfxPath = Path.Combine(game.FilesRoot, GraphicsNarcRelativePath);
        if (!File.Exists(gfxPath)) throw new FileNotFoundException(Loc.T("core.dex.mapMissing"), gfxPath);
        var gfx = Narc.Read(File.ReadAllBytes(gfxPath));
        if (gfx.Files.Count <= PaletteFile) throw new InvalidDataException(Loc.T("core.dex.mapMissing"));
        var screen = NitroScreen.Read(Lz10.DecompressIfNeeded(gfx.Files[ScreenFile]));
        var tiles = NitroTiles.Read(Lz10.DecompressIfNeeded(gfx.Files[TilesFile]));
        var palette = NitroPalette.Read(Lz10.DecompressIfNeeded(gfx.Files[PaletteFile]));
        var full = screen.Render(tiles, palette);
        var (overworlds, dungeons) = LoadTable();
        // largura útil: até a última coluna ocupada por alguma área (o resto do tilemap é sobra)
        int columns = overworlds.Concat(dungeons).Where(s => s is not null).Max(s => s!.X + s.Width) + 1;
        var image = full.Crop(0, 0, Math.Min(columns * Tile, full.Width), Math.Min(Rows * Tile, full.Height));
        return new PokedexAreaMap(image, overworlds, dungeons);
    }

    public PokedexAreaShape? Shape(uint areaId, bool special)
    {
        var list = special ? Dungeons : Overworlds;
        return areaId < list.Count ? list[(int)areaId] : null;
    }

    /// <summary>Área (id) sob um tile, dentro de um conjunto de ids candidatos; null se nenhuma. A menor área ganha.</summary>
    public uint? HitTest(int tileX, int tileY, bool special, IEnumerable<uint> candidates)
    {
        uint? best = null; int bestArea = int.MaxValue;
        foreach (var id in candidates)
            if (Shape(id, special) is { } s && s.Covers(tileX, tileY) && s.Width * s.Height < bestArea) { best = id; bestArea = s.Width * s.Height; }
        return best;
    }
}
