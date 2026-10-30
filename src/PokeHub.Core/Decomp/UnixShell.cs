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
                ["-lc", $"cd \"$(cygpath -u '{dir}')\" && {command}"],
                new Dictionary<string, string> { ["MSYSTEM"] = "MSYS", ["CHERE_INVOKING"] = "1", ["MSYS2_PATH_TYPE"] = "inherit" }),
            UnixShellKind.Wsl => (
                "wsl.exe",
                ["-d", Location, "--cd", workingDir, "-e", "bash", "-lc", command],
                new Dictionary<string, string>()),
            _ => throw new NotSupportedException(Kind.ToString()),
        };
    }

    /// <summary>Procura MSYS2 nas pastas usuais e distros do WSL. MSYS2 vem primeiro: compila no sistema de arquivos do Windows sem a lentidão do /mnt.</summary>
    public static async Task<IReadOnlyList<UnixShell>> DetectAsync()
    {
        var found = new List<UnixShell>();

        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("MSYS2_ROOT"),
            @"C:\msys64",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "msys64"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "msys64"),
            @"C:\tools\msys64", // chocolatey
        };
        foreach (var root in candidates.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(Path.Combine(root!, "usr", "bin", "bash.exe")))
                found.Add(new UnixShell(UnixShellKind.Msys2, root!, $"MSYS2 em {root}"));

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
