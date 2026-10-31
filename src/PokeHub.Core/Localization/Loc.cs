using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PokeHub.Core.Localization;

/// <summary>
/// Textos da interface por idioma. Dicionários em Localization/*.json (embutidos). O indexador permite
/// binding em XAML ({loc:T chave}); PropertyChanged dispara ao trocar o idioma, e a interface atualiza na hora.
/// Chaves ausentes caem para pt-BR e, por fim, para a própria chave entre colchetes.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Default = "pt-BR";
    public static readonly IReadOnlyList<(string Code, string Name)> Available =
    [
        ("pt-BR", "Português (Brasil)"),
        ("en-US", "English"),
    ];

    public static Loc Instance { get; } = new();

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private string _language = Default;

    public event PropertyChangedEventHandler? PropertyChanged;

    private Loc()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("i18n/", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            var table = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
            _tables[name[5..^5]] = table;
        }
    }

    public string Language
    {
        get => _language;
        set
        {
            var code = Available.Any(a => a.Code.Equals(value, StringComparison.OrdinalIgnoreCase)) ? value : Default;
            if (code == _language) return;
            _language = code;
            try { CultureInfo.CurrentUICulture = new CultureInfo(code); } catch { }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            LanguageChanged?.Invoke();
        }
    }

    public event Action? LanguageChanged;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (_tables.TryGetValue(_language, out var t) && t.TryGetValue(key, out var s)) return s;
        if (_tables.TryGetValue(Default, out var d) && d.TryGetValue(key, out var s2)) return s2;
        return $"[{key}]";
    }

    /// <summary>Texto da chave com argumentos de formato.</summary>
    public static string T(string key, params object?[] args)
    {
        var s = Instance.Get(key);
        return args.Length == 0 ? s : string.Format(CultureInfo.CurrentCulture, s, args);
    }

    /// <summary>Idioma inicial: o salvo, senão o do sistema se suportado, senão pt-BR.</summary>
    public static string Resolve(string? saved)
    {
        if (!string.IsNullOrEmpty(saved)) return saved;
        var ui = CultureInfo.CurrentUICulture.Name;
        var match = Available.FirstOrDefault(a => a.Code.Equals(ui, StringComparison.OrdinalIgnoreCase)).Code
                 ?? Available.FirstOrDefault(a => a.Code.StartsWith(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)).Code;
        return match ?? Default;
    }

    public IReadOnlyCollection<string> Keys(string language) => _tables.TryGetValue(language, out var t) ? t.Keys : [];
}
