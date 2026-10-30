using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Decomp;

/// <summary>Roda o build de uma decomp clonada no projeto e copia a ROM resultante para builds/.</summary>
public sealed class DecompBuildService(HubConfig config, HubPaths hub)
{
    private IReadOnlyList<UnixShell>? _shells;

    /// <summary>Resultado da última detecção, sem disparar uma nova (null antes da primeira).</summary>
    public IReadOnlyList<UnixShell>? CachedShells => _shells;

    public async Task<IReadOnlyList<UnixShell>> GetShellsAsync(bool refresh = false)
    {
        if (_shells is null || refresh) _shells = await UnixShell.DetectAsync();
        return _shells;
    }

    /// <summary>Shell escolhido: o preferido em hub.json se ainda existir, senão o primeiro detectado.</summary>
    public async Task<UnixShell?> ResolveShellAsync()
    {
        var shells = await GetShellsAsync();
        if (config.PreferredShell is { } pref)
        {
            var match = shells.FirstOrDefault(s => s.Kind.ToString().Equals(pref.Kind, StringComparison.OrdinalIgnoreCase)
                                                && s.Location.Equals(pref.Location, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return shells.FirstOrDefault();
    }

    public void SetPreferredShell(UnixShell shell)
    {
        config.PreferredShell = new PreferredShell { Kind = shell.Kind.ToString(), Location = shell.Location };
        config.Save(hub.ConfigFile);
    }

    public async Task<string> BuildAsync(PluginManifest m, InstalledTool installed, Project project, IProgress<string> log, CancellationToken ct = default)
    {
        var build = m.Build ?? throw new InvalidOperationException($"{m.Name} não declara como compilar.");
        var repo = installed.ExecutablePath;
        if (!Directory.Exists(repo)) throw new DirectoryNotFoundException($"Repositório não encontrado: {repo}");

        var shell = await ResolveShellAsync()
            ?? throw new InvalidOperationException("Nenhum shell Unix encontrado. Instale o MSYS2 (https://www.msys2.org) ou o WSL2 e as dependências listadas no INSTALL.md da decomp.");
        log.Report($"Usando {shell.Description}");
        if (shell.Kind == UnixShellKind.Wsl)
            log.Report("Aviso: o repositório fica no disco do Windows (/mnt). Pelo WSL o build é bem mais lento; MSYS2 é mais rápido aqui.");

        var (file, args, env) = shell.Compose(build.Command, repo);
        var started = DateTime.Now;
        var code = await ProcessRunner.RunAsync(file, args, repo, log, ct, env);
        if (code != 0) throw new InvalidOperationException($"Build falhou (código {code}). Veja o log; na primeira vez confira as dependências do INSTALL.md.");

        var output = Path.Combine(repo, build.Output.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(output)) throw new FileNotFoundException("Build terminou mas a ROM não apareceu onde o manifesto indica.", output);

        Directory.CreateDirectory(project.Paths.Builds);
        var dest = Path.Combine(project.Paths.Builds, $"{project.Name}_{m.Id}_{DateTime.Now:yyyyMMdd-HHmmss}.nds");
        File.Copy(output, dest, overwrite: true);
        log.Report($"Build concluído em {(DateTime.Now - started).TotalMinutes:F1} min: {dest}");
        return dest;
    }
}
