using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Falas de treinador do HG/SS: cada entrada de trtbl tem treinador e tipo de fala, e o texto é a mensagem de mesmo número no banco 728.
/// trtblofs diz onde começam as entradas de cada treinador. O jogo procura a partir desse ponto, então entradas novas entram no fim,
/// e nenhum número antigo muda. Formato conforme pokeheartgold (trainer_data.c).
/// </summary>
public sealed class TrainerMessages
{
    public const int Intro = 0, Lose = 1, After = 2;
    private const int EntrySize = 4, MessagesBank = 728;

    private readonly Gen4Game _game;
    private readonly Narc _table, _offsets;
    private readonly string _tablePath, _offsetsPath;

    private TrainerMessages(Gen4Game game, string tablePath, string offsetsPath)
    {
        _game = game;
        _tablePath = tablePath;
        _offsetsPath = offsetsPath;
        _table = Narc.Read(File.ReadAllBytes(tablePath));
        _offsets = Narc.Read(File.ReadAllBytes(offsetsPath));
    }

    public static TrainerMessages Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (!game.IsHgss || game.Japanese) throw new NotSupportedException(Loc.T("core.newev.trainerTextsGame"));
        return new TrainerMessages(game, Path.Combine(game.FilesRoot, "a", "0", "5", "7"), Path.Combine(game.FilesRoot, "a", "1", "3", "1"));
    }

    private int OffsetOf(int trainer) => trainer * 2 + 2 <= _offsets.Files[0].Length ? BitConverter.ToUInt16(_offsets.Files[0], trainer * 2) : -1;

    /// <summary>Entradas do treinador: as que vêm em sequência, a partir de onde a lista aponta.</summary>
    private List<(int At, int Kind)> EntriesOf(int trainer)
    {
        var table = _table.Files[0];
        var found = new List<(int, int)>();
        int at = OffsetOf(trainer);
        for (; at >= 0 && at + EntrySize <= table.Length && BitConverter.ToUInt16(table, at) == trainer; at += EntrySize)
            found.Add((at, BitConverter.ToUInt16(table, at + 2)));
        return found;
    }

    public bool Has(int trainer, int kind) => EntriesOf(trainer).Any(e => e.Kind == kind);

    public string? Text(TextRepository texts, int trainer, int kind)
    {
        foreach (var (at, entry) in EntriesOf(trainer))
            if (entry == kind) return texts.Bank(MessagesBank).Texts[at / EntrySize];
        return null;
    }

    /// <summary>
    /// Grava as falas. A que já existe é trocada no lugar. Se falta alguma, as entradas do treinador vão para o fim da tabela, porque precisam ficar em sequência.
    /// Os textos só vão para o disco com o Save do repositório de textos, e a tabela com o Save daqui.
    /// </summary>
    public void Set(TextRepository texts, int trainer, IReadOnlyDictionary<int, string> lines)
    {
        var current = EntriesOf(trainer);
        var missing = lines.Keys.Where(k => current.All(e => e.Kind != k)).ToList();
        foreach (var (at, kind) in current)
            if (lines.TryGetValue(kind, out var text)) texts.SetText(MessagesBank, at / EntrySize, text);
        if (missing.Count == 0) return;

        var table = _table.Files[0];
        int start = table.Length;
        var bank = texts.Bank(MessagesBank);
        if (bank.Texts.Count != table.Length / EntrySize) throw new InvalidDataException(Loc.T("core.newev.trainerTextsMismatch"));

        var kinds = current.Select(e => e.Kind).Concat(missing).ToList();
        var grown = new byte[start + kinds.Count * EntrySize];
        table.CopyTo(grown, 0);
        for (int i = 0; i < kinds.Count; i++)
        {
            BitConverter.TryWriteBytes(grown.AsSpan(start + i * EntrySize), (ushort)trainer);
            BitConverter.TryWriteBytes(grown.AsSpan(start + i * EntrySize + 2), (ushort)kinds[i]);
            string line = lines.TryGetValue(kinds[i], out var chosen) ? chosen : bank.Texts[current[i].At / EntrySize];
            texts.AddText(MessagesBank, line);
        }
        if (grown.Length > ushort.MaxValue) throw new InvalidDataException(Loc.T("core.newev.trainerTextsFull"));
        _table.Files[0] = grown;

        var offsets = _offsets.Files[0];
        if (offsets.Length < (trainer + 1) * 2)
        {
            // treinadores sem lista ainda: apontam para o começo, onde a busca não acha nada deles
            var longer = new byte[(trainer + 1) * 2];
            offsets.CopyTo(longer, 0);
            offsets = longer;
        }
        BitConverter.TryWriteBytes(offsets.AsSpan(trainer * 2), (ushort)start);
        _offsets.Files[0] = offsets;
    }

    public void Save()
    {
        File.WriteAllBytes(_tablePath, _table.Write());
        File.WriteAllBytes(_offsetsPath, _offsets.Write());
    }
}
