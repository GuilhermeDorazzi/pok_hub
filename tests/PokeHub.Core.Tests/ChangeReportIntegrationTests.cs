using PokeHub.Core.Hub;
using PokeHub.Core.Patch;
using PokeHub.Core.Plugins;
using PokeHub.Core.Projects;
using PokeHub.Core.Reports;
using Xunit;
using Xunit.Abstractions;

namespace PokeHub.Core.Tests;

public class ChangeReportIntegrationTests(ITestOutputHelper output)
{
    /// <summary>Com POKEHUB_HUB_ROOT e POKEHUB_REPORT_PROJECT=&lt;nome&gt;: compara a primeira versão do workspace com o atual e imprime o relatório.</summary>
    [Fact]
    public async Task Reports_changes_of_real_project_in_game_terms()
    {
        var root = Environment.GetEnvironmentVariable("POKEHUB_HUB_ROOT");
        var name = Environment.GetEnvironmentVariable("POKEHUB_REPORT_PROJECT");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(name)) return;

        var paths = new HubPaths(root);
        var config = HubConfig.Load(paths.ConfigFile);
        var installer = new PluginInstaller(paths, config);
        var git = new WorkspaceGit(installer.Runtimes);
        var project = new ProjectService(paths).Open(name)!;
        var history = await git.HistoryAsync(project, 100);
        Assert.NotEmpty(history);
        var first = history[^1].Hash;

        var changed = await git.ChangedFilesAsync(project, first);
        output.WriteLine($"arquivos alterados desde {first}: {changed.Count}");
        var temp = Path.Combine(Path.GetTempPath(), "pokehub-compare-test", Guid.NewGuid().ToString("N"));
        try
        {
            var old = await ChangeReport.ExportVersionAsync(git, project, first, temp);
            var lines = ChangeReport.Compare(old, project, changed);
            foreach (var l in lines) output.WriteLine($"[{l.Category}] {l.Text}");
            Assert.True(lines.Count > 0 || changed.Count == 0);
            Assert.DoesNotContain(lines, l => l.Text.Contains("não foi possível") || l.Text.Contains("could not"));
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    /// <summary>Com POKEHUB_HUB_ROOT e POKEHUB_REPORT_PROJECT: patch BPS entre a ROM base e a última build em builds/, aplicado de volta.</summary>
    [Fact]
    public async Task Bps_roundtrip_on_real_rom()
    {
        var root = Environment.GetEnvironmentVariable("POKEHUB_HUB_ROOT");
        var name = Environment.GetEnvironmentVariable("POKEHUB_REPORT_PROJECT");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(name)) return;
        var project = new ProjectService(new HubPaths(root)).Open(name)!;
        var build = Directory.EnumerateFiles(project.Paths.Builds, "*.nds").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (build is null) return;

        var source = await File.ReadAllBytesAsync(project.Paths.BaseRom);
        var target = await File.ReadAllBytesAsync(build);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var patch = Bps.Create(source, target, "teste");
        output.WriteLine($"patch: {patch.Length / 1024} KB em {sw.Elapsed.TotalSeconds:F1}s (ROM {target.Length / 1024 / 1024} MB)");
        sw.Restart();
        var applied = Bps.Apply(source, patch);
        output.WriteLine($"aplicado em {sw.Elapsed.TotalSeconds:F1}s");
        Assert.Equal(target.Length, applied.Length);
        Assert.True(target.AsSpan().SequenceEqual(applied));
    }
}
