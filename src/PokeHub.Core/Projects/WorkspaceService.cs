using PokeHub.Core.Rom;

namespace PokeHub.Core.Projects;

/// <summary>Extrai base.nds para workspace/ e empacota workspace/ em builds/.</summary>
public static class WorkspaceService
{
    public static bool IsExtracted(Project p) => new WorkspaceLayout(p.Paths.Workspace).IsExtracted;

    public static Task<WorkspaceManifest> ExtractAsync(Project p, IProgress<string> log, bool overwrite = false, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var m = NdsExtractor.Extract(p.Paths.BaseRom, p.Paths.Workspace, log, overwrite);
            log.Report($"Extração concluída em {p.Paths.Workspace}");
            return m;
        }, ct);

    /// <summary>Gera builds/&lt;nome&gt;_yyyyMMdd-HHmmss.nds e devolve o caminho.</summary>
    public static Task<string> PackAsync(Project p, IProgress<string> log, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(p.Paths.Builds);
            var output = Path.Combine(p.Paths.Builds, $"{p.Name}_{DateTime.Now:yyyyMMdd-HHmmss}.nds");
            NdsPacker.Pack(p.Paths.Workspace, output, log);
            return output;
        }, ct);
}
