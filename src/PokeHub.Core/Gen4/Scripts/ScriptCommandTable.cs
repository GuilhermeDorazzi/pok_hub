using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>Um comando de script: nome e tipos dos argumentos (tamanho em bytes ou tipo nomeado).</summary>
public sealed class ScriptCommand
{
    public required string Name { get; set; }
    public List<JsonElement> Args { get; set; } = [];
    /// <summary>Comandos com argumentos variáveis: o valor do argumento SwitchArg escolhe a lista extra.</summary>
    public Dictionary<string, List<JsonElement>>? Cases { get; set; }
    public int? SwitchArg { get; set; }
    public bool IsReturn { get; set; }
    public bool IsAbsBranch { get; set; }
}

/// <summary>
/// Tabelas de comandos de script: HGSS (scrcmd.json do pokeheartgold) e Platinum (gerada das macros asm/macros/scrcmd.inc da pokeplatinum), embutidas. Tamanhos: inteiros 1/2/4;
/// "message" 1 byte; "condition" e "object1" 1; "script", "movement" e "addr" 4 (deslocamento relativo ao fim do argumento);
/// todos os outros tipos nomeados (var, flag, item, species, trainer, maps...) 2 bytes.
/// GEN5: BW têm outra tabela.
/// </summary>
public sealed class ScriptCommandTable
{
    public IReadOnlyList<ScriptCommand> Commands { get; }

    private ScriptCommandTable(List<ScriptCommand> commands) => Commands = commands;

    private sealed class Doc { public List<ScriptCommand> Commands { get; set; } = []; }

    private static ScriptCommandTable LoadResource(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} embutido não encontrado.");
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        var doc = JsonSerializer.Deserialize<Doc>(stream, opts)!;
        return new ScriptCommandTable(doc.Commands);
    }

    private static readonly Lazy<ScriptCommandTable> _hgss = new(() => LoadResource("scrcmd-hgss.json"));
    private static readonly Lazy<ScriptCommandTable> _platinum = new(() => LoadResource("scrcmd-platinum.json"));

    public static ScriptCommandTable Hgss => _hgss.Value;
    public static ScriptCommandTable Platinum => _platinum.Value;

    public ScriptCommand? Get(int opcode) => opcode >= 0 && opcode < Commands.Count ? Commands[opcode] : null;

    /// <summary>Tamanho em bytes de um argumento pelo seu tipo.</summary>
    public static int ArgSize(JsonElement arg)
    {
        if (arg.ValueKind == JsonValueKind.Number) return arg.GetInt32();
        return ArgSize(arg.GetString() ?? "");
    }

    public static int ArgSize(string type) => type switch
    {
        "message" or "condition" or "object1" => 1,
        "script" or "movement" or "addr" => 4,
        _ when type.StartsWith("bool") || type.StartsWith("hex") => int.Parse(type[^1..]),
        _ => 2,
    };

    public static string ArgType(JsonElement arg) => arg.ValueKind == JsonValueKind.Number ? "int" + arg.GetInt32() : arg.GetString() ?? "";
}
