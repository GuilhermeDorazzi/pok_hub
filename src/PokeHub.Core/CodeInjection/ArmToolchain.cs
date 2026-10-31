using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PokeHub.Core.Decomp;
using PokeHub.Core.Localization;
using PokeHub.Core.Processes;

namespace PokeHub.Core.CodeInjection;

/// <summary>Resultado de compilar um patch: a imagem para gravar na memória e os símbolos que ela define.</summary>
public sealed record BuiltPatch(uint Address, byte[] Image, IReadOnlyDictionary<string, uint> Symbols);

/// <summary>
/// arm-none-eabi-gcc do MSYS2 (o mesmo pacote que a toolchain das decomps instala). Roda o executável nativo direto, sem shell,
/// porque só precisa do próprio diretório no PATH. Sem biblioteca C: o código novo só chama as funções do jogo.
/// </summary>
public sealed partial class ArmToolchain(string? binDir)
{
    private static readonly string[] CpuFlags = ["-mthumb", "-mthumb-interwork", "-mcpu=arm946e-s"];
    private static readonly string[] SourceExtensions = [".c", ".s", ".S"];

    private string Exe(string tool) => binDir is null ? $"arm-none-eabi-{tool}" : Path.Combine(binDir, $"arm-none-eabi-{tool}.exe");

    /// <summary>Procura no MSYS2 do hub e nas pastas usuais, depois no PATH. Devolve null se não houver.</summary>
    public static async Task<ArmToolchain?> FindAsync(string hubRuntimesDir)
    {
        foreach (var shell in (await UnixShell.DetectAsync(hubRuntimesDir)).Where(s => s.Kind == UnixShellKind.Msys2))
            foreach (var env in new[] { "ucrt64", "mingw64" })
            {
                var bin = Path.Combine(shell.Location, env, "bin");
                if (File.Exists(Path.Combine(bin, "arm-none-eabi-gcc.exe"))) return new ArmToolchain(bin);
            }
        return ProcessRunner.IsOnPath("arm-none-eabi-gcc") ? new ArmToolchain(null) : null;
    }

    public static IReadOnlyList<string> SourcesOf(string patchDir) =>
        Directory.EnumerateFiles(patchDir, "*", SearchOption.AllDirectories)
            .Where(f => SourceExtensions.Contains(Path.GetExtension(f), StringComparer.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

    /// <summary>Compila e liga as fontes de um patch em <paramref name="address"/>. As funções do jogo entram como símbolos no script do ligador.</summary>
    public async Task<BuiltPatch> BuildAsync(CodePatch patch, uint address, string buildDir, IProgress<string> log, CancellationToken ct = default)
    {
        var sources = SourcesOf(patch.Directory);
        if (sources.Count == 0) return new BuiltPatch(address, [], new Dictionary<string, uint>());

        if (Directory.Exists(buildDir)) Directory.Delete(buildDir, recursive: true);
        Directory.CreateDirectory(buildDir);

        var env = binDir is null ? null : new Dictionary<string, string> { ["PATH"] = binDir + ";" + Environment.GetEnvironmentVariable("PATH") };
        async Task Run(string tool, IEnumerable<string> args)
        {
            var code = await ProcessRunner.RunAsync(Exe(tool), args, patch.Directory, log, ct, env);
            if (code != 0) throw new InvalidOperationException(Loc.T("core.inject.compileFailed", patch.Name, tool, code));
        }

        var objects = new List<string>();
        for (int i = 0; i < sources.Count; i++)
        {
            var obj = Path.Combine(buildDir, $"{i:D3}_{Path.GetFileNameWithoutExtension(sources[i])}.o");
            await Run("gcc", [.. CpuFlags, "-Os", "-ffreestanding", "-fno-common", "-ffunction-sections", "-fdata-sections", "-Wall", "-I", patch.Directory, "-c", sources[i], "-o", obj]);
            objects.Add(obj);
        }

        if (patch.Manifest.Symbols.Count > 0)
        {
            var symbolsSource = Path.Combine(buildDir, "game_symbols.s");
            File.WriteAllText(symbolsSource, GameSymbolsAssembly(patch.Manifest.Symbols));
            var symbolsObject = Path.Combine(buildDir, "game_symbols.o");
            await Run("gcc", [.. CpuFlags, "-c", symbolsSource, "-o", symbolsObject]);
            objects.Add(symbolsObject);
        }

        var script = Path.Combine(buildDir, "patch.ld");
        File.WriteAllText(script, LinkerScript(address));
        var elf = Path.Combine(buildDir, "patch.elf");
        // as funções dos ganchos são as raízes: o resto do código só fica se for alcançável a partir delas
        var roots = patch.Manifest.Hooks.Select(h => h.Symbol).Where(s => s is not null && !CodePatch.IsNumber(s) && !patch.Manifest.Symbols.ContainsKey(s)).Distinct().Select(s => "-Wl,-u," + s);
        await Run("gcc", [.. CpuFlags, "-nostdlib", "-T", script, "-Wl,--gc-sections", "-Wl,--no-warn-rwx-segments", "-Wl,-Map=" + Path.Combine(buildDir, "patch.map"), .. roots, .. objects, "-lgcc", "-o", elf]);

        var (code, output) = await ProcessRunner.CaptureAsync(Exe("readelf"), ["-sW", elf]);
        if (code != 0) throw new InvalidOperationException(Loc.T("core.inject.compileFailed", patch.Name, "readelf", code));
        var symbols = ParseSymbols(output);
        // nenhum gancho alcança código (modelo recém-ativado, símbolo que não existe): o objcopy falharia num arquivo sem seções.
        // Quem chama decide: sem ganchos, é um patch vazio; com símbolo inexistente, o erro diz qual é.
        if (symbols["__patch_end"] == address) return new BuiltPatch(address, [], symbols);

        var bin = Path.Combine(buildDir, "patch.bin");
        await Run("objcopy", ["-O", "binary", elf, bin]);
        // o .bss não vai para o arquivo do objcopy, mas precisa de espaço reservado (e zerado) na imagem
        var loaded = File.ReadAllBytes(bin);
        var image = new byte[Math.Max(symbols["__patch_end"] - address, (uint)loaded.Length)];
        loaded.CopyTo(image, 0);
        return new BuiltPatch(address, image, symbols);
    }

    /// <summary>
    /// Símbolos do jogo como funções absolutas. Definidos no script do ligador eles não têm tipo, e o ligador os trata como ARM mesmo com
    /// endereço ímpar: cria um veneer que salta em modo ARM para código Thumb e o jogo trava. Com o tipo função, o bit 0 vale.
    /// </summary>
    public static string GameSymbolsAssembly(IReadOnlyDictionary<string, string> gameSymbols)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in gameSymbols.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $".global {name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $".type {name}, %function");
            sb.AppendLine(CultureInfo.InvariantCulture, $".set {name}, 0x{CodePatch.ParseNumber(value):X8}");
        }
        return sb.ToString();
    }

    public static string LinkerScript(uint address)
    {
        return string.Create(CultureInfo.InvariantCulture, $$"""
            SECTIONS
            {
              . = 0x{{address:X8}};
              .text : { *(.text .text.*) *(.glue_7 .glue_7t .vfp11_veneer .v4_bx) }
              .rodata : { *(.rodata .rodata.*) }
              .data : { *(.data .data.*) }
              .bss : { *(.bss .bss.*) }
              . = ALIGN(4);
              __patch_end = .;
              /DISCARD/ : { *(.ARM.exidx*) *(.ARM.extab*) *(.comment) *(.note*) *(.ARM.attributes) }
            }

            """);
    }

    /// <summary>Lê a saída do readelf -sW. O valor vem cru, então funções Thumb mantêm o bit 0 ligado (o nm o esconde).</summary>
    public static Dictionary<string, uint> ParseSymbols(string readelfOutput)
    {
        var result = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (Match m in SymbolLine().Matches(readelfOutput))
            result[m.Groups["name"].Value] = uint.Parse(m.Groups["value"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return result;
    }

    [GeneratedRegex(@"^\s*\d+:\s+(?<value>[0-9a-fA-F]{8})\s+\d+\s+\w+\s+\w+\s+\w+\s+(?!UND)\S+\s+(?<name>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex SymbolLine();
}
