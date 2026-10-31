using PokeHub.Core.Graphics;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Sprites de batalha de Pokémon: 6 arquivos por espécie no NARC principal (costas fêmea e macho, frente fêmea e macho, paleta normal e shiny).
/// Espécies com formas ficam em outro NARC, com a numeração do jogo (GetMonSpriteCharAndPlttNarcIdsEx na decomp pokeheartgold,
/// BuildPokemonSpriteTemplate na pokeplatinum). DP numera esse NARC de outro jogo e sem ROM para conferir, então lá as formas caem na imagem padrão.
/// </summary>
public sealed class PokemonSprites
{
    private const int FilesPerSpecies = 6, FrontFemale = 2, FrontMale = 3, NormalPalette = 4;
    /// <summary>O Platinum não tem o Pichu de orelha espigada, então todas as paletas ficam 4 arquivos antes.</summary>
    private const int PlatinumPaletteShift = 4;

    private enum Layout
    {
        /// <summary>Frente e costas por forma; uma paleta normal e uma shiny por forma.</summary>
        Paired,
        /// <summary>Frente e costas por forma; paleta única para todas (Deoxys e Unown).</summary>
        PairedSharedPalette,
        /// <summary>Costas de todas as formas, depois as frentes; paletas normal e shiny por forma (Shellos e Gastrodon).</summary>
        Grouped,
        /// <summary>Como Grouped, com as paletas normais de todas as formas antes das shiny (Cherrim).</summary>
        GroupedPalettesTogether,
        /// <summary>Quatro formas de costas, depois quatro de frente, idem nas paletas (Castform).</summary>
        Castform,
    }

    private sealed record FormEntry(int Character, int Palette, int Forms, Layout Layout);

    private static readonly Dictionary<int, FormEntry> FormTable = new()
    {
        [172] = new(154, 252, 2, Layout.Paired),                 // Pichu, só HGSS
        [201] = new(8, 160, 28, Layout.PairedSharedPalette),     // Unown
        [351] = new(64, 162, 4, Layout.Castform),                // Castform
        [386] = new(0, 158, 4, Layout.PairedSharedPalette),      // Deoxys
        [412] = new(72, 170, 3, Layout.Paired),                  // Burmy
        [413] = new(78, 176, 3, Layout.Paired),                  // Wormadam
        [421] = new(92, 190, 2, Layout.GroupedPalettesTogether), // Cherrim
        [422] = new(84, 182, 2, Layout.Grouped),                 // Shellos
        [423] = new(88, 186, 2, Layout.Grouped),                 // Gastrodon
        [479] = new(138, 236, 6, Layout.Paired),                 // Rotom
        [487] = new(150, 248, 2, Layout.Paired),                 // Giratina
        [492] = new(134, 232, 2, Layout.Paired),                 // Shaymin
        [493] = new(96, 194, 18, Layout.Paired),                 // Arceus
    };

    private readonly Narc _narc;
    private readonly Gen4Game _game;
    private Narc? _forms;

    private PokemonSprites(Narc narc, Gen4Game game)
    {
        _narc = narc;
        _game = game;
    }

    /// <summary>null se o jogo não tiver o arquivo: o editor segue funcionando, só sem as imagens.</summary>
    public static PokemonSprites? Load(Gen4Game game) =>
        File.Exists(game.PokemonSpritesPath) ? new PokemonSprites(Narc.Read(File.ReadAllBytes(game.PokemonSpritesPath)), game) : null;

    /// <summary>Frente da espécie, recortada em volta da figura. A espécie 0 é o espaço vazio e não tem imagem.</summary>
    public RgbaImage? Render(int species, int form = 0, bool female = false)
    {
        int first = species * FilesPerSpecies;
        if (species <= 0 || first + FilesPerSpecies > _narc.Files.Count) return null;
        return RenderForm(species, form) ?? RenderDefault(first, female);
    }

    /// <summary>Espécies só de um gênero têm o arquivo do outro vazio, então cai no que existe.</summary>
    private RgbaImage? RenderDefault(int first, bool female)
    {
        var palette = _narc.Files[first + NormalPalette];
        int preferred = female ? FrontFemale : FrontMale, other = female ? FrontMale : FrontFemale;
        return SpriteStrip.TryDecode(_narc.Files[first + preferred], palette)?.TrimmedToOpaque()
            ?? SpriteStrip.TryDecode(_narc.Files[first + other], palette)?.TrimmedToOpaque();
    }

    private RgbaImage? RenderForm(int species, int form)
    {
        if (_game.IsDp || !FormTable.TryGetValue(species, out var entry)) return null;
        if (species == 172 && !_game.IsHgss) return null;
        var narc = FormsNarc();
        if (narc is null) return null;

        if (form < 0 || form >= entry.Forms) form = 0; // o jogo faz o mesmo com forma fora do limite
        var (character, palette) = FormFiles(entry, form);
        if (!_game.IsHgss) palette -= PlatinumPaletteShift;
        if (character >= narc.Files.Count || palette >= narc.Files.Count) return null;
        return SpriteStrip.TryDecode(narc.Files[character], narc.Files[palette])?.TrimmedToOpaque();
    }

    private static (int Character, int Palette) FormFiles(FormEntry e, int form) => e.Layout switch
    {
        Layout.Paired => (e.Character + 1 + form * 2, e.Palette + form * 2),
        Layout.PairedSharedPalette => (e.Character + 1 + form * 2, e.Palette),
        Layout.Grouped => (e.Character + 2 + form, e.Palette + form * 2),
        Layout.GroupedPalettesTogether => (e.Character + 2 + form, e.Palette + form),
        _ => (e.Character + 4 + form, e.Palette + form),
    };

    private Narc? FormsNarc()
    {
        if (_forms is null && File.Exists(_game.PokemonFormSpritesPath)) _forms = Narc.Read(File.ReadAllBytes(_game.PokemonFormSpritesPath));
        return _forms;
    }
}
