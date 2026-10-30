using PokeHub.Core.Decomp;
using PokeHub.Core.Plugins;
using Xunit;

namespace PokeHub.Core.Tests;

public class DecompTests
{
    [Fact]
    public void Msys2_command_runs_bash_in_repo_dir()
    {
        var shell = new UnixShell(UnixShellKind.Msys2, @"C:\msys64", "MSYS2");
        var (file, args, env) = shell.Compose("make rom", @"D:\PokeHub\projects\x\decomp\pokeplatinum");
        Assert.Equal(@"C:\msys64\usr\bin\bash.exe", file);
        Assert.Equal("-lc", args[0]);
        Assert.Contains("cygpath -u 'D:/PokeHub/projects/x/decomp/pokeplatinum'", args[1]);
        Assert.EndsWith("&& make rom", args[1]);
        Assert.Equal("MSYS", env["MSYSTEM"]);
        Assert.Equal("1", env["CHERE_INVOKING"]);
    }

    [Fact]
    public void Wsl_command_uses_distro_and_windows_cd()
    {
        var shell = new UnixShell(UnixShellKind.Wsl, "Ubuntu", "WSL: Ubuntu");
        var (file, args, _) = shell.Compose("make rom", @"D:\PokeHub\p");
        Assert.Equal("wsl.exe", file);
        Assert.Equal(["-d", "Ubuntu", "--cd", @"D:\PokeHub\p", "-e", "bash", "-lc", "make rom"], args);
    }

    [Fact]
    public async Task Detection_does_not_throw()
    {
        var shells = await UnixShell.DetectAsync();
        Assert.NotNull(shells);
        Assert.All(shells, s => Assert.False(string.IsNullOrWhiteSpace(s.Location)));
    }

    [Fact]
    public void Decomp_manifests_declare_build()
    {
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        foreach (var m in catalog.ForContext(ToolContext.Decomp))
        {
            Assert.NotNull(m.Build);
            Assert.EndsWith(".nds", m.Build!.Output);
            Assert.Contains("unix-shell", m.Requires);
        }
    }
}
