using PokeHub.Core.Graphics;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Ícones de item: 32×32, 4 bits por pixel, em tiles. O arquivo de cada item vem de uma tabela no arm9 (sItemIndexMappings na decomp),
/// com 4 números de 16 bits por item: dados, ícone, paleta e id no GBA.
/// </summary>
public sealed class ItemIcons
{
    private const int EntrySize = 8, IconTiles = 16, TilesPerRow = 4;

    /// <summary>
    /// Master, Ultra e Great Ball, os itens 1 a 3. O endereço da tabela muda por jogo e idioma, e este começo é o mesmo em todos,
    /// então procurar evita manter um endereço por versão.
    /// </summary>
    private static readonly ushort[] TableSignature = [1, 2, 3, 1, 2, 4, 5, 2, 3, 6, 7, 3];

    private readonly Narc _narc;
    private readonly byte[] _arm9;
    private readonly int _table;

    private ItemIcons(Narc narc, byte[] arm9, int table)
    {
        _narc = narc;
        _arm9 = arm9;
        _table = table;
    }

    /// <summary>null se faltar o arquivo ou a tabela não aparecer no arm9: o editor segue funcionando, só sem os ícones.</summary>
    public static ItemIcons? Load(Gen4Game game)
    {
        if (!File.Exists(game.ItemIconsPath) || !File.Exists(game.Arm9Path)) return null;
        var arm9 = Blz.DecompressArm9(File.ReadAllBytes(game.Arm9Path));
        int table = FindTable(arm9);
        return table < 0 ? null : new ItemIcons(Narc.Read(File.ReadAllBytes(game.ItemIconsPath)), arm9, table);
    }

    /// <summary>Posição do item 0 no arm9, ou -1.</summary>
    public static int FindTable(byte[] arm9)
    {
        var signature = new byte[TableSignature.Length * 2];
        Buffer.BlockCopy(TableSignature, 0, signature, 0, signature.Length);
        int at = arm9.AsSpan().IndexOf(signature);
        return at < EntrySize ? -1 : at - EntrySize;
    }

    /// <summary>O item 0 é "nenhum" e não tem ícone.</summary>
    public RgbaImage? Render(int item)
    {
        int entry = _table + item * EntrySize;
        if (item <= 0 || entry + EntrySize > _arm9.Length) return null;
        int icon = BitConverter.ToUInt16(_arm9, entry + 2), palette = BitConverter.ToUInt16(_arm9, entry + 4);
        if (icon >= _narc.Files.Count || palette >= _narc.Files.Count) return null;
        try { return Draw(NitroTiles.Read(_narc.Files[icon]), NitroPalette.Read(_narc.Files[palette])); }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    private static RgbaImage? Draw(NitroTiles tiles, NitroPalette palette)
    {
        if (tiles.Is8Bit || tiles.Tiles.Length < IconTiles) return null;
        var image = new RgbaImage(TilesPerRow * 8, TilesPerRow * 8);
        for (int t = 0; t < IconTiles; t++)
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int c = tiles.Tiles[t][y * 8 + x];
                    int px = t % TilesPerRow * 8 + x, py = t / TilesPerRow * 8 + y;
                    if (c == 0) { image.Set(px, py, 0, 0, 0, 0); continue; }
                    var (r, g, b) = c < palette.Colors.Length ? palette.Colors[c] : ((byte)255, (byte)0, (byte)255);
                    image.Set(px, py, r, g, b);
                }
        return image;
    }
}
