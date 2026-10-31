using PokeHub.Core.Projects;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Gen4;

/// <summary>Os oito blocos de áreas da Pokédex: áreas normais (rotas e cidades) e especiais (cavernas e prédios), por período.</summary>
public enum PokedexAreaBlock
{
    NormalMorning, NormalDay, NormalNight, NormalAll,
    SpecialMorning, SpecialDay, SpecialNight, SpecialAll,
}

public sealed record PokedexLocation(uint Id, string Name);

/// <summary>
/// Áreas da Pokédex de HGSS: NARC a/1/3/3. Para cada espécie há um arquivo por bloco, com a lista de ids de área
/// em u32 little-endian terminada por 0. Offsets e nomes das áreas conforme o pk_dex_editor (whackahack, thread 49867).
/// GEN5: BW/B2W2 guardam isso de outro jeito; DP/Pt usam zukan_enc_*.narc com outro formato.
/// </summary>
public sealed class PokedexAreaRepository
{
    public const string HgssNarcRelativePath = @"a\1\3\3";
    public const int SpeciesCount = 493;

    private readonly Narc _narc;
    private readonly string _narcPath;
    private readonly byte[][] _original;

    public Gen4Game Game { get; }
    public IReadOnlyList<string> SpeciesNames { get; }

    private PokedexAreaRepository(Gen4Game game, string narcPath, Narc narc, IReadOnlyList<string> speciesNames)
    {
        Game = game;
        _narcPath = narcPath;
        _narc = narc;
        _original = narc.Files.Select(f => f.ToArray()).ToArray();
        SpeciesNames = speciesNames;
    }

    public static bool Supports(Gen4Game game) => game.IsHgss;

    public static PokedexAreaRepository Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (!Supports(game)) throw new NotSupportedException("O editor de áreas da Pokédex só cobre HeartGold/SoulSilver por enquanto.");
        var narcPath = Path.Combine(game.FilesRoot, HgssNarcRelativePath);
        if (!File.Exists(narcPath)) throw new FileNotFoundException("NARC das áreas da Pokédex não encontrado.", narcPath);
        var narc = Narc.Read(File.ReadAllBytes(narcPath));
        if (narc.Files.Count < 3467 + SpeciesCount) throw new InvalidDataException($"a/1/3/3 tem {narc.Files.Count} arquivos; esperado ao menos {3467 + SpeciesCount}.");

        var text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
        var species = Gen4TextArchive.Read(text.Files[game.SpeciesBank]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList();
        return new PokedexAreaRepository(game, narcPath, narc, species);
    }

    /// <summary>Índice do arquivo no NARC para uma espécie (1..493) e um bloco.</summary>
    public static int FileIndex(int species, PokedexAreaBlock block) => species + block switch
    {
        PokedexAreaBlock.SpecialMorning => 2,
        PokedexAreaBlock.SpecialDay => 497,
        PokedexAreaBlock.SpecialNight => 992,
        PokedexAreaBlock.NormalMorning => 1487,
        PokedexAreaBlock.NormalDay => 1982,
        PokedexAreaBlock.NormalNight => 2477,
        PokedexAreaBlock.SpecialAll => 2972,
        PokedexAreaBlock.NormalAll => 3467,
        _ => throw new ArgumentOutOfRangeException(nameof(block)),
    };

    public static bool IsSpecial(PokedexAreaBlock block) => block is PokedexAreaBlock.SpecialMorning or PokedexAreaBlock.SpecialDay or PokedexAreaBlock.SpecialNight or PokedexAreaBlock.SpecialAll;

    public IReadOnlyList<uint> GetAreas(int species, PokedexAreaBlock block)
    {
        var data = _narc.Files[FileIndex(species, block)];
        var ids = new List<uint>();
        for (int i = 0; i + 4 <= data.Length; i += 4)
        {
            uint id = BitConverter.ToUInt32(data, i);
            if (id == 0) break;
            ids.Add(id);
        }
        return ids;
    }

    public void SetAreas(int species, PokedexAreaBlock block, IEnumerable<uint> ids)
    {
        using var ms = new MemoryStream();
        foreach (var id in ids.Distinct().OrderBy(i => i)) ms.Write(BitConverter.GetBytes(id));
        ms.Write(BitConverter.GetBytes(0u));
        var bytes = ms.ToArray();
        int index = FileIndex(species, block);
        // mantém os bytes originais quando a lista não mudou (preserva preenchimento e ordem do jogo)
        if (GetAreas(species, block).ToHashSet().SetEquals(ids)) bytes = _narc.Files[index];
        _narc.Files[index] = bytes;
    }

    public bool HasChanges => _narc.Files.Where((f, i) => !f.AsSpan().SequenceEqual(_original[i])).Any();

    public void Save()
    {
        File.WriteAllBytes(_narcPath, _narc.Write());
        for (int i = 0; i < _narc.Files.Count; i++) _original[i] = _narc.Files[i].ToArray();
    }

    // ----- tabelas de áreas (HGSS) -----

    public static IReadOnlyList<PokedexLocation> NormalLocations { get; } =
    [
        new(0x01, "New Bark Town"), new(0x02, "Route 29"), new(0x03, "Cherrygrove City"), new(0x04, "Route 30"), new(0x05, "Route 31"),
        new(0x06, "Violet City"), new(0x07, "Route 32"), new(0x08, "Route 33"), new(0x09, "Route 34"), new(0x0A, "Route 35"),
        new(0x0B, "Route 36"), new(0x0C, "Route 37"), new(0x0D, "Ecruteak City"), new(0x0E, "Route 38"), new(0x0F, "Route 39"),
        new(0x10, "Olivine City"), new(0x11, "Route 40"), new(0x12, "Route 41"), new(0x13, "Cianwood City"), new(0x14, "Route 42"),
        new(0x15, "Route 43"), new(0x16, "Lake of Rage"), new(0x17, "Route 44"), new(0x18, "Blackthorn City"), new(0x19, "Route 45"),
        new(0x1A, "Route 46"), new(0x1B, "Route 47"), new(0x1C, "Mt. Silver"), new(0x1D, "Route 12"), new(0x1E, "Route 19"),
        new(0x1F, "Route 20"), new(0x20, "Pallet Town"), new(0x21, "Viridian City"), new(0x22, "Cerulean City"), new(0x23, "Vermilion City"),
        new(0x24, "Celadon City"), new(0x25, "Fuchsia City"), new(0x26, "Cinnabar Island"), new(0x27, "Route 48"), new(0x28, "Route 26"),
        new(0x29, "Route 27"), new(0x2A, "Route 28"), new(0x2B, "Route 1"), new(0x2C, "Route 2"), new(0x2D, "Route 3"),
        new(0x2E, "Route 4"), new(0x2F, "Route 5"), new(0x30, "Route 6"), new(0x31, "Route 7"), new(0x32, "Route 8"),
        new(0x33, "Route 9"), new(0x34, "Route 10"), new(0x35, "Route 11"), new(0x36, "Route 13"), new(0x37, "Route 14"),
        new(0x38, "Route 15"), new(0x39, "Route 16"), new(0x3A, "Route 17"), new(0x3B, "Route 18"), new(0x3C, "Route 21"),
        new(0x3D, "Route 22"), new(0x3E, "Route 24"), new(0x3F, "Route 25"),
    ];

    public static IReadOnlyList<PokedexLocation> SpecialLocations { get; } =
    [
        new(0x01, "Sprout Tower"), new(0x02, "Ruins of Alph"), new(0x03, "Union Cave"), new(0x04, "Slowpoke Well"), new(0x05, "Ilex Forest"),
        new(0x06, "National Park"), new(0x07, "Burned Tower"), new(0x08, "Bell Tower"), new(0x09, "Whirl Islands"), new(0x0A, "Mt. Mortar"),
        new(0x0B, "Ice Path"), new(0x0C, "Dragon's Den"), new(0x0D, "Dark Cave"), new(0x0E, "Mt. Moon"), new(0x0F, "Seafoam Islands"),
        new(0x10, "Mt. Silver"), new(0x11, "Cliff Edge Gate"), new(0x12, "Cliff Cave"), new(0x14, "Rock Tunnel"), new(0x15, "Victory Road"),
        new(0x16, "Tohjo Falls"), new(0x17, "Diglett's Cave"), new(0x18, "Viridian Forest"), new(0x19, "Cerulean Cave"),
    ];
}
