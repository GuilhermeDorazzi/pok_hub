using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;

namespace PokeHub.Core.CodeInjection;

/// <summary>Um ponto do jogo alterado por um patch.</summary>
public sealed class PatchHook
{
    /// <summary>"arm9" ou "overlay:N".</summary>
    public string Target { get; set; } = "arm9";
    /// <summary>Endereço na memória do jogo, como na decomp (ex.: "0x02000E3C").</summary>
    public string Address { get; set; } = "";
    /// <summary>"call" (BL, 4 bytes), "jump" (substitui a função: 12 ou 14 bytes em Thumb, 8 em ARM), "pointer" (endereço, 4 bytes) ou "bytes".</summary>
    public string Type { get; set; } = "call";
    /// <summary>Função do patch, símbolo do jogo ou endereço. Não vale para "bytes".</summary>
    public string? Symbol { get; set; }
    /// <summary>Hexadecimal a gravar, só em "bytes".</summary>
    public string? Bytes { get; set; }
    /// <summary>Modo da instrução no ponto alterado: "thumb" (quase todo o jogo) ou "arm".</summary>
    public string Mode { get; set; } = "thumb";
    /// <summary>Bytes que devem estar lá antes do patch. Protege contra endereço de outra versão do jogo.</summary>
    public string? Expect { get; set; }
}

/// <summary>patch.json de um patch de código.</summary>
public sealed class PatchManifest
{
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>Códigos de jogo aceitos (ex.: "IPGE"). Endereços mudam entre versões, então vazio só serve para patch sem endereço fixo.</summary>
    public List<string> GameCodes { get; set; } = [];
    /// <summary>Funções e dados do jogo usados pelo código: nome → endereço (ímpar para função Thumb).</summary>
    public Dictionary<string, string> Symbols { get; set; } = [];
    public List<PatchHook> Hooks { get; set; } = [];
}

/// <summary>Patch de código em workspace/code-patches/&lt;nome&gt;/: patch.json e fontes C ou assembly. Fica no workspace para entrar nas versões do projeto.</summary>
public sealed partial class CodePatch
{
    public const string ManifestFileName = "patch.json";

    public required string Name { get; init; }
    public required string Directory { get; init; }
    public required PatchManifest Manifest { get; init; }

    public static CodePatch Load(string directory)
    {
        var name = Path.GetFileName(directory);
        PatchManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(Path.Combine(directory, ManifestFileName)), HubConfig.JsonOptions)
                ?? throw new InvalidDataException("null");
        }
        catch (JsonException ex) { throw new InvalidDataException(Loc.T("core.inject.badManifest", name, ex.Message), ex); }
        var patch = new CodePatch { Name = name, Directory = directory, Manifest = manifest };
        patch.Validate();
        return patch;
    }

    public void Save() =>
        File.WriteAllText(Path.Combine(Directory, ManifestFileName), JsonSerializer.Serialize(Manifest, HubConfig.JsonOptions));

    public void Validate()
    {
        void Fail(string detail) => throw new InvalidDataException(Loc.T("core.inject.badManifest", Name, detail));
        foreach (var (symbol, value) in Manifest.Symbols)
        {
            if (!SymbolName().IsMatch(symbol)) Fail($"symbols: \"{symbol}\"");
            if (!IsNumber(value)) Fail($"symbols.{symbol}: \"{value}\"");
        }
        foreach (var h in Manifest.Hooks)
        {
            if (!IsNumber(h.Address)) Fail($"address: \"{h.Address}\"");
            if (h.Target != "arm9" && !OverlayTarget().IsMatch(h.Target)) Fail($"target: \"{h.Target}\"");
            if (h.Mode is not ("thumb" or "arm")) Fail($"mode: \"{h.Mode}\"");
            if (h.Expect is not null && !IsHex(h.Expect)) Fail($"expect: \"{h.Expect}\"");
            switch (h.Type)
            {
                case "call" or "jump" or "pointer":
                    if (h.Symbol is null || !(IsNumber(h.Symbol) || SymbolName().IsMatch(h.Symbol))) Fail($"symbol: \"{h.Symbol}\"");
                    break;
                case "bytes":
                    if (h.Bytes is null || !IsHex(h.Bytes)) Fail($"bytes: \"{h.Bytes}\"");
                    break;
                default:
                    Fail($"type: \"{h.Type}\"");
                    break;
            }
        }
    }

    public static int? OverlayId(string target) => target.StartsWith("overlay:", StringComparison.Ordinal) ? int.Parse(target[8..], CultureInfo.InvariantCulture) : null;

    public static bool IsNumber(string? s) =>
        s is not null && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    public static uint ParseNumber(string s) => uint.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>Hexadecimal com ou sem espaços, em número par de dígitos.</summary>
    public static byte[] ParseHex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    private static bool IsHex(string s)
    {
        var compact = s.Replace(" ", "");
        return compact.Length > 0 && compact.Length % 2 == 0 && compact.All(Uri.IsHexDigit);
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SymbolName();

    [GeneratedRegex(@"^overlay:\d{1,4}$")]
    private static partial Regex OverlayTarget();
}
