using PokeHub.Core.Localization;
using PokeHub.Core.Rom;
using PokeHub.Core.Runtimes;

namespace PokeHub.Core.Projects;

public enum WorkspaceFormat { None, DsRom, Ndstool }

/// <summary>
/// Extrai base.nds para workspace/ e empacota workspace/ em builds/.
/// Formato preferido: ds-rom (nativo do DSPRE 2.x, via runtime gerenciado). Reserva: layout ndstool em C# puro.
/// </summary>
public sealed class WorkspaceService(RuntimeManager runtimes)
{
    public static WorkspaceFormat FormatOf(Project p) =>
        DsRomTool.IsDsRomWorkspace(p.Paths.Workspace) ? WorkspaceFormat.DsRom
        : new WorkspaceLayout(p.Paths.Workspace).IsExtracted ? WorkspaceFormat.Ndstool
        : WorkspaceFormat.None;

    public static bool IsExtracted(Project p) => FormatOf(p) != WorkspaceFormat.None;

    public static string FormatLabel(Project p) => FormatOf(p) switch
    {
        WorkspaceFormat.DsRom => Loc.T("ws.dsrom"),
        WorkspaceFormat.Ndstool => Loc.T("ws.ndstool"),
        _ => Loc.T("ws.empty"),
    };

    /// <summary>Devolve true ao concluir (para o chamador distinguir de uma falha tratada).</summary>
    public async Task<bool> ExtractAsync(Project p, IProgress<string> log, bool overwrite = false, CancellationToken ct = default)
    {
        if (IsExtracted(p) && !overwrite)
            throw new InvalidOperationException(Loc.T("core.ws.alreadyExtracted"));

        var dsrom = await EnsureDsRomAsync(log, ct);
        if (dsrom is not null)
        {
            log.Report(Loc.T("core.ws.extractingDsrom"));
            var meta = await DsRomTool.ExtractAsync(dsrom, p.Paths.BaseRom, p.Paths.Workspace, log, ct);
            log.Report(Loc.T("core.ws.extractedDsrom", p.Paths.Workspace, meta.GameCode));
        }
        else
        {
            log.Report(Loc.T("core.ws.dsromUnavailable"));
            await Task.Run(() => NdsExtractor.Extract(p.Paths.BaseRom, p.Paths.Workspace, log, overwrite: true), ct);
            log.Report(Loc.T("core.ws.extractedNdstool", p.Paths.Workspace));
        }
        return true;
    }

    /// <summary>Gera builds/&lt;nome&gt;_yyyyMMdd-HHmmss.nds e devolve o caminho.</summary>
    public async Task<string> PackAsync(Project p, IProgress<string> log, CancellationToken ct = default)
    {
        Directory.CreateDirectory(p.Paths.Builds);
        var output = Path.Combine(p.Paths.Builds, $"{p.Name}_{DateTime.Now:yyyyMMdd-HHmmss}.nds");
        switch (FormatOf(p))
        {
            case WorkspaceFormat.DsRom:
                var dsrom = await EnsureDsRomAsync(log, ct) ?? throw new InvalidOperationException(Loc.T("core.ws.dsromInstallFailed"));
                await DsRomTool.BuildAsync(dsrom, p.Paths.Workspace, output, log, ct);
                log.Report(Loc.T("core.ws.romGenerated", output, new FileInfo(output).Length / 1024 / 1024));
                break;
            case WorkspaceFormat.Ndstool:
                await Task.Run(() => NdsPacker.Pack(p.Paths.Workspace, output, log), ct);
                break;
            default:
                throw new InvalidOperationException(Loc.T("core.ws.notExtracted"));
        }
        CarrySave(output, log);
        return output;
    }

    /// <summary>
    /// Cada ROM tem nome próprio, e o emulador procura o save com o nome da ROM: sem isto, o jogo abriria como novo a cada empacotamento.
    /// Copia o save mais recente da pasta, sem substituir nada. Se não der para copiar, a ROM vale do mesmo jeito e o log avisa.
    /// </summary>
    public static void CarrySave(string rom, IProgress<string> log)
    {
        var folder = Path.GetDirectoryName(rom)!;
        var target = Path.ChangeExtension(rom, ".sav");
        if (File.Exists(target)) return;
        var latest = new DirectoryInfo(folder).GetFiles("*.sav").Where(f => f.Length > 0).OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).FirstOrDefault();
        if (latest is null) return;
        try
        {
            File.Copy(latest.FullName, target);
            log.Report(Loc.T("core.ws.saveCarried", latest.Name));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Report(Loc.T("core.ws.saveCarryFailed", latest.Name, ex.Message));
        }
    }

    private async Task<string?> EnsureDsRomAsync(IProgress<string> log, CancellationToken ct)
    {
        if (runtimes.ResolveDsRom() is { } exe) return exe;
        try
        {
            return (await runtimes.InstallAsync(RuntimeManager.DsRomId, log, ct)).ExecutablePath;
        }
        catch (Exception ex)
        {
            log.Report(Loc.T("core.ws.dsromInstallError", ex.Message));
            return null;
        }
    }
}
