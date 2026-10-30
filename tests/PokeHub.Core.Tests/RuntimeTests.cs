using PokeHub.Core.Hub;
using PokeHub.Core.Runtimes;
using Xunit;

namespace PokeHub.Core.Tests;

public class RuntimeTests
{
    [Fact]
    public void Known_runtimes_and_fallback_to_path()
    {
        Assert.True(RuntimeManager.IsManaged("java21"));
        Assert.True(RuntimeManager.IsManaged("JAVA21"));
        Assert.False(RuntimeManager.IsManaged("mwcc"));

        var root = Path.Combine(Path.GetTempPath(), $"pokehub-rt-{Guid.NewGuid():N}");
        var paths = new HubPaths(root);
        var mgr = new RuntimeManager(paths, new HubConfig());
        Assert.Equal("java", mgr.ResolveJava(["java21"]));
        Assert.Null(mgr.GetInstalled("java21"));
    }

    [Fact]
    public void Installed_runtime_is_ignored_when_executable_is_missing()
    {
        var config = new HubConfig();
        config.InstalledRuntimes["java21"] = new InstalledTool { Version = "x", ExecutablePath = @"C:\nao\existe\java.exe" };
        var mgr = new RuntimeManager(new HubPaths(Path.GetTempPath()), config);
        Assert.Null(mgr.GetInstalled("java21"));
        Assert.Equal("java", mgr.ResolveJava(["java21"]));
    }

    /// <summary>Baixa o Temurin de verdade. Só roda com POKEHUB_NETWORK_TESTS=1.</summary>
    [Fact]
    public async Task Installs_temurin_from_adoptium()
    {
        if (Environment.GetEnvironmentVariable("POKEHUB_NETWORK_TESTS") != "1") return;

        var root = Path.Combine(Path.GetTempPath(), $"pokehub-rt-{Guid.NewGuid():N}");
        try
        {
            var paths = new HubPaths(root);
            foreach (var d in paths.AllDirectories()) Directory.CreateDirectory(d);
            var config = new HubConfig();
            config.Save(paths.ConfigFile);
            var mgr = new RuntimeManager(paths, config);
            var log = new List<string>();

            var tool = await mgr.InstallAsync("java21", new Progress<string>(log.Add));
            Assert.True(File.Exists(tool.ExecutablePath), tool.ExecutablePath);
            Assert.StartsWith("jdk-21", tool.Version);
            Assert.Equal(tool.ExecutablePath, mgr.ResolveJava(["java21"]));
            Assert.Equal(tool.ExecutablePath, HubConfig.Load(paths.ConfigFile).InstalledRuntimes["java21"].ExecutablePath);

            // o java instalado responde
            var psi = new System.Diagnostics.ProcessStartInfo(tool.ExecutablePath, "-version")
            { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            Assert.Contains("21", err);
        }
        finally { DeleteWithRetry(root); }
    }

    /// <summary>O java.exe recém-encerrado ainda segura a java.dll por alguns instantes.</summary>
    private static void DeleteWithRetry(string dir)
    {
        for (int i = 0; i < 10 && Directory.Exists(dir); i++)
        {
            try { Directory.Delete(dir, true); }
            catch (UnauthorizedAccessException) { Thread.Sleep(300); }
            catch (IOException) { Thread.Sleep(300); }
        }
    }
}
