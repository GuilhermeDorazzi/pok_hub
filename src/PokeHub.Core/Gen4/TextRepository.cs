using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>Um banco de texto do msg.narc, decodificado sob demanda.</summary>
public sealed class TextBank
{
    public required int Id { get; init; }
    public string? Label { get; init; }
    public Gen4TextArchive Archive { get; init; } = null!;
    public List<string> Texts { get; init; } = [];
    public bool Dirty { get; set; }
    public int Count => Texts.Count;
    public string Title => Label is null ? $"{Id:D4}" : $"{Id:D4}  {Label}";
}

public sealed record TextSearchHit(int Bank, int Index, string Text);

/// <summary>Carrega, busca e grava os bancos de texto de um projeto Gen 4 (msg.narc).</summary>
public sealed class TextRepository
{
    private readonly Narc _narc;
    private readonly TextBank?[] _banks;

    public Gen4Game Game { get; }
    public int BankCount => _banks.Length;

    private TextRepository(Gen4Game game, Narc narc)
    {
        Game = game;
        _narc = narc;
        _banks = new TextBank?[narc.Files.Count];
    }

    public static TextRepository Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (!File.Exists(game.TextArchivePath)) throw new FileNotFoundException(Loc.T("core.txt.narcMissing"), game.TextArchivePath);
        return new TextRepository(game, Narc.Read(File.ReadAllBytes(game.TextArchivePath)));
    }

    /// <summary>Rótulos dos bancos conhecidos pelo hub (os mesmos índices usados pelos outros editores).</summary>
    public string? LabelOf(int bank)
    {
        if (bank == Game.SpeciesBank) return Loc.T("txt.bank.species");
        if (bank == Game.ItemsBank) return Loc.T("txt.bank.items");
        if (bank == Game.MovesBank) return Loc.T("txt.bank.moves");
        if (bank == Game.AbilitiesBank) return Loc.T("txt.bank.abilities");
        if (bank == Game.TrainerNamesBank) return Loc.T("txt.bank.trainerNames");
        if (bank == Game.TrainerClassesBank) return Loc.T("txt.bank.trainerClasses");
        if (bank == Game.LocationNamesBank) return Loc.T("txt.bank.locations");
        return null;
    }

    public TextBank Bank(int id)
    {
        if (id < 0 || id >= _banks.Length) throw new ArgumentOutOfRangeException(nameof(id));
        if (_banks[id] is { } b) return b;
        var archive = Gen4TextArchive.Read(_narc.Files[id]);
        var bank = new TextBank
        {
            Id = id,
            Label = LabelOf(id),
            Archive = archive,
            Texts = archive.Messages.Select(m => Gen4TextArchive.Decode(m)).ToList(),
        };
        _banks[id] = bank;
        return bank;
    }

    /// <summary>Quantidade de mensagens sem decodificar o banco inteiro.</summary>
    public int MessageCount(int id) => _narc.Files[id].Length >= 2 ? BitConverter.ToUInt16(_narc.Files[id], 0) : 0;

    public bool IsCompressed(int bank, int index) => Bank(bank).Archive.Messages[index].IsCompressed;

    /// <summary>Substitui o texto de uma mensagem (codificando na hora, para validar os caracteres). Devolve false se não mudou.</summary>
    public bool SetText(int bankId, int index, string text)
    {
        var bank = Bank(bankId);
        if (bank.Texts[index] == text) return false;
        var codes = Gen4TextArchive.Encode(text);
        var message = bank.Archive.Messages[index];
        // nomes de treinador são comprimidos em 9 bits: só caracteres com código até 0x1FF, sem comandos
        if (message.IsCompressed && codes.Any(c => c > 0x1FF))
            throw new InvalidDataException(Loc.T("core.txt.compressedOnlySimple"));
        message.SetCodes(codes);
        bank.Texts[index] = text;
        bank.Dirty = true;
        return true;
    }

    /// <summary>Acrescenta uma mensagem no fim do banco e devolve o número dela. As existentes não mudam de número.</summary>
    public int AddText(int bankId, string text)
    {
        var bank = Bank(bankId);
        var message = new Gen4Message();
        message.SetCodes(Gen4TextArchive.Encode(text));
        bank.Archive.Messages.Add(message);
        bank.Texts.Add(text);
        bank.Dirty = true;
        return bank.Texts.Count - 1;
    }

    /// <summary>Nome da espécie no banco do jogo.</summary>
    public string Species(int species) => Bank(Game.SpeciesBank).Texts[species];

    public bool HasChanges => _banks.Any(b => b is { Dirty: true });

    public IEnumerable<TextSearchHit> Search(string query, int limit = 500)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        int found = 0;
        for (int b = 0; b < _banks.Length && found < limit; b++)
        {
            var bank = Bank(b);
            for (int i = 0; i < bank.Texts.Count && found < limit; i++)
                if (bank.Texts[i].Contains(query, StringComparison.OrdinalIgnoreCase)) { found++; yield return new TextSearchHit(b, i, bank.Texts[i]); }
        }
    }

    public void Save()
    {
        for (int i = 0; i < _banks.Length; i++)
        {
            if (_banks[i] is not { Dirty: true } bank) continue;
            _narc.Files[i] = bank.Archive.Write();
            bank.Dirty = false;
        }
        File.WriteAllBytes(Game.TextArchivePath, _narc.Write());
    }
}
