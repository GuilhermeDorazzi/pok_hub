using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>O NARC de scripts com um editor por arquivo aberto, para a janela e para o servidor MCP.</summary>
public sealed class ScriptArchive
{
    private readonly Narc _narc;
    private readonly Dictionary<int, ScriptEditor> _open = [];
    private readonly Dictionary<int, int> _messageBank = [];
    private readonly Narc _text;

    public Gen4Game Game { get; }
    public ScriptCommandTable Table { get; }
    public IReadOnlyList<string> Names { get; }

    private ScriptArchive(Gen4Game game)
    {
        Game = game;
        Table = game.IsHgss ? ScriptCommandTable.Hgss : ScriptCommandTable.Platinum;
        _narc = Narc.Read(File.ReadAllBytes(game.ScriptsPath));
        _text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
        var headers = MapHeaders.Load(game).Where(h => h.ScriptsBank >= 0).ToList();
        foreach (var h in headers) _messageBank.TryAdd(h.ScriptsBank, h.MsgBank);
        var maps = headers.GroupBy(h => h.ScriptsBank).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(h => h.InternalName).Distinct().Take(3)));
        Names = Enumerable.Range(0, _narc.Files.Count).Select(i => maps.TryGetValue(i, out var name) ? $"{i:D4} {name}" : $"{i:D4}").ToList();
    }

    /// <summary>Diamond e Pearl ficam de fora, como no mapa de eventos: a tabela de comandos é outra.</summary>
    public static ScriptArchive Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (game.IsDp) throw new NotSupportedException(Localization.Loc.T("core.ev.hgssOnly"));
        return new ScriptArchive(game);
    }

    public ScriptEditor Open(int file)
    {
        if (!_open.TryGetValue(file, out var editor)) _open[file] = editor = new ScriptEditor(_narc.Files[file], Table);
        return editor;
    }

    /// <summary>Banco de texto do mapa dono do arquivo, ou -1.</summary>
    public int MessageBank(int file) => _messageBank.GetValueOrDefault(file, -1);

    /// <summary>Texto de uma mensagem do mapa dono do arquivo, para mostrar ao lado do comando.</summary>
    public string? Message(int file, int index)
    {
        if (!_messageBank.TryGetValue(file, out var bank) || bank < 0 || bank >= _text.Files.Count) return null;
        var messages = Gen4TextArchive.Read(_text.Files[bank]).Messages;
        return index >= 0 && index < messages.Count ? Gen4TextArchive.Decode(messages[index]) : null;
    }

    public bool HasChanges => _open.Any(o => !ReferenceEquals(o.Value.Raw, _narc.Files[o.Key]) && !o.Value.Raw.AsSpan().SequenceEqual(_narc.Files[o.Key]));

    public void Save()
    {
        foreach (var (file, editor) in _open) _narc.Files[file] = editor.Raw;
        var temp = Game.ScriptsPath + ".tmp";
        File.WriteAllBytes(temp, _narc.Write());
        File.Move(temp, Game.ScriptsPath, overwrite: true);
    }
}
