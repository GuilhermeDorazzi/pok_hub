using PokeHub.Core.Localization;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>Uma linha do editor de scripts, já com nomes no lugar dos números que têm nome. Line conta a partir do início do script.</summary>
public sealed record ScriptListingLine(ScriptInstruction Instruction, string Entry, int Line, string Args);

/// <summary>Monta a listagem do editor: flags e variáveis com nome, e saltos dizendo em que script e linha caem.</summary>
public sealed class ScriptListing
{
    private readonly FlagNames _names;
    private readonly Dictionary<int, string> _targets = [];

    public IReadOnlyList<ScriptListingLine> Lines { get; }

    public ScriptListing(ScriptEditor editor, FlagNames names)
    {
        _names = names;
        var entries = new Dictionary<int, int>();
        for (int i = 0; i < editor.File.EntryOffsets.Count; i++) entries.TryAdd(editor.File.EntryOffsets[i], i + 1);

        // a primeira passada numera as linhas, porque um salto pode apontar para a frente
        var numbered = new List<(ScriptInstruction Instruction, string Entry, int Line)>();
        int script = 0, line = 0;
        foreach (var instruction in editor.Instructions())
        {
            bool starts = entries.TryGetValue(instruction.Offset, out var number);
            if (starts) { script = number; line = 0; }
            line++;
            numbered.Add((instruction, starts ? Loc.T("scr.entry", number) : "", line));
            if (script > 0) _targets[instruction.Offset] = Loc.T("scr.jumpTarget", script, line);
        }
        Lines = numbered.Select(n => new ScriptListingLine(n.Instruction, n.Entry, n.Line, string.Join(", ", n.Instruction.Args.Select(ArgText)))).ToList();
    }

    private string ArgText(ScriptArg arg) => $"{arg.Type}: {Describe(arg.Type, arg.Value) ?? arg.Value.ToString()}";

    /// <summary>O que um valor quer dizer para esse tipo de argumento, ou null se ele é só um número.</summary>
    public string? Describe(string type, long value) => type switch
    {
        "flag" => FlagNames.Label((int)value, _names.Flag((int)value)),
        // argumentos de variável também aceitam um número direto: abaixo da faixa das variáveis, é só o número
        "var" => _names.Var((int)value) is { } name ? FlagNames.Label((int)value, name) : value >= _names.VarBase ? FlagNames.Label((int)value, null) : null,
        "script" or "movement" or "addr" => _targets.TryGetValue((int)value, out var target) ? $"{target} ({value:X4})" : $"{value:X4}",
        _ => null,
    };
}
