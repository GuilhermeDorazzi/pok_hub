using System.Diagnostics;
using System.Text;

namespace PokeHub.Core.Processes;

/// <summary>Executa um comando até o fim, repassando stdout/stderr linha a linha.</summary>
public static class ProcessRunner
{
    public static async Task<int> RunAsync(string fileName, IEnumerable<string> args, string? workingDir,
        IProgress<string>? log, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? environment = null, Encoding? outputEncoding = null)
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
        if (environment is not null) foreach (var (k, v) in environment) psi.Environment[k] = v;
        // git, bash e a maioria das CLIs escrevem UTF-8; wsl.exe passa Unicode explicitamente
        psi.StandardOutputEncoding = outputEncoding ?? Encoding.UTF8;
        psi.StandardErrorEncoding = outputEncoding ?? Encoding.UTF8;

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) log?.Report(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) log?.Report(e.Data); };
        log?.Report($"> {fileName} {string.Join(' ', args)}");
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(entireProcessTree: true); } catch { } throw; }
        return p.ExitCode;
    }

    /// <summary>Captura stdout inteiro de um comando curto (sem log).</summary>
    public static async Task<(int ExitCode, string Output)> CaptureAsync(string fileName, IEnumerable<string> args,
        Encoding? outputEncoding = null, int timeoutMs = 10000)
    {
        var psi = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.StandardOutputEncoding = outputEncoding ?? Encoding.UTF8;
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Não foi possível iniciar {fileName}");
        using var cts = new CancellationTokenSource(timeoutMs);
        var output = await p.StandardOutput.ReadToEndAsync(cts.Token);
        await p.WaitForExitAsync(cts.Token);
        return (p.ExitCode, output);
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
