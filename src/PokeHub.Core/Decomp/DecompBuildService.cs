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
        if (_shells is null || refresh) _shells = await UnixShell.DetectAsync(hub.Runtimes);
        return _shells;
    }

    /// <summary>
    /// Deixa um MSYS2 recém-extraído pronto para as decomps: login shell inicial, `pacman -Syuu` duas vezes
    /// (a primeira pode encerrar o shell ao atualizar o core) e os pacotes pedidos.
    /// </summary>
    public async Task PrepareMsys2ToolchainAsync(UnixShell shell, IReadOnlyList<string> packages, IProgress<string> log, CancellationToken ct = default, string? libpngVersion = null)
    {
        if (shell.Kind != UnixShellKind.Msys2) throw new InvalidOperationException("Só o MSYS2 é preparado pelo hub.");
        var home = shell.Location;
        async Task<int> Run(string command, bool tolerate = false, bool firstStart = false)
        {
            var (file, args, env) = shell.Compose(command, home);
            // o /etc/profile só executa /etc/post-install/*.post (devices, home, chaveiro do pacman) com esta variável, como o msys2_shell.cmd faz
            if (firstStart) env["MAYBE_FIRST_START"] = "true";
            var code = await ProcessRunner.RunAsync(file, args, home, log, ct, env);
            if (code != 0 && !tolerate) throw new InvalidOperationException($"Comando no MSYS2 falhou (código {code}): {command}");
            return code;
        }
        log.Report("MSYS2: inicializando o ambiente (primeira execução)...");
        await Run("true", tolerate: true, firstStart: true);
        // o profile só roda os scripts de /etc/post-install em shell interativo; sem eles o chaveiro do pacman não existe
        log.Report("MSYS2: inicializando o chaveiro do pacman...");
        await Run("[ -d /etc/pacman.d/gnupg ] || { export GNUPGHOME=\"$(pacman-conf.exe gpgdir)\"; pacman-key --init && pacman-key --populate msys2; gpgconf --kill all || true; }");
        log.Report("MSYS2: atualizando pacotes base (1/2)...");
        await Run("pacman --noconfirm -Syuu", tolerate: true);
        log.Report("MSYS2: atualizando pacotes (2/2)...");
        await Run("pacman --noconfirm -Syuu");
        if (packages.Count > 0)
        {
            log.Report($"MSYS2: instalando {packages.Count} pacotes da toolchain (pode levar vários minutos)...");
            await Run("pacman --noconfirm -S --needed " + string.Join(' ', packages));
        }
        if (libpngVersion is not null)
        {
            // O repositório msys não tem libpng, e a do mingw64 não é compatível com programas do runtime msys
            // (nitrogfx usa png_init_io com FILE* do msys e cai). Compila-se uma libpng estática em /usr/local.
            log.Report($"MSYS2: compilando libpng {libpngVersion} estática para o runtime msys (só na primeira vez)...");
            await Run(
                $"[ -f /usr/local/lib/libpng16.a ] && echo 'libpng já compilada' || {{ " +
                $"cd /tmp && rm -rf pokehub-libpng && mkdir pokehub-libpng && cd pokehub-libpng && " +
                $"wget -q -O libpng.tar.xz https://download.sourceforge.net/libpng/libpng-{libpngVersion}.tar.xz && " +
                $"tar xf libpng.tar.xz && cd libpng-{libpngVersion} && " +
                $"./configure --prefix=/usr/local --disable-shared --enable-static >configure.log 2>&1 && " +
                $"make -j$(nproc) >make.log 2>&1 && make install >install.log 2>&1 && echo 'libpng instalada em /usr/local'; }}");
            // biblioteca estática: o -lz de Libs.private precisa ir para Libs, senão o link do nitrogfx fica sem o zlib
            await Run("for f in /usr/local/lib/pkgconfig/libpng16.pc /usr/local/lib/pkgconfig/libpng.pc; do [ -f \"$f\" ] && sed -i 's/^Libs: .*/Libs: -L${libdir} -lpng16 -lz -lm/' \"$f\"; done; true");
            await Run("PKG_CONFIG_PATH=/usr/local/lib/pkgconfig pkg-config --modversion --libs libpng");
        }
        await Run("arm-none-eabi-gcc --version | head -1 && make --version | head -1 && ninja --version", tolerate: true);
        log.Report("MSYS2: toolchain pronta.");
        _shells = null; // redetectar
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
