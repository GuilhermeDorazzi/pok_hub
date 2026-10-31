using PokeHub.Core.Localization;
using System.Text;
using PokeHub.Core.Processes;

namespace PokeHub.Core.Decomp;

public enum UnixShellKind { Msys2, Wsl }

/// <summary>Um shell Unix disponível no Windows para rodar o make das decomps.</summary>
public sealed record UnixShell(UnixShellKind Kind, string Location, string Description)
{
    /// <summary>Monta o executável e os argumentos para rodar <paramref name="command"/> dentro de <paramref name="workingDir"/>.</summary>
    public (string FileName, string[] Args, Dictionary<string, string> Env) Compose(string command, string workingDir)
    {
        var dir = workingDir.Replace('\\', '/');
        return Kind switch
        {
            UnixShellKind.Msys2 => (
                Path.Combine(Location, "usr", "bin", "bash.exe"),
                // Shell MSYS: as ferramentas nativas das decomps usam APIs POSIX (fnmatch, mkdir de 2 argumentos) e só compilam com o gcc do msys.
                // A libpng do mingw64 que o INSTALL.md sugere não serve: nitrogfx passa FILE* do msys para ela (png_init_io) e cai com
                // segmentation fault. Por isso a toolchain do hub compila uma libpng estática para o msys em /usr/local (ver DecompBuildService),
                // e PKG_CONFIG_PATH (e a variante FOR_BUILD, que o meson consulta em build cruzado) apontam para lá.
                // PATH e PKG_CONFIG_PATH são exportados no comando porque o /etc/profile do MSYS2 os redefine depois do ambiente.
                ["-lc", $"export PATH=\"$PATH:/ucrt64/bin:/mingw64/bin\" PKG_CONFIG_PATH=\"/usr/local/lib/pkgconfig:$PKG_CONFIG_PATH\"; export PKG_CONFIG_PATH_FOR_BUILD=\"$PKG_CONFIG_PATH\"; cd \"$(cygpath -u '{dir}')\" && {command}"],
                new Dictionary<string, string> { ["MSYSTEM"] = "MSYS", ["CHERE_INVOKING"] = "1", ["MSYS2_PATH_TYPE"] = "inherit" }),
            UnixShellKind.Wsl => (
                "wsl.exe",
                ["-d", Location, "--cd", workingDir, "-e", "bash", "-lc", command],
                new Dictionary<string, string>()),
            _ => throw new NotSupportedException(Kind.ToString()),
        };
    }

    /// <summary>Procura MSYS2 nas pastas usuais e distros do WSL. MSYS2 vem primeiro: compila no sistema de arquivos do Windows sem a lentidão do /mnt.</summary>
    public static async Task<IReadOnlyList<UnixShell>> DetectAsync(string? hubRuntimesDir = null)
    {
        var found = new List<UnixShell>();

        var candidates = new[]
        {
            hubRuntimesDir is null ? null : Path.Combine(hubRuntimesDir, "msys2", "msys64"), // instalado pelo hub: tem prioridade
            Environment.GetEnvironmentVariable("MSYS2_ROOT"),
            @"C:\msys64",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "msys64"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "msys64"),
            @"C:\tools\msys64", // chocolatey
        };
        foreach (var root in candidates.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(Path.Combine(root!, "usr", "bin", "bash.exe")))
                found.Add(new UnixShell(UnixShellKind.Msys2, root!, hubRuntimesDir is not null && root!.StartsWith(hubRuntimesDir, StringComparison.OrdinalIgnoreCase) ? Loc.T("core.shell.hub") : Loc.T("core.shell.msysAt", root)));

        try
        {
            // wsl.exe escreve UTF-16; -q lista só os nomes das distros
            var (code, output) = await ProcessRunner.CaptureAsync("wsl.exe", ["-l", "-q"], Encoding.Unicode);
            if (code == 0)
                foreach (var line in output.Split('\n').Select(l => l.Trim('\r', ' ', '\0')).Where(l => l.Length > 0))
                    found.Add(new UnixShell(UnixShellKind.Wsl, line, $"WSL: {line}"));
        }
        catch { /* sem WSL */ }

        return found;
    }
}
