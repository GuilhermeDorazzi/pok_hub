using PokeHub.Core.Decomp;
using PokeHub.Core.Hub;
using PokeHub.Core.Runtimes;
using Xunit;

namespace PokeHub.Core.Tests;

public class DependencyTests
{
    private static DependencyChecker NewChecker()
    {
        var paths = new HubPaths(Path.GetTempPath());
        var config = new HubConfig();
        return new DependencyChecker(new RuntimeManager(paths, config), new DecompBuildService(config, paths));
    }

    [Fact]
    public void DotNet_framework_is_detected_on_this_machine()
    {
        // Windows 10/11 trazem o .NET Framework 4.8 embutido; o teste serve para validar a leitura do registro.
        var s = NewChecker().Check("dotnet-framework-4.8");
        Assert.Equal("dotnet-framework-4.8", s.Id);
        Assert.NotNull(s.Satisfied);
        if (s.Satisfied == true) Assert.Null(s.InstallUrl); else Assert.NotNull(s.InstallUrl);
    }

    [Fact]
    public void Older_framework_requirement_is_satisfied_when_newer_is_present()
    {
        var c = NewChecker();
        if (c.Check("dotnet-framework-4.8").Satisfied != true) return;
        Assert.True(c.Check("dotnet-framework-4.5").Satisfied);
    }

    [Fact]
    public void Unknown_requirement_is_informational()
    {
        var s = NewChecker().Check("mwcc");
        Assert.Null(s.Satisfied);
        Assert.False(s.IsMissing);
    }

    [Fact]
    public void Managed_runtime_reports_hub_installs_it()
    {
        var s = NewChecker().Check("java21");
        Assert.False(s.Satisfied);
        Assert.Contains("hub instala", s.Detail);
        Assert.Null(s.InstallUrl);
    }

    [Fact]
    public void Unix_shell_is_unknown_before_detection()
    {
        var s = NewChecker().Check("unix-shell");
        Assert.Null(s.Satisfied);
    }
}
