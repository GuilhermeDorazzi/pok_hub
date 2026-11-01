using PokeHub.Core.Graphics;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Ícones de 32x32 das mochilas e do menu de Pokémon, os mesmos que o jogo mostra. Os de item vêm de <see cref="ItemIcons"/>;
/// os de Pokémon usam uma tabela do arm9 com uma das 3 paletas de ícone por espécie, achada pelo conteúdo porque a posição muda em cada jogo.
/// </summary>
public sealed class GameIcons
{
    private const int FirstSpeciesMember = 7, ColorsPerPalette = 16, IconTiles = 4, MaxSpecies = 493;
    /// <summary>Paletas de ícone da espécie 0 ao Beedrill, iguais nos jogos de Gen 4.</summary>
    private static readonly byte[] SpeciesPaletteStart = [0, 1, 1, 1, 0, 0, 0, 0, 2, 2, 1, 1, 0, 1, 2, 2];
    private readonly ItemIcons? _items;
    private readonly Narc _species;
    private readonly byte[] _arm9;
    private readonly int _paletteTable;
    private readonly NitroPalette _speciesPalette;

    private GameIcons(ItemIcons? items, Narc species, byte[] arm9)
    {
        _items = items; _species = species; _arm9 = arm9;
        _paletteTable = FindSpeciesPalettes(arm9);
        _speciesPalette = NitroPalette.Read(species.Files[0]);
    }

    /// <summary>null se os arquivos faltarem ou estiverem em outro formato: as listas seguem só com o nome.</summary>
    public static GameIcons? TryLoad(Gen4Game game)
    {
        string species = Path.Combine(game.FilesRoot, game.IsHgss ? @"a\0\2\0" : @"poketool\icongra\pl_poke_icon.narc");
        if (!File.Exists(species) || !File.Exists(game.Arm9Path)) return null;
        // no workspace do ndstool o arm9 fica comprimido em disco
        try { return new GameIcons(ItemIcons.Load(game), Narc.Read(File.ReadAllBytes(species)), Blz.DecompressArm9(File.ReadAllBytes(game.Arm9Path))); }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    private static int FindSpeciesPalettes(byte[] arm9)
    {
        int from = 0;
        while (true)
        {
            int at = arm9.AsSpan(from).IndexOf(SpeciesPaletteStart);
            if (at < 0) return -1;
            at += from;
            if (at + MaxSpecies < arm9.Length && arm9.AsSpan(at, MaxSpecies + 1).IndexOfAnyExceptInRange((byte)0, (byte)2) < 0) return at;
            from = at + 1;
        }
    }

    public bool UsesSpeciesPalettes => _paletteTable >= 0;

    public RgbaImage? Item(int item) => _items?.Render(item);

    public RgbaImage? Species(int species)
    {
        if (species < 0 || species > MaxSpecies || species + FirstSpeciesMember >= _species.Files.Count) return null;
        int bank = _paletteTable < 0 ? 0 : _arm9[_paletteTable + species];
        try { return Draw(_species.Files[species + FirstSpeciesMember], _speciesPalette, bank); }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    /// <summary>O ícone é de 4x4 tiles, em sequência; o do Pokémon tem um segundo quadro depois, para a animação, que fica de fora.</summary>
    private static RgbaImage? Draw(byte[] ncgr, NitroPalette palette, int bank)
    {
        var tiles = NitroTiles.Read(ncgr).Tiles;
        if (tiles.Length < IconTiles * IconTiles) return null;
        var image = new RgbaImage(IconTiles * 8, IconTiles * 8);
        for (int t = 0; t < IconTiles * IconTiles; t++)
            for (int i = 0; i < 64; i++)
            {
                int color = tiles[t][i];
                if (color == 0) continue; // a cor 0 é transparente
                int index = bank * ColorsPerPalette + color;
                if (index >= palette.Colors.Length) continue;
                var (r, g, b) = palette.Colors[index];
                image.Set(t % IconTiles * 8 + i % 8, t / IconTiles * 8 + i / 8, r, g, b);
            }
        return image;
    }
}
