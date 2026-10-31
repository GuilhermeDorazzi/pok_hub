using PokeHub.Core.Plugins;
using Xunit;

namespace PokeHub.Core.Tests;

public class ManifestTextsTests
{
    private static PluginCatalog Catalog => PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));

    [Fact]
    public void Every_manifest_explains_its_purpose()
    {
        foreach (var m in Catalog.All)
            Assert.False(string.IsNullOrWhiteSpace(m.Purpose), $"{m.Id} sem purpose");
    }

    [Fact]
    public void Installable_manifests_state_their_cost()
    {
        foreach (var m in Catalog.All.Where(m => m.Source.Type != SourceType.Builtin))
            Assert.False(string.IsNullOrWhiteSpace(m.Cost), $"{m.Id} sem cost");
    }

    [Fact]
    public void Heavy_installs_confirm_and_decomps_warn_before_first_build()
    {
        Assert.True(Catalog.Get("msys2")!.ConfirmInstall);
        foreach (var m in Catalog.All.Where(m => m.Build is not null))
        {
            Assert.True(m.ConfirmInstall, $"{m.Id} deveria confirmar a instalação");
            Assert.False(string.IsNullOrWhiteSpace(m.Build!.FirstBuildNotice), $"{m.Id} sem firstBuildNotice");
            Assert.Contains("minutos", m.Build.FirstBuildNotice);
        }
    }
}
