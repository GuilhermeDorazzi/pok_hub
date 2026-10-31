using System.Reflection;
using System.Text.Json;

namespace PokeHub.Core.Gen4;

/// <summary>Tabela de caracteres dos jogos de Gen 4 (código u16 ↔ texto), embutida a partir do charmap.json do DSPRE.</summary>
public sealed class CharMap
{
    private static readonly Lazy<CharMap> _default = new(LoadEmbedded);
    public static CharMap Default => _default.Value;

    private readonly Dictionary<ushort, string> _toText = new();
    private readonly Dictionary<string, ushort> _toCode = new(StringComparer.Ordinal);
    private readonly Dictionary<ushort, string> _commands = new();
    private readonly Dictionary<string, ushort> _commandCodes = new(StringComparer.Ordinal);
    private readonly int _maxTextLength;

    public const ushort Terminator = 0xFFFF;
    public const ushort CommandMarker = 0xFFFE;
    public const ushort CompressedMarker = 0xF100;

    private CharMap(JsonDocument doc)
    {
        foreach (var p in doc.RootElement.GetProperty("char_map").EnumerateObject())
        {
            var code = Convert.ToUInt16(p.Name, 16);
            var text = p.Value.GetProperty("char").GetString() ?? "";
            _toText[code] = text;
            _toCode.TryAdd(text, code);
            if (p.Value.TryGetProperty("aliases", out var aliases))
                foreach (var a in aliases.EnumerateArray()) _toCode.TryAdd(a.GetString() ?? "", code);
        }
        if (doc.RootElement.TryGetProperty("command_map", out var cmds))
            foreach (var p in cmds.EnumerateObject())
            {
                var code = Convert.ToUInt16(p.Name, 16);
                var name = p.Value.GetString() ?? "";
                _commands[code] = name;
                _commandCodes.TryAdd(name, code);
            }
        _maxTextLength = _toCode.Keys.Max(k => k.Length);
    }

    private static CharMap LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("charmap.json")
            ?? throw new InvalidOperationException("charmap.json embutido não encontrado.");
        return new CharMap(JsonDocument.Parse(stream));
    }

    public string? CharOf(ushort code) => _toText.GetValueOrDefault(code);
    public string? CommandOf(ushort code) => _commands.GetValueOrDefault(code);
    public bool TryCode(string text, out ushort code) => _toCode.TryGetValue(text, out code);
    public bool TryCommandCode(string name, out ushort code) => _commandCodes.TryGetValue(name, out code);
    public int MaxTextLength => _maxTextLength;
}
