using PokeHub.Core.Decomp;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Runtimes;
using Xunit;

namespace PokeHub.Core.Tests;

public class Msys2Tests
{
    [Fact]
    public void Msys2_runtime_spec_targets_sfx()
    {
        var spec = RuntimeManager.Known[RuntimeManager.Msys2Id];
        Assert.True(spec.SfxExtract);
        Assert.Matches(spec.AssetPattern!, "msys2-base-x86_64-latest.sfx.exe");
        Assert.DoesNotMatch(spec.AssetPattern!, "msys2-x86_64-latest.exe");
        Assert.DoesNotMatch(spec.AssetPattern!, "msys2-base-x86_64-latest.tar.xz");
    }

    [Fact]
    public void Msys2_manifest_is_a_runtime_with_packages()
    {
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        var m = catalog.Get("msys2")!;
        Assert.Equal(SourceType.Runtime, m.Source.Type);
        Assert.Equal("msys2", m.Source.RuntimeId);
        Assert.Contains("mingw-w64-ucrt-x86_64-arm-none-eabi-gcc", m.Setup!.Pacman);
        Assert.Contains(ToolContext.Decomp, m.Contexts);
    }

    [Fact]
    public void Hub_msys2_is_detected_first_when_present()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pokehub-msys-{Guid.NewGuid():N}");
        try
        {
            var bash = Path.Combine(root, "runtimes", "msys2", "msys64", "usr", "bin", "bash.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(bash)!);
            File.WriteAllBytes(bash, []);
            var shells = UnixShell.DetectAsync(Path.Combine(root, "runtimes")).Result;
            Assert.Equal(UnixShellKind.Msys2, shells[0].Kind);
            Assert.Equal("MSYS2 do hub", shells[0].Description);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Com POKEHUB_MSYS2_HUB_ROOT=&lt;raiz do hub&gt;: baixa, extrai e prepara a toolchain de verdade nessa raiz (demora minutos).</summary>
    [Fact]
    public async Task Installs_msys2_and_prepares_toolchain()
    {
        var root = Environment.GetEnvironmentVariable("POKEHUB_MSYS2_HUB_ROOT");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        var paths = new HubPaths(root);
        foreach (var d in paths.AllDirectories()) Directory.CreateDirectory(d);
        var config = HubConfig.Load(paths.ConfigFile);
        var runtimes = new RuntimeManager(paths, config);
        var builds = new DecompBuildService(config, paths);
        var log = new List<string>();
        var progress = new Progress<string>(l => { log.Add(l); Console.WriteLine(l); });

        var tool = runtimes.GetInstalled(RuntimeManager.Msys2Id) ?? await runtimes.InstallAsync(RuntimeManager.Msys2Id, progress);
        Assert.True(File.Exists(tool.ExecutablePath));

        var shells = await builds.GetShellsAsync(refresh: true);
        var shell = Assert.Single(shells, s => s.Description == "MSYS2 do hub");

        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        var setup = catalog.Get("msys2")!.Setup!;
        await builds.PrepareMsys2ToolchainAsync(shell, setup.Pacman, progress, libpngVersion: setup.LibpngVersion);
        Assert.Contains(log, l => l.Contains("arm-none-eabi-gcc", StringComparison.OrdinalIgnoreCase) && l.Contains("GCC", StringComparison.OrdinalIgnoreCase));
    }
}
