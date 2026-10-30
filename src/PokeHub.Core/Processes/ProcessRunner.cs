using System.Diagnostics;

namespace PokeHub.Core.Processes;

/// <summary>Executa um comando até o fim, repassando stdout/stderr linha a linha.</summary>
public static class ProcessRunner
{
    public static async Task<int> RunAsync(string fileName, IEnumerable<string> args, string? workingDir,
        IProgress<string>? log, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) log?.Report(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) log?.Report(e.Data); };
        log?.Report($"> {fileName} {string.Join(' ', args)}");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    public static bool IsOnPath(string exe)
    {
        try
        {
            var psi = new ProcessStartInfo("where", exe) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using var p = Process.Start(psi)!;
            p.WaitForExit(3000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
