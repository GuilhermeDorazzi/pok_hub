using System.Reflection;
using System.Text.Json;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>
/// Nomes e limites de flags e variáveis (HGSS: constants/flags.h e vars.h do pokeheartgold; Platinum: vars_flags.txt da pokeplatinum; embutidos).
/// Flags: 0..NumFlags-1 salvas; 0x4000+ são temporárias (não salvas). Variáveis: VarBase..VarBase+NumVars-1 salvas; 0x8000+ especiais.
/// Nomes UNK_/UNUSED_ indicam que a decomp não identificou uso. GEN5: outros limites.
/// </summary>
public sealed class FlagNames
{
    public int NumFlags { get; }
    public int VarBase { get; }
    public int NumVars { get; }
    private readonly Dictionary<int, string> _flags;
    private readonly Dictionary<int, string> _vars;

    private FlagNames(int numFlags, int varBase, int numVars, Dictionary<int, string> flags, Dictionary<int, string> vars)
    {
        NumFlags = numFlags; VarBase = varBase; NumVars = numVars; _flags = flags; _vars = vars;
    }

    private sealed class Doc
    {
        public int NumFlags { get; set; } public int VarBase { get; set; } public int NumVars { get; set; }
        public Dictionary<string, string> Flags { get; set; } = []; public Dictionary<string, string> Vars { get; set; } = [];
    }

    private static FlagNames LoadResource(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"{resource} embutido não encontrado.");
        var doc = JsonSerializer.Deserialize<Doc>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return new FlagNames(doc.NumFlags, doc.VarBase, doc.NumVars,
            doc.Flags.ToDictionary(k => int.Parse(k.Key), k => k.Value), doc.Vars.ToDictionary(k => int.Parse(k.Key), k => k.Value));
    }

    private static readonly Lazy<FlagNames> _hgss = new(() => LoadResource("flagnames-hgss.json"));
    private static readonly Lazy<FlagNames> _platinum = new(() => LoadResource("flagnames-platinum.json"));

    public static FlagNames Hgss => _hgss.Value;
    public static FlagNames Platinum => _platinum.Value;

    public string? Flag(int id) => _flags.GetValueOrDefault(id);
    public string? Var(int id) => _vars.GetValueOrDefault(id);
    public bool IsSavedFlag(int id) => id > 0 && id < NumFlags;
    public bool IsSavedVar(int id) => id >= VarBase && id < VarBase + NumVars;
    /// <summary>A decomp não deu uso a esta flag/variável (nome UNK_/UNUSED_ ou sem nome).</summary>
    public bool IsUnidentified(string? name) => name is null || name.Contains("UNK_") || name.Contains("UNUSED");
}
