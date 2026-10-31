using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using Xunit;

namespace PokeHub.Core.Tests;

public class PluginInstallIntegrationTests
{
    /// <summary>Com POKEHUB_HUB_ROOT e POKEHUB_INSTALL_PLUGINS="id1,id2": instala de verdade os plugins na raiz indicada (download real).</summary>
    [Fact]
    public async Task Installs_requested_plugins_into_hub_root()
    {
        var root = Environment.GetEnvironmentVariable("POKEHUB_HUB_ROOT");
        var ids = Environment.GetEnvironmentVariable("POKEHUB_INSTALL_PLUGINS");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root) || string.IsNullOrEmpty(ids)) return;

        var paths = new HubPaths(root);
        var config = HubConfig.Load(paths.ConfigFile);
        var installer = new PluginInstaller(paths, config);
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        var log = new List<string>();
        foreach (var id in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = catalog.Get(id) ?? throw new InvalidOperationException($"manifesto {id} não existe");
            var tool = await installer.InstallAsync(m, null, new Progress<string>(log.Add));
            Assert.True(File.Exists(tool.ExecutablePath), $"{id}: {tool.ExecutablePath}");
            Assert.NotNull(installer.GetInstalled(m, null));
            if (m.Requires.Contains("java17")) Assert.NotNull(installer.Runtimes.GetInstalled("java17"));
        }
    }
}
