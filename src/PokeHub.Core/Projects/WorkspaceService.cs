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
        WorkspaceFormat.DsRom => "workspace ds-rom",
        WorkspaceFormat.Ndstool => "workspace ndstool (legado)",
        _ => "workspace vazio",
    };

    /// <summary>Devolve true ao concluir (para o chamador distinguir de uma falha tratada).</summary>
    public async Task<bool> ExtractAsync(Project p, IProgress<string> log, bool overwrite = false, CancellationToken ct = default)
    {
        if (IsExtracted(p) && !overwrite)
            throw new InvalidOperationException("workspace/ já contém uma ROM extraída. Use sobrescrever para extrair de novo.");

        var dsrom = await EnsureDsRomAsync(log, ct);
        if (dsrom is not null)
        {
            log.Report("Extraindo com ds-rom (formato nativo do DSPRE)...");
            var meta = await DsRomTool.ExtractAsync(dsrom, p.Paths.BaseRom, p.Paths.Workspace, log, ct);
            log.Report($"Extração concluída em {p.Paths.Workspace} ({meta.GameCode}, ds-rom)");
        }
        else
        {
            log.Report("ds-rom indisponível: extraindo no layout ndstool (o DSPRE vai oferecer converter).");
            await Task.Run(() => NdsExtractor.Extract(p.Paths.BaseRom, p.Paths.Workspace, log, overwrite: true), ct);
            log.Report($"Extração concluída em {p.Paths.Workspace} (ndstool)");
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
                var dsrom = await EnsureDsRomAsync(log, ct) ?? throw new InvalidOperationException("ds-rom não instalado e não foi possível baixar.");
                await DsRomTool.BuildAsync(dsrom, p.Paths.Workspace, output, log, ct);
                log.Report($"ROM gerada: {output} ({new FileInfo(output).Length / 1024 / 1024} MB)");
                break;
            case WorkspaceFormat.Ndstool:
                await Task.Run(() => NdsPacker.Pack(p.Paths.Workspace, output, log), ct);
                break;
            default:
                throw new InvalidOperationException("workspace/ não contém uma ROM extraída.");
        }
        return output;
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
            log.Report($"Não foi possível instalar o ds-rom: {ex.Message}");
            return null;
        }
    }
}
