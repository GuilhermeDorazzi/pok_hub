using System.Text;

namespace PokeHub.Core.Plugins;

/// <summary>
/// O melonDS começa com todas as teclas do teclado sem mapeamento (-1) e o jogo só responde ao toque.
/// Aqui entra um mapeamento padrão no melonDS.toml, só enquanto nenhum botão foi configurado: quem escolheu as próprias teclas não é tocado.
/// </summary>
public static class MelonDsKeys
{
    private const string Section = "[Instance0.Keyboard]";

    /// <summary>Códigos de tecla do Qt: letras em ASCII maiúsculo, teclas especiais a partir de 0x01000000.</summary>
    private static readonly IReadOnlyDictionary<string, int> Defaults = new Dictionary<string, int>
    {
        ["A"] = 'X', ["B"] = 'Z', ["X"] = 'S', ["Y"] = 'A', ["L"] = 'Q', ["R"] = 'W',
        ["Start"] = 0x01000004,  // Enter
        ["Select"] = 0x01000003, // Backspace
        ["Left"] = 0x01000012, ["Up"] = 0x01000013, ["Right"] = 0x01000014, ["Down"] = 0x01000015,
    };

    /// <summary>O conteúdo novo do arquivo, ou null se já há algum botão configurado. Atalhos (HK_) e outras seções ficam como estão.</summary>
    public static string? WithDefaults(string? toml)
    {
        toml ??= "";
        string newline = toml.Contains("\r\n") ? "\r\n" : "\n";
        var lines = toml.Split(newline).ToList();
        int start = lines.FindIndex(l => l.Trim() == Section);
        if (start < 0)
        {
            var text = new StringBuilder(toml);
            if (toml.Length > 0 && !toml.EndsWith(newline)) text.Append(newline);
            text.Append(Section).Append(newline);
            foreach (var (key, value) in Defaults) text.Append(key).Append(" = ").Append(value).Append(newline);
            return text.ToString();
        }

        int end = start + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('[')) end++;

        var missing = new HashSet<string>(Defaults.Keys);
        for (int i = start + 1; i < end; i++)
        {
            var parts = lines[i].Split('=', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim();
            if (!Defaults.TryGetValue(key, out var value)) continue;
            if (parts[1].Trim() != "-1") return null;
            lines[i] = $"{key} = {value}";
            missing.Remove(key);
        }
        lines.InsertRange(start + 1, missing.Select(k => $"{k} = {Defaults[k]}"));
        return string.Join(newline, lines);
    }
}
