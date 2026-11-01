using PokeHub.Core.Graphics;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>Algo com posição no mapa, em tiles a partir do canto da área desenhada. Warp leva ao cabeçalho de destino.</summary>
public sealed record LayoutMarker(EventKind Kind, int Index, int X, int Y, int Width = 1, int Height = 1, int? SpriteId = null, int? TargetMap = null);

/// <summary>
/// Planta de um mapa para mostrar os eventos no lugar: colisões e tipos de tile das células da matriz que pertencem ao mapa.
/// A matriz lista, por célula, o cabeçalho e o arquivo de terreno (land data), com 32×32 tiles de 2 bytes: tipo e colisão.
/// As posições dos eventos contam a partir do canto da matriz inteira. Só a planta, sem o modelo 3D nem as sprites dos NPCs.
/// </summary>
public sealed class MapLayout
{
    private const int CellTiles = 32, PermissionBytes = CellTiles * CellTiles * 2;
    private const ushort NoCell = 0xFFFF, SoundSignature = 0x1234;
    private const byte Blocked = 0x80;
    /// <summary>Warp dinâmico: o destino é o lugar de onde o jogador veio, guardado no save. Por isso TargetMap fica null.</summary>
    private const int DynamicWarpHeader = 0xFFF;

    public int Width { get; }
    public int Height { get; }
    /// <summary>Tile da matriz que corresponde ao canto da planta: somado a uma posição da planta, dá a posição do arquivo de eventos.</summary>
    public int OriginX { get; private set; }
    public int OriginY { get; private set; }
    public List<LayoutMarker> Markers { get; } = [];
    /// <summary>Células desenhadas: arquivo de terreno e canto, em tiles a partir do canto da planta.</summary>
    public List<(int Land, int Left, int Top)> Cells { get; } = [];
    private readonly byte[] _types, _collisions;
    private readonly bool[] _hasTile;

    private MapLayout(int width, int height)
    {
        Width = width;
        Height = height;
        _types = new byte[width * height];
        _collisions = new byte[width * height];
        _hasTile = new bool[width * height];
    }

    public bool IsBlocked(int x, int y) => (_collisions[y * Width + x] & Blocked) != 0;

    /// <param name="landFile">Arquivo de terreno pelo número, ou null se não existir.</param>
    public static MapLayout? Build(byte[] matrix, Func<int, byte[]?> landFile, int mapId, EventFile events, bool hasSoundSection)
    {
        if (matrix.Length < 5) return null;
        int width = matrix[0], height = matrix[1], count = width * height;
        bool hasHeaders = matrix[2] != 0, hasAltitudes = matrix[3] != 0;
        int p = 5 + matrix[4];
        var headers = new int[count];
        if (hasHeaders)
        {
            if (p + count * 2 > matrix.Length) return null;
            for (int i = 0; i < count; i++, p += 2) headers[i] = BitConverter.ToUInt16(matrix, p);
        }
        if (hasAltitudes) p += count;
        if (count == 0 || p + count * 2 > matrix.Length) return null;
        var land = new int[count];
        for (int i = 0; i < count; i++, p += 2) land[i] = BitConverter.ToUInt16(matrix, p);

        // matriz sem cabeçalhos (interiores) é toda deste mapa; na grande do mundo, só as células dele
        var cells = Enumerable.Range(0, count).Where(i => land[i] != NoCell && (!hasHeaders || headers[i] == mapId)).ToHashSet();
        if (cells.Count == 0) cells = Enumerable.Range(0, count).Where(i => land[i] != NoCell).ToHashSet();
        if (cells.Count == 0) return null;
        // um evento pode ficar numa célula vizinha, de outro cabeçalho: entra na área para não sumir
        void Include(int x, int y)
        {
            int cx = x / CellTiles, cy = y / CellTiles;
            if (x >= 0 && y >= 0 && cx < width && cy < height && land[cy * width + cx] != NoCell) cells.Add(cy * width + cx);
        }
        foreach (var o in events.Objects) Include(o.X, o.Z);
        foreach (var b in events.Bg) Include(b.X, b.Z);
        foreach (var w in events.Warps) Include(w.X, w.Z);
        foreach (var c in events.Coords) Include(c.X, c.Z);

        int minX = cells.Min(c => c % width), maxX = cells.Max(c => c % width);
        int minY = cells.Min(c => c / width), maxY = cells.Max(c => c / width);
        var layout = new MapLayout((maxX - minX + 1) * CellTiles, (maxY - minY + 1) * CellTiles);
        foreach (var cell in cells)
        {
            layout.Cells.Add((land[cell], (cell % width - minX) * CellTiles, (cell / width - minY) * CellTiles));
            if (landFile(land[cell]) is { } file)
                layout.ReadCell(file, (cell % width - minX) * CellTiles, (cell / width - minY) * CellTiles, hasSoundSection);
        }

        int ox = minX * CellTiles, oy = minY * CellTiles;
        layout.OriginX = ox;
        layout.OriginY = oy;
        foreach (var o in events.Objects) layout.Markers.Add(new(EventKind.Npc, o.Index, o.X - ox, o.Z - oy, SpriteId: o.SpriteId));
        foreach (var b in events.Bg) layout.Markers.Add(new(EventKind.Sign, b.Index, b.X - ox, b.Z - oy));
        foreach (var c in events.Coords) layout.Markers.Add(new(EventKind.Trigger, c.Index, c.X - ox, c.Z - oy, Math.Max(1, c.W), Math.Max(1, c.H)));
        foreach (var w in events.Warps) layout.Markers.Add(new(EventKind.Warp, w.Index, w.X - ox, w.Z - oy, TargetMap: w.TargetHeader == DynamicWarpHeader ? null : w.TargetHeader));
        return layout;
    }

    private void ReadCell(byte[] file, int left, int top, bool hasSoundSection)
    {
        if (file.Length < 16) return;
        int p = 16;
        // HGSS: antes das colisões vem a seção de sons de fundo, com assinatura e tamanho
        if (hasSoundSection)
        {
            if (file.Length < p + 4 || BitConverter.ToUInt16(file, p) != SoundSignature) return;
            p += 4 + BitConverter.ToUInt16(file, p + 2);
        }
        if (p + PermissionBytes > file.Length) return;
        for (int y = 0; y < CellTiles; y++)
            for (int x = 0; x < CellTiles; x++, p += 2)
            {
                int i = (top + y) * Width + left + x;
                _types[i] = file[p];
                _collisions[i] = file[p + 1];
                _hasTile[i] = true;
            }
    }

    /// <summary>Um pixel por tile; quem mostra amplia. Água e grama vêm antes da colisão: água bloqueada a pé ainda é água.</summary>
    public RgbaImage Render()
    {
        var image = new RgbaImage(Width, Height);
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                var (r, g, b) = !_hasTile[i] ? ((byte)40, (byte)44, (byte)48) : _types[i] switch
                {
                    0x02 or 0x03 => ((byte)122, (byte)190, (byte)100),                 // grama
                    0x10 or 0x13 or 0x15 or 0x17 => ((byte)104, (byte)165, (byte)225), // água
                    _ => IsBlocked(x, y) ? ((byte)96, (byte)104, (byte)110) : ((byte)232, (byte)236, (byte)224),
                };
                image.Set(x, y, r, g, b);
            }
        return image;
    }
}
