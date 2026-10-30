using PokeHub.Core.Processes;
using PokeHub.Core.Runtimes;

namespace PokeHub.Core.Projects;

/// <summary>
/// Versionamento automático de workspace/ com git: um repositório por projeto, commits a cada marco
/// (extração, antes de empacotar, ao fechar uma ferramenta, ou quando o usuário pede).
/// </summary>
public sealed class WorkspaceGit(RuntimeManager runtimes)
{
    private static readonly string[] Identity = ["-c", "user.name=PokeHub", "-c", "user.email=pokehub@localhost", "-c", "core.autocrlf=false"];

    public bool IsAvailable => runtimes.ResolveGit() is not null;

    public static bool IsRepo(Project p) => Directory.Exists(Path.Combine(p.Paths.Workspace, ".git"));

    /// <summary>Cria o repositório em workspace/ se ainda não existir e faz o commit inicial.</summary>
    public async Task EnsureRepoAsync(Project p, IProgress<string> log, CancellationToken ct = default)
    {
        var git = RequireGit();
        if (!Directory.Exists(p.Paths.Workspace)) return;
        if (!IsRepo(p))
        {
            await Run(git, ["init", "-q", "-b", "main"], p, log, ct);
            await File.WriteAllTextAsync(Path.Combine(p.Paths.Workspace, ".gitattributes"), "* binary\n", ct);
            log.Report("Repositório git criado em workspace/");
        }
        await SnapshotAsync(p, "Extração da ROM base", log, ct);
    }

    /// <summary>Commita tudo o que mudou. Devolve false se não havia mudanças.</summary>
    public async Task<bool> SnapshotAsync(Project p, string message, IProgress<string> log, CancellationToken ct = default)
    {
        var git = RequireGit();
        if (!IsRepo(p)) return false;
        await Run(git, ["add", "-A"], p, null, ct);
        var (code, _) = await ProcessRunner.CaptureAsync(git, [.. Identity, "-C", p.Paths.Workspace, "diff", "--cached", "--quiet"], timeoutMs: 120000);
        if (code == 0) { log.Report("Nenhuma alteração no workspace desde a última versão."); return false; }
        var rc = await Run(git, ["commit", "-q", "-m", $"{message} ({DateTime.Now:yyyy-MM-dd HH:mm})"], p, null, ct);
        if (rc != 0) throw new InvalidOperationException("git commit falhou. Veja o log.");
        log.Report($"Versão salva: {message}");
        return true;
    }

    /// <summary>Últimas versões, mais recente primeiro: (hash curto, data, mensagem).</summary>
    public async Task<IReadOnlyList<(string Hash, string Message)>> HistoryAsync(Project p, int count = 20)
    {
        var git = RequireGit();
        if (!IsRepo(p)) return [];
        var (code, output) = await ProcessRunner.CaptureAsync(git, ["-C", p.Paths.Workspace, "log", $"-{count}", "--pretty=format:%h\t%s"], timeoutMs: 30000);
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
        await SnapshotAsync(p, "Antes de restaurar " + hash, log, ct);
        var rc = await Run(git, ["restore", "--source", hash, "--worktree", "--staged", "--", "."], p, log, ct);
        if (rc != 0) throw new InvalidOperationException("git restore falhou.");
        await Run(git, ["clean", "-fdq"], p, null, ct);
        await SnapshotAsync(p, "Restaurado de " + hash, log, ct);
    }

    private string RequireGit() => runtimes.ResolveGit()
        ?? throw new InvalidOperationException("Git não encontrado. Instale pelo hub (Instalar Git) ou pelo Git for Windows.");

    private static Task<int> Run(string git, string[] args, Project p, IProgress<string>? log, CancellationToken ct) =>
        ProcessRunner.RunAsync(git, [.. Identity, "-C", p.Paths.Workspace, .. args], p.Paths.Workspace, log, ct);
}
