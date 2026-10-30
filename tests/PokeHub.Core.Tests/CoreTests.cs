using System.Text;
using PokeHub.Core.Hub;
using PokeHub.Core.Plugins;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;
using Xunit;

namespace PokeHub.Core.Tests;

public class RomHeaderTests
{
    private static string FakeRom(string title, string gameCode)
    {
        var bytes = new byte[0x400];
        Encoding.ASCII.GetBytes(title.PadRight(12, '\0')).CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(gameCode).CopyTo(bytes, 0x0C);
        Encoding.ASCII.GetBytes("01").CopyTo(bytes, 0x10);
        BitConverter.GetBytes(0x400u).CopyTo(bytes, 0x80);
        var path = Path.Combine(Path.GetTempPath(), $"pokehub-test-{Guid.NewGuid():N}.nds");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Theory]
    [InlineData("CPUE", BaseGame.Platinum, 4, "USA")]
    [InlineData("IPKE", BaseGame.HeartGold, 4, "USA")]
    [InlineData("IPGP", BaseGame.SoulSilver, 4, "EUR")]
    [InlineData("ADAJ", BaseGame.Diamond, 4, "JPN")]
    [InlineData("APAE", BaseGame.Pearl, 4, "USA")]
    [InlineData("IRBO", BaseGame.Unknown, 0, "O")] // GEN5: Black ainda não suportado
    public void Detects_game_from_header(string code, BaseGame expected, int gen, string region)
    {
        var rom = FakeRom("POKEMON PL", code);
        try
        {
            var h = NdsRomHeader.Read(rom);
            Assert.Equal(code, h.GameCode);
            Assert.Equal(expected, h.Game);
            Assert.Equal(gen, h.Generation);
            Assert.Equal(region, h.Region);
            Assert.Equal("POKEMON PL", h.Title);
        }
        finally { File.Delete(rom); }
    }

    [Fact]
    public void Rejects_tiny_file()
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, new byte[10]);
        try { Assert.Throws<InvalidDataException>(() => NdsRomHeader.Read(path)); }
        finally { File.Delete(path); }
    }
}

public class PluginCatalogTests
{
    private static string PluginsDir => Path.Combine(AppContext.BaseDirectory, "plugins");

    [Fact]
    public void Loads_all_bundled_manifests()
    {
        var catalog = PluginCatalog.LoadFromDirectory(PluginsDir);
        Assert.True(catalog.All.Count >= 7, $"só {catalog.All.Count} manifestos");
        foreach (var m in catalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Id));
            Assert.NotEmpty(m.Contexts);
            Assert.NotEmpty(m.Generations);
            if (m.Source.Type is SourceType.GitHubRelease or SourceType.GitClone) Assert.False(string.IsNullOrWhiteSpace(m.Source.Repo), m.Id);
            if (m.Source.Type == SourceType.GitHubRelease) Assert.False(string.IsNullOrWhiteSpace(m.Source.AssetPattern), m.Id);
        }
    }

    [Fact]
    public void Filters_by_context_and_generation()
    {
        var catalog = PluginCatalog.LoadFromDirectory(PluginsDir);
        Assert.Contains(catalog.ForContext(ToolContext.Emulator, 4), p => p.Id == "melonds");
        Assert.Empty(catalog.ForContext(ToolContext.Emulator, 5)); // GEN5: passa a ter melonds quando generations incluir 5
        Assert.Contains(catalog.ForContext(ToolContext.Decomp, 4), p => p.Scope == PluginScope.Project);
    }
}

public class HubLayoutTests
{
    [Fact]
    public void Project_creation_builds_expected_tree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pokehub-{Guid.NewGuid():N}");
        try
        {
            var paths = new HubPaths(root);
            foreach (var d in paths.AllDirectories()) Directory.CreateDirectory(d);
            var config = new HubConfig();
            config.Save(paths.ConfigFile);

            var rom = Path.Combine(root, "dump.nds");
            var bytes = new byte[0x400];
            Encoding.ASCII.GetBytes("IPKE").CopyTo(bytes, 0x0C);
            File.WriteAllBytes(rom, bytes);

            var svc = new ProjectService(paths);
            var p = svc.Create("meu-hack", rom);

            Assert.True(File.Exists(p.Paths.BaseRom));
            Assert.True(Directory.Exists(p.Paths.Workspace));
            Assert.True(Directory.Exists(p.Paths.Builds));
            Assert.Equal(BaseGame.HeartGold, p.Config.BaseGame);

            var reloaded = svc.Open("meu-hack");
            Assert.NotNull(reloaded);
            Assert.Equal("IPKE", reloaded!.Config.GameCode);
            Assert.Single(svc.ListProjects());

            Assert.Throws<InvalidOperationException>(() => svc.Create("meu-hack", rom));
            Assert.Throws<ArgumentException>(() => svc.Create("a/b", rom));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Config_roundtrips_installed_tools()
    {
        var file = Path.GetTempFileName();
        try
        {
            var c = new HubConfig();
            c.InstalledTools["dspre"] = new InstalledTool { Version = "v2.3.4.0", ExecutablePath = @"C:\x\DSPRE.exe" };
            c.Save(file);
            var back = HubConfig.Load(file);
            Assert.Equal("v2.3.4.0", back.InstalledTools["DSPRE"].Version);
        }
        finally { File.Delete(file); }
    }
}
