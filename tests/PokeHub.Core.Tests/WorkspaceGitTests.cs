using System.Text;
using PokeHub.Core.Hub;
using PokeHub.Core.Projects;
using PokeHub.Core.Runtimes;
using Xunit;

namespace PokeHub.Core.Tests;

public class WorkspaceGitTests
{
    [Fact]
    public void Git_runtime_spec_targets_mingit_64()
    {
        var spec = RuntimeManager.Known[RuntimeManager.GitId];
        Assert.Equal(RuntimeSource.GitHubRelease, spec.Source);
        Assert.Matches(spec.AssetPattern!, "MinGit-2.56.0.4-64-bit.zip");
        Assert.DoesNotMatch(spec.AssetPattern!, "MinGit-2.56.0.4-busybox-64-bit.zip");
        Assert.DoesNotMatch(spec.AssetPattern!, "MinGit-2.56.0.4-32-bit.zip");
    }

    /// <summary>Usa o git do sistema (presente na máquina de desenvolvimento) para versionar um workspace de mentira.</summary>
    [Fact]
    public async Task Snapshot_history_and_restore_in_workspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pokehub-git-{Guid.NewGuid():N}");
        try
        {
            var paths = new HubPaths(root);
            foreach (var d in paths.AllDirectories()) Directory.CreateDirectory(d);
            var runtimes = new RuntimeManager(paths, new HubConfig());
            if (runtimes.ResolveGit() is null) return; // sem git na máquina: nada a testar

            var p = new Project
            {
                Paths = new ProjectPaths(paths.ProjectDir("hack")),
                Config = new ProjectConfig { Name = "hack" },
            };
            Directory.CreateDirectory(p.Paths.Workspace);
            var file = Path.Combine(p.Paths.Workspace, "arm9.bin");
            await File.WriteAllBytesAsync(file, Encoding.ASCII.GetBytes("v1"));

            var git = new WorkspaceGit(runtimes);
            var log = new List<string>();
            var progress = new Progress<string>(log.Add);

            await git.EnsureRepoAsync(p, progress);
            Assert.True(WorkspaceGit.IsRepo(p));
            Assert.False(await git.SnapshotAsync(p, "nada mudou", progress));

            await File.WriteAllBytesAsync(file, Encoding.ASCII.GetBytes("v2"));
            Assert.True(await git.SnapshotAsync(p, "Após fechar dspre", progress));

            var history = await git.HistoryAsync(p);
            Assert.Equal(2, history.Count);
            Assert.StartsWith("Após fechar dspre", history[0].Message);

            await git.RestoreAsync(p, history[1].Hash, progress);
            Assert.Equal("v1", await File.ReadAllTextAsync(file));
            Assert.True((await git.HistoryAsync(p)).Count >= 3);
        }
        finally { DeleteGitDir(root); }
    }

    /// <summary>Objetos do git são somente leitura; Directory.Delete recusa removê-los sem limpar o atributo.</summary>
    private static void DeleteGitDir(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }
}
