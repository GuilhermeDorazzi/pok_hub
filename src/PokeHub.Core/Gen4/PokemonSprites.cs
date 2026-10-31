using PokeHub.Core.Graphics;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Sprites de batalha de Pokémon: 6 arquivos por espécie no NARC (costas fêmea e macho, frente fêmea e macho, paleta normal e shiny).
/// Formas alternativas ficam em outro NARC e ainda não são mostradas.
/// </summary>
public sealed class PokemonSprites
{
    private const int FilesPerSpecies = 6, FrontFemale = 2, FrontMale = 3, NormalPalette = 4;
    private readonly Narc _narc;

    private PokemonSprites(Narc narc) => _narc = narc;

    /// <summary>null se o jogo não tiver o arquivo: o editor segue funcionando, só sem as imagens.</summary>
    public static PokemonSprites? Load(Gen4Game game) =>
        File.Exists(game.PokemonSpritesPath) ? new PokemonSprites(Narc.Read(File.ReadAllBytes(game.PokemonSpritesPath))) : null;

    /// <summary>Frente da espécie. Espécies só de fêmeas têm o arquivo de macho vazio, então cai no outro. A espécie 0 é o espaço vazio e não tem imagem.</summary>
    public RgbaImage? Render(int species)
    {
        int first = species * FilesPerSpecies;
        if (species <= 0 || first + FilesPerSpecies > _narc.Files.Count) return null;
        var palette = _narc.Files[first + NormalPalette];
        var male = SpriteStrip.TryDecode(_narc.Files[first + FrontMale], palette);
        return male?.TrimmedToOpaque() ?? SpriteStrip.TryDecode(_narc.Files[first + FrontFemale], palette)?.TrimmedToOpaque();
    }
}
