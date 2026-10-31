using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Dados de uma espécie (BaseStats nas decomps): 44 bytes no NARC de dados pessoais. As propriedades leem e gravam direto nos bytes,
/// então o que o hub não edita (compatibilidade de TMs, por exemplo) continua igual.
/// </summary>
public sealed class PersonalData(byte[] raw)
{
    public const int Size = 44;
    public byte[] Raw { get; } = raw;

    public byte Hp { get => Raw[0]; set => Raw[0] = value; }
    public byte Attack { get => Raw[1]; set => Raw[1] = value; }
    public byte Defense { get => Raw[2]; set => Raw[2] = value; }
    public byte Speed { get => Raw[3]; set => Raw[3] = value; }
    public byte SpAttack { get => Raw[4]; set => Raw[4] = value; }
    public byte SpDefense { get => Raw[5]; set => Raw[5] = value; }
    public byte Type1 { get => Raw[6]; set => Raw[6] = value; }
    public byte Type2 { get => Raw[7]; set => Raw[7] = value; }
    public byte CatchRate { get => Raw[8]; set => Raw[8] = value; }
    public byte ExpYield { get => Raw[9]; set => Raw[9] = value; }
    public ushort Item1 { get => BitConverter.ToUInt16(Raw, 12); set => BitConverter.TryWriteBytes(Raw.AsSpan(12), value); }
    public ushort Item2 { get => BitConverter.ToUInt16(Raw, 14); set => BitConverter.TryWriteBytes(Raw.AsSpan(14), value); }
    public byte GenderRatio { get => Raw[16]; set => Raw[16] = value; }
    public byte EggCycles { get => Raw[17]; set => Raw[17] = value; }
    public byte Friendship { get => Raw[18]; set => Raw[18] = value; }
    public byte GrowthRate { get => Raw[19]; set => Raw[19] = value; }
    public byte EggGroup1 { get => Raw[20]; set => Raw[20] = value; }
    public byte EggGroup2 { get => Raw[21]; set => Raw[21] = value; }
    public byte Ability1 { get => Raw[22]; set => Raw[22] = value; }
    public byte Ability2 { get => Raw[23]; set => Raw[23] = value; }

    public int Total => Hp + Attack + Defense + Speed + SpAttack + SpDefense;

    /// <summary>Pontos de esforço, 2 bits cada, na ordem HP, Ataque, Defesa, Velocidade, Ataque Especial, Defesa Especial.</summary>
    public int EvYield(int stat) => (BitConverter.ToUInt16(Raw, 10) >> (stat * 2)) & 3;

    public void SetEvYield(int stat, int value)
    {
        int word = BitConverter.ToUInt16(Raw, 10);
        word = (word & ~(3 << (stat * 2))) | ((Math.Clamp(value, 0, 3)) << (stat * 2));
        BitConverter.TryWriteBytes(Raw.AsSpan(10), (ushort)word);
    }
}

/// <summary>Carrega e grava os dados pessoais, com os nomes vindos do próprio jogo.</summary>
public sealed class PersonalRepository
{
    /// <summary>Ordem dos tipos no jogo; o 9 é o "???" das decomps.</summary>
    public static readonly IReadOnlyList<string> TypeNames = ["Normal", "Fighting", "Flying", "Poison", "Ground", "Rock", "Bug", "Ghost", "Steel", "???", "Fire", "Water", "Grass", "Electric", "Psychic", "Ice", "Dragon", "Dark"];

    private readonly Narc _narc;
    private readonly string _path;
    private readonly byte[][] _original;

    public Gen4Game Game { get; }
    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyList<string> Abilities { get; }
    public int Count => _narc.Files.Count;

    private PersonalRepository(Gen4Game game, string path, Narc narc, IReadOnlyList<string> species, IReadOnlyList<string> items, IReadOnlyList<string> abilities)
    {
        Game = game; _path = path; _narc = narc; Species = species; Items = items; Abilities = abilities;
        _original = narc.Files.Select(f => f.ToArray()).ToArray();
    }

    public static PersonalRepository Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (!File.Exists(game.PersonalPath)) throw new FileNotFoundException(Loc.T("core.personal.missing"), game.PersonalPath);
        var text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
        List<string> Bank(int b) => b < text.Files.Count ? Gen4TextArchive.Read(text.Files[b]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList() : [];
        return new PersonalRepository(game, game.PersonalPath, Narc.Read(File.ReadAllBytes(game.PersonalPath)), Bank(game.SpeciesBank), Bank(game.ItemsBank), Bank(game.AbilitiesBank));
    }

    public string NameOf(int species) => species >= 0 && species < Species.Count ? Species[species] : $"#{species}";

    /// <summary>null para arquivo curto demais: entradas extras do NARC, como as de formas, podem ser diferentes em versões modificadas.</summary>
    public PersonalData? Get(int species) =>
        species >= 0 && species < _narc.Files.Count && _narc.Files[species].Length >= PersonalData.Size ? new PersonalData(_narc.Files[species]) : null;

    public bool HasChanges => _narc.Files.Where((f, i) => !f.AsSpan().SequenceEqual(_original[i])).Any();

    public void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, _narc.Write());
        File.Move(temp, _path, overwrite: true);
        for (int i = 0; i < _narc.Files.Count; i++) _original[i] = _narc.Files[i].ToArray();
    }
}
