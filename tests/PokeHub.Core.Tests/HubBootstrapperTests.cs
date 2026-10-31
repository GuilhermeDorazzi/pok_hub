using PokeHub.Core.Hub;
using Xunit;

namespace PokeHub.Core.Tests;

public class HubBootstrapperTests
{
    [Fact]
    public void Root_for_one_run_does_not_change_the_configured_one()
    {
        var before = HubBootstrapper.FindConfiguredRoot();
        var temp = Directory.CreateTempSubdirectory("pokehub-root").FullName;
        try
        {
            var paths = HubBootstrapper.Initialize(temp, remember: false);
            Assert.True(File.Exists(paths.ConfigFile));
            Assert.Equal(before, HubBootstrapper.FindConfiguredRoot());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
