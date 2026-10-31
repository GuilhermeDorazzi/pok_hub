using PokeHub.Core.Decomp;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;
using Xunit;

namespace PokeHub.Core.Tests;

public class DecompBuildIntegrationTests
{
    /// <summary>
    /// Com POKEHUB_MSYS2_HUB_ROOT (raiz com runtimes/msys2 pronto) e POKEHUB_DECOMP_BUILD=&lt;id do manifesto&gt;:
    /// clona a decomp num projeto temporário e roda o build de verdade (demora muitos minutos).
    /// </summary>
    [Fact]
    public async Task Clones_and_builds_a_decomp()
    {
        var root = Environment.GetEnvironmentVariable("POKEHUB_MSYS2_HUB_ROOT");
        var id = Environment.GetEnvironmentVariable("POKEHUB_DECOMP_BUILD");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root) || string.IsNullOrEmpty(id)) return;

        var paths = new HubPaths(root);
        var config = HubConfig.Load(paths.ConfigFile);
        var installer = new PluginInstaller(paths, config);
        var builds = new DecompBuildService(config, paths);
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        var manifest = catalog.Get(id)!;

        var projectDir = Path.Combine(Path.GetTempPath(), "pokehub-decomp-build");
        Directory.CreateDirectory(projectDir);
        var project = new Project
        {
            Paths = new ProjectPaths(projectDir),
            Config = new ProjectConfig { Name = "decomp-build", BaseGame = BaseGame.Platinum, Generation = 4 },
        };
        var log = new List<string>();
        var progress = new Progress<string>(l => { log.Add(l); Console.WriteLine(l); });

        var installed = installer.GetInstalled(manifest, project) ?? await installer.InstallAsync(manifest, project, progress);
        var rom = await builds.BuildAsync(manifest, installed, project, progress);
        Assert.True(File.Exists(rom));
        var header = NdsRomHeader.Read(rom);
        Assert.Equal(4, header.Generation);
    }
}
