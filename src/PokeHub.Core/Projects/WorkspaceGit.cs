using PokeHub.Core.Localization;
using PokeHub.Core.Processes;
using PokeHub.Core.Runtimes;

namespace PokeHub.Core.Projects;

/// <summary>
/// Versionamento automático de workspace/ com git: um repositório por projeto, commits a cada marco
/// (extração, antes de empacotar, ao fechar uma ferramenta, ou quando o usuário pede).
/// Se a pasta do projeto já é um repositório, as versões entram nele, e os comandos ficam restritos a workspace/.
/// </summary>
public sealed class WorkspaceGit(RuntimeManager runtimes)
{
    private static readonly string[] Identity = ["-c", "user.name=PokHub", "-c", "user.email=pokehub@localhost", "-c", "core.autocrlf=false"];

    public bool IsAvailable => runtimes.ResolveGit() is not null;

    /// <summary>A pasta do projeto é o próprio repositório, como num projeto publicado no GitHub.</summary>
    public static bool UsesProjectRepo(Project p) => Directory.Exists(Path.Combine(p.Paths.Dir, ".git")) || File.Exists(Path.Combine(p.Paths.Dir, ".git"));

    public static bool IsRepo(Project p) => UsesProjectRepo(p) || Directory.Exists(Path.Combine(p.Paths.Workspace, ".git"));

    /// <summary>Cria o repositório em workspace/ se ainda não existir e faz o commit inicial.</summary>
    public async Task EnsureRepoAsync(Project p, IProgress<string> log, CancellationToken ct = default)
    {
        var git = RequireGit();
        if (!Directory.Exists(p.Paths.Workspace)) return;
        if (!IsRepo(p))
        {
            await Run(git, ["init", "-q", "-b", "main"], p, log, ct);
            log.Report(Loc.T("core.git.repoCreated"));
        }
        var attributes = Path.Combine(p.Paths.Workspace, ".gitattributes");
        if (!File.Exists(attributes)) await File.WriteAllTextAsync(attributes, "* binary\n", ct);
        await SnapshotAsync(p, Loc.T("core.git.initialSnapshot"), log, ct);
    }

    /// <summary>Commita tudo o que mudou. Devolve false se não havia mudanças.</summary>
    public async Task<bool> SnapshotAsync(Project p, string message, IProgress<string> log, CancellationToken ct = default)
    {
        var git = RequireGit();
        if (!IsRepo(p)) return false;
        await Run(git, ["add", "-A", "--", "."], p, null, ct);
        var (code, _) = await ProcessRunner.CaptureAsync(git, [.. Identity, "-C", p.Paths.Workspace, "diff", "--cached", "--quiet"], timeoutMs: 120000);
        if (code == 0) { log.Report(Loc.T("core.git.noChanges")); return false; }
        var rc = await Run(git, ["commit", "-q", "-m", $"{message} ({DateTime.Now:yyyy-MM-dd HH:mm})"], p, null, ct);
        if (rc != 0) throw new InvalidOperationException(Loc.T("core.git.commitFailed"));
        log.Report(Loc.T("core.git.saved", message));
        return true;
    }

    /// <summary>Últimas versões, mais recente primeiro: (hash curto, data, mensagem).</summary>
    public async Task<IReadOnlyList<(string Hash, string Message)>> HistoryAsync(Project p, int count = 20)
    {
        var git = RequireGit();
        if (!IsRepo(p)) return [];
        var (code, output) = await ProcessRunner.CaptureAsync(git, ["-C", p.Paths.Workspace, "log", $"-{count}", "--pretty=format:%h\t%s", "--", "."], timeoutMs: 30000);
        if (code != 0) return [];
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('\t', 2))
            .Where(a => a.Length == 2)
            .Select(a => (a[0], a[1]))
            .ToList();
    }

    /// <summary>Volta workspace/ para uma versão, guardando antes o estado atual como commit.</summary>
    public async Task RestoreAsync(Project p, string hash, IProgress<string> log, CancellationToken ct = default)
    {
        var git = RequireGit();
        await SnapshotAsync(p, Loc.T("core.git.beforeRestore", hash), log, ct);
        var rc = await Run(git, ["restore", "--source", hash, "--worktree", "--staged", "--", "."], p, log, ct);
        if (rc != 0) throw new InvalidOperationException(Loc.T("core.git.restoreFailed"));
        await Run(git, ["clean", "-fdq"], p, null, ct);
        await SnapshotAsync(p, Loc.T("core.git.restoredFrom", hash), log, ct);
    }

    /// <summary>Arquivos alterados entre uma versão e o workspace atual (caminhos relativos ao workspace).</summary>
    public async Task<IReadOnlyList<string>> ChangedFilesAsync(Project p, string hash)
    {
        var git = RequireGit();
        var (code, output) = await ProcessRunner.CaptureAsync(git, ["-C", p.Paths.Workspace, "diff", "--name-only", "--relative", hash, "--", "."], timeoutMs: 60000);
        if (code != 0) return [];
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>Lê um arquivo do workspace como estava numa versão; null se não existia.</summary>
    public async Task<byte[]?> ReadFileAtAsync(Project p, string hash, string relativePath)
    {
        var git = RequireGit();
        var (code, bytes) = await ProcessRunner.CaptureBytesAsync(git, ["-C", p.Paths.Workspace, "show", $"{hash}:./{relativePath.Replace('\\', '/')}"]);
        return code == 0 ? bytes : null;
    }

    private string RequireGit() => runtimes.ResolveGit()
        ?? throw new InvalidOperationException(Loc.T("core.git.missing"));

    private static Task<int> Run(string git, string[] args, Project p, IProgress<string>? log, CancellationToken ct) =>
        ProcessRunner.RunAsync(git, [.. Identity, "-C", p.Paths.Workspace, .. args], p.Paths.Workspace, log, ct);
}
