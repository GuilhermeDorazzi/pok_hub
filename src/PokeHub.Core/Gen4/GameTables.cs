using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Gen4;

public enum TableCellKind { Number, Species, Move, Item, Ability, Type, Choice, Flag }

/// <summary>Uma coluna: onde o valor fica no registro. Bits &gt; 0 lê só esse trecho do campo, a partir de Bit.</summary>
public sealed record TableColumn(string Key, int Offset, int Bytes, TableCellKind Kind = TableCellKind.Number, int Bit = 0, int Bits = 0, bool Signed = false, string? ChoicePrefix = null, int ChoiceCount = 0, int Buffer = 0)
{
    public int Min => Signed ? -(1 << (Bytes * 8 - 1)) : 0;
    public long Max => Bits > 0 ? (1L << Bits) - 1 : Signed ? (1L << (Bytes * 8 - 1)) - 1 : (1L << (Bytes * 8)) - 1;
}

/// <summary>Uma linha da tabela. Lê e grava direto nos bytes, então o que não tem coluna continua igual.</summary>
public class TableRow(string name, byte[][] buffers, int[] bases)
{
    public string Name { get; set; } = name;

    public virtual int Get(TableColumn c)
    {
        int at = bases[c.Buffer] + c.Offset;
        var b = buffers[c.Buffer];
        long raw = 0;
        for (int i = 0; i < c.Bytes; i++) raw |= (long)b[at + i] << (8 * i);
        if (c.Bits > 0) return (int)((raw >> c.Bit) & ((1L << c.Bits) - 1));
        if (c.Signed && raw >= 1L << (c.Bytes * 8 - 1)) raw -= 1L << (c.Bytes * 8);
        return (int)raw;
    }

    public virtual void Set(TableColumn c, int value)
    {
        long v = Math.Clamp(value, c.Min, c.Max);
        int at = bases[c.Buffer] + c.Offset;
        var b = buffers[c.Buffer];
        long raw = 0;
        for (int i = 0; i < c.Bytes; i++) raw |= (long)b[at + i] << (8 * i);
        if (c.Bits > 0)
        {
            long mask = ((1L << c.Bits) - 1) << c.Bit;
            raw = (raw & ~mask) | ((v << c.Bit) & mask);
        }
        else raw = v;
        for (int i = 0; i < c.Bytes; i++) b[at + i] = (byte)(raw >> (8 * i));
    }
}

/// <summary>Um bit por linha: serve para compatibilidade de TMs, em que cada linha é outro bit do mesmo campo.</summary>
public sealed class TableBitRow(string name, byte[] buffer, int baseOffset, int bit) : TableRow(name, [buffer], [baseOffset])
{
    public override int Get(TableColumn c) => (buffer[baseOffset + bit / 8] >> (bit % 8)) & 1;

    public override void Set(TableColumn c, int value)
    {
        byte mask = (byte)(1 << (bit % 8));
        if (value != 0) buffer[baseOffset + bit / 8] |= mask;
        else buffer[baseOffset + bit / 8] &= (byte)~mask;
    }
}

/// <summary>Nomes do jogo usados nas colunas e nas linhas.</summary>
public sealed record TableNames(IReadOnlyList<string> Species, IReadOnlyList<string> Moves, IReadOnlyList<string> Items, IReadOnlyList<string> Abilities)
{
    public static string At(IReadOnlyList<string> list, int i) => i >= 0 && i < list.Count ? list[i] : $"#{i}";
}

/// <summary>
/// Uma tabela de dados do jogo. Quando há donos, cada um tem as próprias linhas (os golpes aprendidos de cada espécie);
/// sem donos, é uma lista só. Dados e formatos conforme as decomps pokeheartgold e pokeplatinum.
/// </summary>
public abstract class GameTable(string id, IReadOnlyList<TableColumn> columns)
{
    public string Id { get; } = id;
    public IReadOnlyList<TableColumn> Columns { get; } = columns;
    public virtual IReadOnlyList<string>? Owners => null;
    public virtual bool CanResize => false;
    public abstract IList<TableRow> Rows(int owner);
    public virtual TableRow AddRow(int owner) => throw new NotSupportedException();
    public virtual void RemoveRow(int owner, int index) => throw new NotSupportedException();
    /// <summary>Recusa antes de gravar o que o jogo não aguentaria. Devolve a mensagem, ou null.</summary>
    public virtual string? Validate() => null;
    public abstract bool HasChanges { get; }
    public abstract void Save();
}

/// <summary>Arquivo gravado por inteiro, com troca atômica.</summary>
internal static class TableFiles
{
    public static void Write(string path, byte[] data)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>NARC em memória, com o estado original para saber o que mudou.</summary>
internal sealed class NarcStore
{
    private readonly byte[][] _original;
    public string Path { get; }
    public Narc Narc { get; }

    public NarcStore(string path)
    {
        Path = path;
        Narc = Narc.Read(File.ReadAllBytes(path));
        _original = Narc.Files.Select(f => f.ToArray()).ToArray();
    }

    public bool HasChanges => Narc.Files.Where((f, i) => !f.AsSpan().SequenceEqual(_original[i])).Any();

    public void Save()
    {
        TableFiles.Write(Path, Narc.Write());
        for (int i = 0; i < Narc.Files.Count; i++) _original[i] = Narc.Files[i].ToArray();
    }
}

/// <summary>Um registro por arquivo do NARC: golpes, trocas e, com o mapa do arm9, itens.</summary>
internal sealed class NarcRecordTable : GameTable
{
    private readonly NarcStore _store;
    private readonly List<TableRow> _rows = [];

    public NarcRecordTable(string id, string path, IReadOnlyList<TableColumn> columns, int recordSize, Func<int, string> name, Func<int, int>? fileOf = null, int? count = null) : base(id, columns)
    {
        _store = new NarcStore(path);
        for (int i = 0; i < (count ?? _store.Narc.Files.Count); i++)
        {
            int file = fileOf?.Invoke(i) ?? i;
            if (file < 0 || file >= _store.Narc.Files.Count || _store.Narc.Files[file].Length < recordSize) continue;
            _rows.Add(new TableRow(name(i), [_store.Narc.Files[file]], [0]));
        }
    }

    public override IList<TableRow> Rows(int owner) => _rows;
    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Evoluções: sete vagas fixas de 6 bytes em cada arquivo.</summary>
internal sealed class EvolutionTable : GameTable
{
    private const int Slots = 7, SlotSize = 6;
    private readonly NarcStore _store;
    private readonly IReadOnlyList<string> _owners;

    public EvolutionTable(string path, TableNames names) : base("evolutions",
    [
        new("method", 0, 2, TableCellKind.Choice, ChoicePrefix: "tables.evo.", ChoiceCount: 27),
        new("param", 2, 2),
        new("target", 4, 2, TableCellKind.Species),
    ])
    {
        _store = new NarcStore(path);
        _owners = Enumerable.Range(0, Math.Min(_store.Narc.Files.Count, names.Species.Count)).Select(i => $"{i:D3} {names.Species[i]}").ToList();
    }

    public override IReadOnlyList<string> Owners => _owners;

    public override IList<TableRow> Rows(int owner)
    {
        var file = _store.Narc.Files[owner];
        return Enumerable.Range(0, Slots).Where(i => (i + 1) * SlotSize <= file.Length)
            .Select(i => new TableRow($"{i + 1}", [file], [i * SlotSize])).ToList();
    }

    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Lista de u16 por dono, com fim em 0xFFFF: golpes aprendidos por nível.</summary>
internal sealed class LearnsetTable : GameTable
{
    /// <summary>Tamanho do buffer que o jogo aloca para a lista (MAX_LEARNED_MOVES nas decomps), contando o fim.</summary>
    public const int MaxEntries = 22;
    private readonly NarcStore _store;
    private readonly IReadOnlyList<string> _owners;
    private readonly Dictionary<int, List<TableRow>> _open = [];
    private readonly Dictionary<int, List<byte[]>> _cells = [];

    public LearnsetTable(string path, TableNames names) : base("learnsets",
    [
        new("level", 0, 2, Bit: 9, Bits: 7),
        new("move", 0, 2, TableCellKind.Move, Bit: 0, Bits: 9),
    ])
    {
        _store = new NarcStore(path);
        _owners = Enumerable.Range(0, Math.Min(_store.Narc.Files.Count, names.Species.Count)).Select(i => $"{i:D3} {names.Species[i]}").ToList();
    }

    public override IReadOnlyList<string> Owners => _owners;
    public override bool CanResize => true;

    public override IList<TableRow> Rows(int owner)
    {
        if (_open.TryGetValue(owner, out var open)) return open;
        var file = _store.Narc.Files[owner];
        var cells = new List<byte[]>();
        for (int i = 0; i + 2 <= file.Length && BitConverter.ToUInt16(file, i) != 0xFFFF; i += 2) cells.Add(file[i..(i + 2)]);
        _cells[owner] = cells;
        return _open[owner] = cells.Select((c, i) => new TableRow($"{i + 1}", [c], [0])).ToList();
    }

    public override TableRow AddRow(int owner)
    {
        var rows = Rows(owner);
        if (rows.Count + 1 >= MaxEntries) throw new InvalidOperationException(Loc.T("core.tables.tooMany", MaxEntries - 1));
        var cell = new byte[2];
        BitConverter.TryWriteBytes(cell, (ushort)(1 << 9 | 1));
        _cells[owner].Add(cell);
        var row = new TableRow($"{rows.Count + 1}", [cell], [0]);
        rows.Add(row);
        return row;
    }

    public override void RemoveRow(int owner, int index)
    {
        Rows(owner).RemoveAt(index);
        _cells[owner].RemoveAt(index);
        for (int i = index; i < _open[owner].Count; i++) _open[owner][i].Name = $"{i + 1}";
    }

    private void Encode()
    {
        foreach (var (owner, cells) in _cells)
        {
            // a ordem por nível é a que o jogo espera: ele para de ler ao passar do nível atual
            var sorted = cells.OrderBy(c => BitConverter.ToUInt16(c, 0) >> 9).ToList();
            using var ms = new MemoryStream();
            foreach (var c in sorted) ms.Write(c);
            ms.Write(BitConverter.GetBytes((ushort)0xFFFF));
            while (ms.Length % 4 != 0) ms.WriteByte(0);
            var bytes = ms.ToArray();
            var old = _store.Narc.Files[owner];
            // sem mudança real, mantém os bytes originais, com o preenchimento do jogo
            if (OldEntries(old).SequenceEqual(cells.Select(c => BitConverter.ToUInt16(c, 0)))) continue;
            _store.Narc.Files[owner] = bytes;
        }
    }

    private static IEnumerable<ushort> OldEntries(byte[] file)
    {
        for (int i = 0; i + 2 <= file.Length && BitConverter.ToUInt16(file, i) != 0xFFFF; i += 2) yield return BitConverter.ToUInt16(file, i);
    }

    public override bool HasChanges { get { Encode(); return _store.HasChanges; } }
    public override void Save() { Encode(); _store.Save(); }
}

/// <summary>
/// Golpes de ovo: lista única em que 20000 + espécie abre o bloco de cada espécie, com fim em 0xFFFF.
/// No HG/SS é um arquivo de NARC; no Platinum fica dentro de um overlay. Nos dois, o espaço não cresce.
/// </summary>
internal sealed class EggMovesTable : GameTable
{
    private const int Marker = 20000;
    private readonly byte[] _buffer;
    private readonly byte[] _original;
    private readonly int _start, _capacity;
    private readonly Action<byte[]> _save;
    private readonly List<int> _species = [];
    private readonly List<string> _owners = [];
    private readonly List<List<TableRow>> _rows = [];
    private readonly List<List<byte[]>> _cells = [];

    public EggMovesTable(byte[] buffer, int start, int capacity, TableNames names, Action<byte[]> save) : base("eggMoves", [new("move", 0, 2, TableCellKind.Move)])
    {
        _buffer = buffer;
        _original = buffer.ToArray();
        _start = start;
        _capacity = capacity;
        _save = save;
        for (int i = start; i + 2 <= start + capacity; i += 2)
        {
            int v = BitConverter.ToUInt16(buffer, i);
            if (v == 0xFFFF) break;
            if (v > Marker)
            {
                _species.Add(v - Marker);
                _owners.Add($"{v - Marker:D3} {TableNames.At(names.Species, v - Marker)}");
                _rows.Add([]);
                _cells.Add([]);
                continue;
            }
            if (_rows.Count == 0) continue;
            var cell = buffer[i..(i + 2)];
            _cells[^1].Add(cell);
            _rows[^1].Add(new TableRow($"{_rows[^1].Count + 1}", [cell], [0]));
        }
    }

    public override IReadOnlyList<string> Owners => _owners;
    public override bool CanResize => true;
    public override IList<TableRow> Rows(int owner) => _rows[owner];

    public override TableRow AddRow(int owner)
    {
        var cell = new byte[2];
        BitConverter.TryWriteBytes(cell, (ushort)1);
        _cells[owner].Add(cell);
        var row = new TableRow($"{_rows[owner].Count + 1}", [cell], [0]);
        _rows[owner].Add(row);
        return row;
    }

    public override void RemoveRow(int owner, int index)
    {
        _rows[owner].RemoveAt(index);
        _cells[owner].RemoveAt(index);
        for (int i = index; i < _rows[owner].Count; i++) _rows[owner][i].Name = $"{i + 1}";
    }

    private int NeededBytes => (_species.Count + _cells.Sum(c => c.Count) + 1) * 2;

    /// <summary>O total não pode passar do espaço original: para somar golpes, tire de outra espécie.</summary>
    public override string? Validate() => NeededBytes > _capacity ? Loc.T("core.tables.eggFull", (NeededBytes - _capacity) / 2) : null;

    private void Encode()
    {
        if (Validate() is not null) return;
        int at = _start;
        for (int s = 0; s < _species.Count; s++)
        {
            BitConverter.TryWriteBytes(_buffer.AsSpan(at), (ushort)(Marker + _species[s]));
            at += 2;
            foreach (var c in _cells[s]) { c.CopyTo(_buffer, at); at += 2; }
        }
        BitConverter.TryWriteBytes(_buffer.AsSpan(at), (ushort)0xFFFF);
    }

    public override bool HasChanges { get { Encode(); return !_buffer.AsSpan().SequenceEqual(_original); } }

    public override void Save()
    {
        if (Validate() is { } error) throw new InvalidOperationException(error);
        Encode();
        _save(_buffer);
        _buffer.CopyTo(_original, 0);
    }
}

/// <summary>Altura e peso: dois arquivos de u32 por espécie. Grava também na cópia da forma alternativa, que o jogo usa com o outro Giratina.</summary>
internal sealed class HeightWeightTable : GameTable
{
    private readonly NarcStore _main;
    private readonly string? _copyPath;
    private readonly List<TableRow> _rows = [];

    public HeightWeightTable(string path, string? copyPath, TableNames names) : base("heightWeight",
    [
        new("height", 0, 4),
        new("weight", 0, 4, Buffer: 1),
    ])
    {
        _main = new NarcStore(path);
        _copyPath = copyPath;
        int count = Math.Min(_main.Narc.Files[0].Length, _main.Narc.Files[1].Length) / 4;
        for (int i = 1; i < count; i++)
            _rows.Add(new TableRow($"{i:D3} {TableNames.At(names.Species, i)}", [_main.Narc.Files[0], _main.Narc.Files[1]], [i * 4, i * 4]));
    }

    public override IList<TableRow> Rows(int owner) => _rows;
    public override bool HasChanges => _main.HasChanges;

    public override void Save()
    {
        _main.Save();
        if (_copyPath is null || !File.Exists(_copyPath)) return;
        var copy = Narc.Read(File.ReadAllBytes(_copyPath));
        copy.Files[0] = _main.Narc.Files[0].ToArray();
        copy.Files[1] = _main.Narc.Files[1].ToArray();
        TableFiles.Write(_copyPath, copy.Write());
    }
}

/// <summary>Tabela dentro de um arquivo só: filhote de cada espécie, TMs, lojas, tutores. O tamanho nunca muda.</summary>
internal sealed class BinaryTable : GameTable
{
    private readonly string _path;
    private readonly byte[] _data;
    private readonly byte[] _original;
    private readonly IReadOnlyList<string>? _owners;
    private readonly IReadOnlyList<List<TableRow>> _rows;

    public BinaryTable(string id, string path, byte[] data, IReadOnlyList<TableColumn> columns, Func<byte[], IReadOnlyList<List<TableRow>>> rows, IReadOnlyList<string>? owners = null) : base(id, columns)
    {
        _path = path;
        _data = data;
        _original = data.ToArray();
        _rows = rows(data);
        _owners = owners;
    }

    public override IReadOnlyList<string>? Owners => _owners;
    public override IList<TableRow> Rows(int owner) => _rows[owner];
    public override bool HasChanges => !_data.AsSpan().SequenceEqual(_original);

    public override void Save()
    {
        TableFiles.Write(_path, _data);
        _data.CopyTo(_original, 0);
    }
}

/// <summary>Compatibilidade de TMs e HMs: um bit por golpe, no fim dos dados pessoais de cada espécie.</summary>
internal sealed class TmCompatibilityTable : GameTable
{
    private const int Offset = 0x1C, Machines = 100, Hidden = 8;
    private readonly NarcStore _store;
    private readonly IReadOnlyList<string> _owners;
    private readonly Func<int, string> _label;

    public TmCompatibilityTable(string path, TableNames names, Func<int, string> label) : base("tmCompat", [new("learns", 0, 1, TableCellKind.Flag)])
    {
        _store = new NarcStore(path);
        _label = label;
        _owners = Enumerable.Range(0, Math.Min(_store.Narc.Files.Count, names.Species.Count))
            .Where(i => _store.Narc.Files[i].Length >= PersonalData.Size).Select(i => $"{i:D3} {names.Species[i]}").ToList();
    }

    public override IReadOnlyList<string> Owners => _owners;

    public override IList<TableRow> Rows(int owner)
    {
        var file = _store.Narc.Files[owner];
        return Enumerable.Range(0, Machines + Hidden).Select(bit => (TableRow)new TableBitRow(_label(bit), file, Offset, bit)).ToList();
    }

    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Registros de tamanho fixo dentro de um arquivo de NARC.</summary>
internal sealed class NarcSliceTable : GameTable
{
    private readonly NarcStore _store;
    private readonly List<TableRow> _rows = [];

    public NarcSliceTable(string id, string path, int member, int recordSize, IReadOnlyList<TableColumn> columns, Func<int, string> name) : base(id, columns)
    {
        _store = new NarcStore(path);
        var file = _store.Narc.Files[member];
        for (int i = 0; i < file.Length / recordSize; i++) _rows.Add(new TableRow(name(i), [file], [i * recordSize]));
    }

    public override IList<TableRow> Rows(int owner) => _rows;
    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Posição do sprite em batalha: 89 bytes por espécie (SpeciesSpriteData na pokeplatinum), dos quais os três últimos são estes. As animações ficam como estão.</summary>
internal sealed class SpritePositionTable : GameTable
{
    private const int RecordSize = 89;
    private readonly NarcStore _store;
    private readonly List<TableRow> _rows = [];

    public SpritePositionTable(string path, TableNames names) : base("spritePosition",
    [
        new("yOffset", 86, 1, Signed: true),
        new("shadowX", 87, 1, Signed: true),
        new("shadowSize", 88, 1),
    ])
    {
        _store = new NarcStore(path);
        var file = _store.Narc.Files[0];
        for (int i = 1; i < file.Length / RecordSize && i < names.Species.Count; i++)
            _rows.Add(new TableRow($"{i:D3} {names.Species[i]}", [file], [i * RecordSize]));
    }

    public override IList<TableRow> Rows(int owner) => _rows;
    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Treinadores do Frontier: classe, quantidade e os números dos conjuntos de Pokémon que podem usar.</summary>
internal sealed class FrontierTrainersTable : GameTable
{
    private readonly NarcStore _store;
    private readonly IReadOnlyList<string> _owners;

    public FrontierTrainersTable(string path) : base("frontierTrainers", [new("set", 0, 2)])
    {
        _store = new NarcStore(path);
        _owners = Enumerable.Range(0, _store.Narc.Files.Count).Select(i => $"{Loc.T("tables.trainer")} {i}").ToList();
    }

    public override IReadOnlyList<string> Owners => _owners;

    public override IList<TableRow> Rows(int owner)
    {
        var file = _store.Narc.Files[owner];
        var rows = new List<TableRow> { new(Loc.T("tables.trainerClass"), [file], [0]) };
        int count = Math.Min(BitConverter.ToUInt16(file, 2), (file.Length - 4) / 2);
        for (int i = 0; i < count; i++) rows.Add(new TableRow($"{i + 1}", [file], [4 + i * 2]));
        return rows;
    }

    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Headbutt do HG/SS: um arquivo por mapa, com seis vagas comuns, seis raras e seis secretas. As árvores vêm depois e não mudam.</summary>
internal sealed class HeadbuttTable : GameTable
{
    private const int Header = 4, SlotSize = 4, SlotsPerGroup = 6;
    private readonly NarcStore _store;
    private readonly List<int> _files = [];
    private readonly List<string> _owners = [];

    public HeadbuttTable(string path, IReadOnlyList<MapHeaderInfo> headers) : base("headbutt",
    [
        new("species", 0, 2, TableCellKind.Species),
        new("minLevel", 2, 1),
        new("maxLevel", 3, 1),
    ])
    {
        _store = new NarcStore(path);
        for (int i = 0; i < _store.Narc.Files.Count; i++)
        {
            if (_store.Narc.Files[i].Length < Header + SlotSize * SlotsPerGroup * 3) continue;
            // só mapas com árvores têm o que editar
            if (BitConverter.ToUInt16(_store.Narc.Files[i], 0) + BitConverter.ToUInt16(_store.Narc.Files[i], 2) == 0) continue;
            _files.Add(i);
            _owners.Add($"{i:D3} {(i < headers.Count ? headers[i].InternalName : "")}");
        }
    }

    public override IReadOnlyList<string> Owners => _owners;

    public override IList<TableRow> Rows(int owner)
    {
        var file = _store.Narc.Files[_files[owner]];
        string[] groups = [Loc.T("tables.hb.common"), Loc.T("tables.hb.rare"), Loc.T("tables.hb.secret")];
        return Enumerable.Range(0, SlotsPerGroup * 3).Select(i => new TableRow($"{groups[i / SlotsPerGroup]} {i % SlotsPerGroup + 1}", [file], [Header + i * SlotSize])).ToList();
    }

    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Safari do HG/SS: dez vagas por período para grama, surfe e as três varas, cada grupo seguido dos bônus liberados pelos objetos. A quantidade de bônus e o que os libera não mudam.</summary>
internal sealed class SafariTable : GameTable
{
    private const int Header = 8, SlotSize = 4, Slots = 10, UnlockSize = 4;
    private static readonly string[] Methods = ["land", "surf", "oldRod", "goodRod", "superRod"];
    private static readonly string[] Times = ["morning", "day", "night"];
    private readonly NarcStore _store;
    private readonly IReadOnlyList<string> _owners;

    public SafariTable(string path) : base("safari",
    [
        new("species", 0, 2, TableCellKind.Species),
        new("level", 2, 2),
    ])
    {
        _store = new NarcStore(path);
        _owners = Enumerable.Range(0, _store.Narc.Files.Count).Select(i => Loc.T($"tables.safari.area.{i}")).ToList();
    }

    public override IReadOnlyList<string> Owners => _owners;

    public override IList<TableRow> Rows(int owner)
    {
        var file = _store.Narc.Files[owner];
        var rows = new List<TableRow>();
        int start = Header;
        for (int m = 0; m < Methods.Length; m++)
        {
            int bonus = file[m];
            for (int t = 0; t < Times.Length; t++)
                for (int i = 0; i < Slots; i++)
                {
                    int at = start + (t * Slots + i) * SlotSize;
                    rows.Add(new TableRow($"{Loc.T("tables.safari." + Methods[m])} · {Loc.T("tables.safari." + Times[t])} {i + 1}", [file], [at]));
                }
            for (int t = 0; t < Times.Length; t++)
                for (int i = 0; i < bonus; i++)
                {
                    int bonusAt = start + (Times.Length * Slots + t * bonus + i) * SlotSize;
                    rows.Add(new TableRow($"{Loc.T("tables.safari." + Methods[m])} · {Loc.T("tables.safari." + Times[t])} · {Loc.T("tables.safari.bonus")} {i + 1}", [file], [bonusAt]));
                }
            // grupo: base, bônus e as condições, de 4 bytes cada
            start += (Times.Length * Slots + Times.Length * bonus) * SlotSize + bonus * UnlockSize;
        }
        return rows;
    }

    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Um argumento de comando de script: o tamanho vem do comando, não da coluna.</summary>
internal sealed class ScriptArgRow(string name, byte[] file, int at, int size) : TableRow(name, [file], [at])
{
    public override int Get(TableColumn c) => size switch { 1 => file[at], 2 => BitConverter.ToUInt16(file, at), _ => BitConverter.ToInt32(file, at) };

    public override void Set(TableColumn c, int value)
    {
        switch (size)
        {
            case 1: file[at] = (byte)Math.Clamp(value, 0, 255); break;
            case 2: BitConverter.TryWriteBytes(file.AsSpan(at), (ushort)Math.Clamp(value, 0, 65535)); break;
            default: BitConverter.TryWriteBytes(file.AsSpan(at), value); break;
        }
    }
}

/// <summary>
/// Scripts: edita valores de argumentos no lugar, como mensagem, flag, variável, espécie, item ou treinador.
/// Saltos ficam de fora, e nada muda de tamanho, porque inserir comandos exigiria recalcular todos os saltos do arquivo.
/// </summary>
internal sealed class ScriptArgsTable : GameTable
{
    private readonly NarcStore _store;
    private readonly ScriptCommandTable _commands;
    private readonly IReadOnlyList<string> _owners;
    private readonly Dictionary<int, List<TableRow>> _open = [];

    public ScriptArgsTable(string path, ScriptCommandTable commands, IReadOnlyList<MapHeaderInfo> headers) : base("scripts", [new("value", 0, 4, Signed: true)])
    {
        _store = new NarcStore(path);
        _commands = commands;
        var maps = headers.Where(h => h.ScriptsBank >= 0).GroupBy(h => h.ScriptsBank).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(h => h.InternalName).Distinct().Take(3)));
        _owners = Enumerable.Range(0, _store.Narc.Files.Count).Select(i => maps.TryGetValue(i, out var name) ? $"{i:D4} {name}" : $"{i:D4}").ToList();
    }

    public override IReadOnlyList<string> Owners => _owners;

    public override IList<TableRow> Rows(int owner)
    {
        if (_open.TryGetValue(owner, out var open)) return open;
        var file = _store.Narc.Files[owner];
        var script = new ScriptFile(file, _commands);
        var instructions = new Dictionary<int, ScriptInstruction>();
        for (int id = 1; id <= script.EntryOffsets.Count; id++)
            foreach (var i in script.Routine(id)!.Instructions) instructions[i.Offset] = i;
        var rows = new List<TableRow>();
        foreach (var i in instructions.Values.OrderBy(i => i.Offset))
            for (int a = 0; a < i.Args.Count; a++)
                if (i.Args[a].Type is not ("script" or "movement" or "addr") && i.Args[a].Size > 0)
                    rows.Add(new ScriptArgRow($"{i.Offset:X4} {i.Name} [{a + 1}] {i.Args[a].Type}", file, i.Args[a].At, i.Args[a].Size));
        return _open[owner] = rows;
    }

    public override bool HasChanges => _store.HasChanges;
    public override void Save() => _store.Save();
}

/// <summary>Abre as tabelas disponíveis no jogo. As que moram no código só aparecem em workspace ds-rom, onde o arm9 e os overlays estão descomprimidos.</summary>
public static class GameTables
{
    private const int MachineCount = 92, HiddenCount = 8;
    private static readonly ushort[] HiddenStart = [15, 19, 57, 70];
    private static readonly int[] TutorNpcs = [0, 1, 0, 0, 1, 0, 0, 1];
    private const int TutorCount = 52, MartMinimum = 10, TypeChartMinimum = 100, BadgeMartCount = 19, EggMarkerMinimum = 100, OpponentNamesBank = 205, PlatinumTutorCount = 38, PlatinumTutorSize = 12;

    public static bool Supports(Gen4Game game) => !game.IsDp;

    public static IReadOnlyList<GameTable> Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (!Supports(game)) throw new NotSupportedException(Loc.T("core.tables.noDp"));
        var text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
        List<string> Bank(int b) => b < text.Files.Count ? Gen4TextArchive.Read(text.Files[b]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList() : [];
        var names = new TableNames(Bank(game.SpeciesBank), Bank(game.MovesBank), Bank(game.ItemsBank), Bank(game.AbilitiesBank));
        var root = game.FilesRoot;
        string P(string hgss, string pt) => Path.Combine(root, game.IsHgss ? hgss : pt);

        var tables = new List<GameTable>
        {
            new NarcRecordTable("moves", P(@"a\0\1\1", @"poketool\waza\pl_waza_tbl.narc"),
            [
                new("effect", 0, 2),
                new("category", 2, 1, TableCellKind.Choice, ChoicePrefix: "tables.category.", ChoiceCount: 3),
                new("power", 3, 1),
                new("type", 4, 1, TableCellKind.Type),
                new("accuracy", 5, 1),
                new("pp", 6, 1),
                new("effectChance", 7, 1),
                new("range", 8, 2),
                new("priority", 10, 1, Signed: true),
                new("moveFlags", 11, 1),
                new("contestEffect", 12, 1),
                new("contestType", 13, 1),
            ], 16, i => $"{i:D3} {TableNames.At(names.Moves, i)}", count: Math.Min(names.Moves.Count, 468)),
        };

        var arm9 = Blz.DecompressArm9(File.ReadAllBytes(game.Arm9Path));
        int itemMap = ItemIcons.FindTable(arm9);
        if (itemMap >= 0)
            tables.Add(new NarcRecordTable("items", P(@"a\0\1\7", @"itemtool\itemdata\pl_item_data.narc"),
            [
                new("price", 0, 2),
                new("holdEffect", 2, 1),
                new("holdParam", 3, 1),
                new("pluckEffect", 4, 1),
                new("flingEffect", 5, 1),
                new("flingPower", 6, 1),
                new("giftPower", 7, 1),
                new("giftType", 8, 2, TableCellKind.Type, Bit: 0, Bits: 5),
                new("noToss", 8, 2, TableCellKind.Flag, Bit: 5, Bits: 1),
                new("selectable", 8, 2, TableCellKind.Flag, Bit: 6, Bits: 1),
                new("fieldPocket", 8, 2, Bit: 7, Bits: 4),
                new("battlePocket", 8, 2, Bit: 11, Bits: 5),
                new("fieldUse", 10, 1),
                new("battleUse", 11, 1),
            ], 34, i => $"{i:D3} {TableNames.At(names.Items, i)}", i => BitConverter.ToUInt16(arm9, itemMap + i * 8), names.Items.Count));

        tables.Add(new LearnsetTable(P(@"a\0\3\3", @"poketool\personal\wotbl.narc"), names));
        tables.Add(new EvolutionTable(P(@"a\0\3\4", @"poketool\personal\evo.narc"), names));
        if (game.IsHgss && File.Exists(Path.Combine(root, @"a\2\2\9")))
        {
            var eggStore = new NarcStore(Path.Combine(root, @"a\2\2\9"));
            var eggFile = eggStore.Narc.Files[0];
            tables.Add(new EggMovesTable(eggFile, 0, eggFile.Length, names, _ => eggStore.Save()));
        }

        var babies = Path.Combine(root, @"poketool\personal\pms.narc");
        if (File.Exists(babies))
            tables.Add(new BinaryTable("babies", babies, File.ReadAllBytes(babies), [new("baby", 0, 2, TableCellKind.Species)],
                data => [Enumerable.Range(1, Math.Min(data.Length / 2, names.Species.Count) - 1).Select(i => new TableRow($"{i:D3} {names.Species[i]}", [data], [i * 2])).ToList()]));

        tables.Add(new HeightWeightTable(P(@"a\0\7\4", @"application\zukanlist\zkn_data\zukan_data.narc"), P(@"a\2\1\4", @"application\zukanlist\zkn_data\zukan_data_gira.narc"), names));

        var trades = P(@"a\1\1\2", @"fielddata\pokemon_trade\fld_trade.narc");
        if (File.Exists(trades))
            tables.Add(new NarcRecordTable("trades", trades,
            [
                new("giveSpecies", 0, 4, TableCellKind.Species),
                new("askSpecies", 0x4C, 4, TableCellKind.Species),
                new("hpIv", 4, 4), new("atkIv", 8, 4), new("defIv", 12, 4), new("speedIv", 16, 4), new("spAtkIv", 20, 4), new("spDefIv", 24, 4),
                new("ability", 28, 4, TableCellKind.Ability),
                new("otId", 32, 4),
                new("heldItem", 60, 4, TableCellKind.Item),
                new("gender", 64, 4),
            ], 0x50, i => $"{Loc.T("tables.trade")} {i + 1}"));

        // Pokéathlon: nível base de cada atributo (PokeathlonBasePerformance) e os limites diários, na ordem do arquivo.
        // As espécies vêm primeiro, começando no Bulbasaur; os arquivos depois delas são formas.
        var pokeathlon = Path.Combine(root, @"a\1\6\9");
        if (game.IsHgss && File.Exists(pokeathlon))
            tables.Add(new NarcRecordTable("pokeathlon", pokeathlon,
            [
                new("power", 0, 1), new("stamina", 1, 1), new("jump", 2, 1), new("skill", 3, 1), new("speedStat", 4, 1),
                new("powerMin", 9, 1), new("powerMax", 10, 1),
                new("staminaMin", 11, 1), new("staminaMax", 12, 1),
                new("jumpMin", 13, 1), new("jumpMax", 14, 1),
                new("skillMin", 15, 1), new("skillMax", 16, 1),
                new("speedMin", 17, 1), new("speedMax", 18, 1),
            ], 20, i => i < 493 ? $"{i + 1:D3} {TableNames.At(names.Species, i + 1)}" : $"{Loc.T("tables.form")} {i - 492}"));

        // Concursos: 96 oponentes de 48 bytes. Só espécie e golpes têm significado confirmado; o resto continua como está.
        var contest = Path.Combine(root, @"contest\data\contest_data.narc");
        if (game.IsPlatinum && File.Exists(contest))
        {
            var opponents = Bank(OpponentNamesBank);
            tables.Add(new NarcSliceTable("contestOpponents", contest, 0, 48,
            [
                new("species", 0x14, 2, TableCellKind.Species),
                new("move1", 0x0C, 2, TableCellKind.Move), new("move2", 0x0E, 2, TableCellKind.Move), new("move3", 0x10, 2, TableCellKind.Move), new("move4", 0x12, 2, TableCellKind.Move),
            ], i => $"{i:D2} {TableNames.At(opponents, i)}"));
        }

        var spritePosition = P(@"a\1\8\0", @"poketool\poke_edit\pl_poke_data.narc");
        if (File.Exists(spritePosition)) tables.Add(new SpritePositionTable(spritePosition, names));

        var frontierTrainers = P(@"a\2\0\2", @"battle\b_pl_tower\pl_btdtr.narc");
        if (File.Exists(frontierTrainers)) tables.Add(new FrontierTrainersTable(frontierTrainers));

        if (game.IsHgss && File.Exists(Path.Combine(root, @"a\2\5\2"))) tables.Add(new HeadbuttTable(Path.Combine(root, @"a\2\5\2"), MapHeaders.Load(game)));
        if (game.IsHgss && File.Exists(Path.Combine(root, @"a\2\3\0"))) tables.Add(new SafariTable(Path.Combine(root, @"a\2\3\0")));

        var frontier = P(@"a\2\0\3", @"battle\b_pl_tower\pl_btdpm.narc");
        if (File.Exists(frontier))
            tables.Add(new NarcRecordTable("frontierMons", frontier,
            [
                new("species", 0, 2, TableCellKind.Species),
                new("move1", 2, 2, TableCellKind.Move), new("move2", 4, 2, TableCellKind.Move), new("move3", 6, 2, TableCellKind.Move), new("move4", 8, 2, TableCellKind.Move),
                new("evFlags", 10, 1),
                new("nature", 11, 1),
                new("heldItem", 12, 2, TableCellKind.Item),
                new("form", 14, 2),
            ], 16, i => $"{i:D3}"));

        tables.Add(new ScriptArgsTable(game.ScriptsPath, game.IsHgss ? ScriptCommandTable.Hgss : ScriptCommandTable.Platinum, MapHeaders.Load(game)));

        // o que mora no código só é editável sem compressão
        if (Directory.Exists(Path.Combine(project.Paths.Workspace, "arm9")))
        {
            int hidden = FindHiddenMachines(arm9);
            int machines = hidden - MachineCount * 2;
            string Machine(int bit) => bit < MachineCount
                ? $"TM{bit + 1:D2} {(machines >= 0 ? TableNames.At(names.Moves, BitConverter.ToUInt16(arm9, machines + bit * 2)) : "")}"
                : bit < 100 ? $"TM{bit + 1:D2}"
                : $"HM{bit - 99:D2} {(hidden >= 0 ? TableNames.At(names.Moves, BitConverter.ToUInt16(arm9, hidden + (bit - 100) * 2)) : "")}";
            tables.Add(new TmCompatibilityTable(game.PersonalPath, names, Machine));
            if (machines >= 0)
                tables.Add(new BinaryTable("tmMoves", game.Arm9Path, arm9, [new("move", 0, 2, TableCellKind.Move)],
                    data => [Enumerable.Range(0, MachineCount).Select(i => new TableRow($"TM{i + 1:D2}", [data], [machines + i * 2])).ToList()]));

            uint baseAddress = BaseAddress(project.Paths.Workspace);
            var marts = FindMarts(arm9, baseAddress, names.Items.Count);
            if (marts.Count > 0)
                tables.Add(new BinaryTable("marts", game.Arm9Path, arm9, [new("item", 0, 2, TableCellKind.Item)],
                    data => marts.Select(m => Enumerable.Range(0, m.Count).Select(i => new TableRow($"{i + 1}", [data], [m.Offset + i * 2])).ToList()).ToList(),
                    marts.Select((m, i) => $"{Loc.T("tables.mart")} {i}").ToList()));

            int badges = FindBadgeMart(arm9, names.Items.Count);
            if (badges >= 0)
                tables.Add(new BinaryTable("martBadges", game.Arm9Path, arm9,
                [
                    new("item", 0, 2, TableCellKind.Item),
                    new("badges", 2, 2),
                ], data => [Enumerable.Range(0, BadgeMartCount).Select(i => new TableRow($"{i + 1}", [data], [badges + i * 4])).ToList()]));

            if (game.IsPlatinum && FindEggMoves(project.Paths.Workspace) is { } egg)
            {
                var bytes = File.ReadAllBytes(egg.Path);
                tables.Add(new EggMovesTable(bytes, egg.Offset, egg.Capacity, names, data => TableFiles.Write(egg.Path, data)));
            }

            if (game.IsPlatinum && FindPlatinumTutors(project.Paths.Workspace, names.Moves.Count) is { } shards)
            {
                var bytes = File.ReadAllBytes(shards.Path);
                tables.Add(new BinaryTable("tutors", shards.Path, bytes,
                [
                    new("move", 0, 2, TableCellKind.Move),
                    new("shardRed", 2, 1), new("shardBlue", 3, 1), new("shardYellow", 4, 1), new("shardGreen", 5, 1),
                ], data => [Enumerable.Range(0, PlatinumTutorCount).Select(i => new TableRow($"{i + 1}", [data], [shards.Offset + i * PlatinumTutorSize])).ToList()]));
            }

            if (FindTypeChart(project.Paths.Workspace) is { } chart)
            {
                var bytes = File.ReadAllBytes(chart.Path);
                tables.Add(new BinaryTable("typeChart", chart.Path, bytes,
                [
                    new("attacker", 0, 1, TableCellKind.Type),
                    new("defender", 1, 1, TableCellKind.Type),
                    new("multiplier", 2, 1),
                ], data => [chart.Rows.Select((at, i) => new TableRow($"{i + 1}", [data], [at])).ToList()]));
            }

            if (game.IsHgss && FindTutors(project.Paths.Workspace, names.Moves.Count) is { } tutors)
            {
                var bytes = File.ReadAllBytes(tutors.Path);
                tables.Add(new BinaryTable("tutors", tutors.Path, bytes,
                [
                    new("move", 0, 2, TableCellKind.Move),
                    new("cost", 2, 1),
                ], data => [Enumerable.Range(0, TutorCount).Select(i => new TableRow($"{i + 1}", [data], [tutors.Offset + i * 4])).ToList()]));
            }
        }
        return tables;
    }

    private static uint BaseAddress(string workspace)
    {
        foreach (var line in File.ReadLines(Path.Combine(workspace, "arm9", "arm9.yaml")))
            if (line.StartsWith("base_address:", StringComparison.Ordinal)) return uint.Parse(line.AsSpan(13).Trim());
        throw new InvalidDataException(Loc.T("core.inject.needDsRom"));
    }

    /// <summary>Os HMs raramente mudam e ficam logo depois dos TMs, então servem de âncora mesmo com TMs editados.</summary>
    public static int FindHiddenMachines(byte[] arm9)
    {
        var signature = new byte[HiddenStart.Length * 2];
        Buffer.BlockCopy(HiddenStart, 0, signature, 0, signature.Length);
        for (int at = arm9.AsSpan().IndexOf(signature); at >= 0;)
        {
            if (at % 2 == 0 && at >= MachineCount * 2) return at;
            int next = arm9.AsSpan(at + 2).IndexOf(signature);
            at = next < 0 ? -1 : at + 2 + next;
        }
        return -1;
    }

    /// <summary>
    /// A tabela de lojas é uma lista de ponteiros para listas de itens com fim em 0xFFFF. Procurar pela forma, e não pelo conteúdo,
    /// continua funcionando em qualquer idioma e depois de editar as lojas.
    /// </summary>
    public static List<(int Offset, int Count)> FindMarts(byte[] arm9, uint baseAddress, int itemCount)
    {
        bool List(uint pointer, out (int Offset, int Count) list)
        {
            list = default;
            long at = pointer - (long)baseAddress;
            if (at < 0 || at + 2 > arm9.Length || at % 2 != 0) return false;
            int n = 0;
            for (long o = at; o + 2 <= arm9.Length; o += 2, n++)
            {
                int v = BitConverter.ToUInt16(arm9, (int)o);
                if (v == 0xFFFF)
                {
                    list = ((int)at, n);
                    return n > 0;
                }
                if (v == 0 || v >= itemCount || n > 64) return false;
            }
            return false;
        }

        var best = new List<(int Offset, int Count)>();
        for (int start = 0; start + 4 <= arm9.Length; start += 4)
        {
            var found = new List<(int Offset, int Count)>();
            for (int o = start; o + 4 <= arm9.Length && List(BitConverter.ToUInt32(arm9, o), out var list); o += 4) found.Add(list);
            if (found.Count > best.Count) best = found;
            if (found.Count > 0) start += (found.Count - 1) * 4;
        }
        return best.Count >= MartMinimum ? best : [];
    }

    /// <summary>Loja comum: pares (item, insígnias exigidas). É a maior sequência desse formato no arm9, então continua achando depois de editada.</summary>
    private static int FindBadgeMart(byte[] arm9, int itemCount)
    {
        int best = -1, bestCount = 0;
        for (int at = 0; at + 4 <= arm9.Length; at += 2)
        {
            int n = 0;
            for (int o = at; o + 4 <= arm9.Length; o += 4, n++)
            {
                int item = BitConverter.ToUInt16(arm9, o), tier = BitConverter.ToUInt16(arm9, o + 2);
                if (item == 0 || item >= itemCount || tier is 0 or > 8) break;
            }
            if (n > bestCount) { best = at; bestCount = n; }
        }
        return bestCount == BadgeMartCount ? best : -1;
    }

    /// <summary>No Platinum a lista de golpes de ovo fica no overlay 5: marcadores de espécie e golpes até 0xFFFF.</summary>
    private static (string Path, int Offset, int Capacity)? FindEggMoves(string workspace)
    {
        var path = Path.Combine(workspace, "arm9_overlays", "ov005.bin");
        if (!File.Exists(path)) return null;
        var d = File.ReadAllBytes(path);
        for (int at = 0; at + 2 <= d.Length; at += 2)
        {
            if (BitConverter.ToUInt16(d, at) is not (> 20000 and <= 20493)) continue;
            int markers = 0, o = at;
            for (; o + 2 <= d.Length; o += 2)
            {
                int v = BitConverter.ToUInt16(d, o);
                if (v == 0xFFFF) break;
                if (v is > 20000 and <= 20493) markers++;
                else if (v == 0 || v >= 470) { markers = 0; break; }
            }
            if (markers >= EggMarkerMinimum) return (path, at, o + 2 - at);
        }
        return null;
    }

    /// <summary>Tutores do Platinum: 38 linhas de 12 bytes no overlay 5, com custos em cacos pequenos. A linha seguinte não é golpe.</summary>
    private static (string Path, int Offset)? FindPlatinumTutors(string workspace, int moveCount)
    {
        var path = Path.Combine(workspace, "arm9_overlays", "ov005.bin");
        if (!File.Exists(path)) return null;
        var d = File.ReadAllBytes(path);
        for (int at = 0; at + (PlatinumTutorCount + 1) * PlatinumTutorSize <= d.Length; at += 2)
        {
            bool ok = true;
            for (int i = 0; i < PlatinumTutorCount && ok; i++)
            {
                int o = at + i * PlatinumTutorSize;
                ok = BitConverter.ToUInt16(d, o) is > 0 and var m && m < moveCount
                    && d[o + 2] <= 40 && d[o + 3] <= 40 && d[o + 4] <= 40 && d[o + 5] <= 40 && BitConverter.ToUInt16(d, o + 6) == 0 && BitConverter.ToUInt32(d, o + 8) <= 3;
            }
            int next = at + PlatinumTutorCount * PlatinumTutorSize;
            if (ok && BitConverter.ToUInt16(d, next) is 0 or >= 470) return (path, at);
        }
        return null;
    }

    /// <summary>
    /// Tabela de tipos: trincas (ataque, defesa, multiplicador 0, 5 ou 20), com 0xFE antes das exceções de Foresight e 0xFF no fim.
    /// Procura pela forma, então continua achando depois de editada.
    /// </summary>
    private static (string Path, List<int> Rows)? FindTypeChart(string workspace)
    {
        var folder = Path.Combine(workspace, "arm9_overlays");
        if (!Directory.Exists(folder)) return null;
        static bool Entry(byte[] d, int o) => d[o] <= 17 && d[o] != 9 && d[o + 1] <= 17 && d[o + 1] != 9 && d[o + 2] is 0 or 5 or 20;
        foreach (var path in Directory.GetFiles(folder, "*.bin"))
        {
            var d = File.ReadAllBytes(path);
            for (int at = 0; at + 3 <= d.Length; at++)
            {
                var rows = new List<int>();
                int o = at;
                while (o + 3 <= d.Length)
                {
                    if (d[o] == 0xFE) { o += 3; continue; }
                    if (!Entry(d, o)) break;
                    rows.Add(o);
                    o += 3;
                }
                if (rows.Count >= TypeChartMinimum && o + 3 <= d.Length && d[o] == 0xFF) return (path, rows);
            }
        }
        return null;
    }

    private static (string Path, int Offset)? FindTutors(string workspace, int moveCount)
    {
        var folder = Path.Combine(workspace, "arm9_overlays");
        if (!Directory.Exists(folder)) return null;
        foreach (var path in Directory.GetFiles(folder, "*.bin"))
        {
            var d = File.ReadAllBytes(path);
            for (int at = 0; at + TutorCount * 4 <= d.Length; at += 2)
            {
                bool ok = true;
                for (int i = 0; i < TutorNpcs.Length && ok; i++) ok = d[at + i * 4 + 3] == TutorNpcs[i];
                for (int i = 0; i < TutorCount && ok; i++) ok = BitConverter.ToUInt16(d, at + i * 4) is > 0 and var m && m < moveCount && d[at + i * 4 + 3] <= 3;
                if (ok) return (path, at);
            }
        }
        return null;
    }
}
