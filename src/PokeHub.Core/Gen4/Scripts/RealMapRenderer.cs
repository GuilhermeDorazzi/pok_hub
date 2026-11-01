using PokeHub.Core.Graphics;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>
/// Desenha o mapa como o jogo o mostra, visto de cima: o modelo 3D de cada célula da matriz, com as texturas da área, e os prédios no lugar.
/// A câmera do jogo é inclinada, então telhados e chão não ficam exatamente iguais ao que se vê jogando.
/// </summary>
public sealed class RealMapRenderer
{
    public const int PixelsPerTile = 16;
    private const float WorldPerTile = 16f;
    private const int CellTiles = 32, BuildingRecord = 48;
    /// <summary>Mapas sem célula própria caem na matriz do mundo inteiro: dariam imagens de quase 1 GB e quase um minuto de espera.</summary>
    private const int MaxTiles = 256 * 256;
    private readonly Gen4Game _game;
    private readonly Narc _land, _buildings, _indoorBuildings, _mapTextures, _buildingTextures, _areas;
    private readonly Dictionary<int, NitroTextureSet?> _mapSets = [], _buildingSets = [];
    private readonly Dictionary<(Narc, int), NsbmdModel?> _buildingModels = [];

    private RealMapRenderer(Gen4Game game, Narc land, Narc buildings, Narc indoorBuildings, Narc mapTextures, Narc buildingTextures, Narc areas)
    {
        _game = game; _land = land; _buildings = buildings; _indoorBuildings = indoorBuildings; _mapTextures = mapTextures; _buildingTextures = buildingTextures; _areas = areas;
    }

    /// <summary>null se algum arquivo faltar: a tela continua com a planta.</summary>
    public static RealMapRenderer? TryLoad(Gen4Game game)
    {
        string P(string hgss, string dppt) => Path.Combine(game.FilesRoot, game.IsHgss ? hgss : dppt);
        string[] paths =
        [
            game.LandPath,
            P(@"a\0\4\0", @"fielddata\build_model\build_model.narc"),
            P(@"a\0\4\4", @"fielddata\areadata\area_map_tex\map_tex_set.narc"),
            P(@"a\0\7\0", @"fielddata\areadata\area_build_model\areabm_texset.narc"),
            P(@"a\0\4\2", @"fielddata\areadata\area_data.narc"),
            P(@"a\1\4\8", @"fielddata\build_model\build_model.narc"), // só o HGSS separa os prédios de interior
        ];
        if (paths.Any(p => !File.Exists(p))) return null;
        var n = paths.Select(p => Narc.Read(File.ReadAllBytes(p))).ToArray();
        return new RealMapRenderer(game, n[0], n[1], n[5], n[2], n[3], n[4]);
    }

    private static NitroTextureSet? Set(Dictionary<int, NitroTextureSet?> cache, Narc narc, int index)
    {
        if (cache.TryGetValue(index, out var set)) return set;
        return cache[index] = index >= 0 && index < narc.Files.Count ? NitroTextureSet.TryRead(narc.Files[index]) : null;
    }

    public static bool CanRender(MapLayout layout) => layout.Width * layout.Height <= MaxTiles;

    /// <summary>Uma imagem do tamanho da planta, com 16 pixels por tile; null se o mapa for grande demais ou faltar a área.</summary>
    public RgbaImage? Render(MapLayout layout, int areaId)
    {
        if (!CanRender(layout)) return null;
        if (areaId < 0 || areaId >= _areas.Files.Count || _areas.Files[areaId].Length < 4) return null;
        var area = _areas.Files[areaId];
        var buildingSet = Set(_buildingSets, _buildingTextures, BitConverter.ToUInt16(area, 0));
        var mapSet = Set(_mapSets, _mapTextures, BitConverter.ToUInt16(area, 2));
        if (mapSet is null) return null;
        // AreaDataManager_Alloc: no HGSS, byte 6 zerado quer dizer interior
        var buildings = _game.IsHgss && area.Length > 6 && area[6] == 0 ? _indoorBuildings : _buildings;

        var raster = new TopDownRaster(layout.Width * PixelsPerTile, layout.Height * PixelsPerTile, PixelsPerTile / WorldPerTile, (28, 30, 34));
        foreach (var (land, left, top) in layout.Cells)
        {
            if (land < 0 || land >= _land.Files.Count) continue;
            DrawCell(raster, _land.Files[land], left, top, mapSet, buildingSet, buildings);
        }
        return raster.Image;
    }

    private void DrawCell(TopDownRaster raster, byte[] file, int left, int top, NitroTextureSet mapSet, NitroTextureSet? buildingSet, Narc buildings)
    {
        if (file.Length < 16) return;
        int permissions = BitConverter.ToInt32(file, 0), objects = BitConverter.ToInt32(file, 4), models = BitConverter.ToInt32(file, 8);
        int p = 16;
        if (_game.IsHgss)
        {
            if (file.Length < p + 4) return;
            p += 4 + BitConverter.ToUInt16(file, p + 2);
        }
        int objectsAt = p + permissions, modelAt = objectsAt + objects;
        if (modelAt + models > file.Length) return;

        // o modelo da célula tem a origem no centro da célula
        float centerX = (left + CellTiles / 2f) * WorldPerTile, centerZ = (top + CellTiles / 2f) * WorldPerTile;
        var ground = NsbmdModel.TryRead(file.AsSpan(modelAt, models));
        if (ground is not null) raster.Draw(ground, centerX, 0, centerZ, mapSet.Get);

        if (buildingSet is null) return;
        for (int i = 0; i + BuildingRecord <= objects; i += BuildingRecord)
        {
            int at = objectsAt + i;
            int id = BitConverter.ToInt32(file, at);
            var model = BuildingModel(buildings, id);
            if (model is null) continue;
            raster.Draw(model, centerX + Fixed(file, at + 4), Fixed(file, at + 8), centerZ + Fixed(file, at + 12), buildingSet.Get);
        }
    }

    private static float Fixed(byte[] file, int at) => BitConverter.ToInt32(file, at) / 4096f;

    private NsbmdModel? BuildingModel(Narc narc, int id)
    {
        if (_buildingModels.TryGetValue((narc, id), out var known)) return known;
        return _buildingModels[(narc, id)] = id >= 0 && id < narc.Files.Count ? NsbmdModel.TryRead(narc.Files[id]) : null;
    }
}
