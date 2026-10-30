using System.Text.Json.Nodes;
using PokeHub.Core.Plugins;
using PokeHub.Core.Rom;
using PokeHub.Core.Runtimes;
using Xunit;

namespace PokeHub.Core.Tests;

public class DsRomTests
{
    [Fact]
    public void DsRom_runtime_spec_targets_windows_exe()
    {
        var spec = RuntimeManager.Known[RuntimeManager.DsRomId];
        Assert.Matches(spec.AssetPattern!, "dsrom-windows-x86_64.exe");
        Assert.DoesNotMatch(spec.AssetPattern!, "dsrom-linux-x86_64");
    }

    [Fact]
    public void Dspre_settings_hook_preserves_other_keys()
    {
        var file = Path.Combine(Path.GetTempPath(), $"pokehub-dspre-{Guid.NewGuid():N}", "userSettings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """{ "useDecompNames": true, "openDefaultRom": "C:\\velho" }""");
            LaunchHooks.SetDspreOpenDefaultRom(file, @"D:\PokeHub\projects\x\workspace");
            var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            Assert.Equal(@"D:\PokeHub\projects\x\workspace", (string?)root["openDefaultRom"]);
            Assert.True((bool?)root["useDecompNames"]);
        }
        finally { Directory.Delete(Path.GetDirectoryName(file)!, true); }
    }

    [Fact]
    public void Dspre_manifest_uses_workspace_hook()
    {
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        Assert.Equal("dspre-open-workspace", catalog.Get("dspre")!.Launch!.Hook);
        Assert.Contains("dspre-open-workspace", LaunchHooks.All.Keys);
    }

    /// <summary>Com POKEHUB_REAL_ROM e POKEHUB_DSROM definidos: extrai e reconstrói com o ds-rom e exige igualdade byte a byte até o tamanho usado.</summary>
    [Fact]
    public async Task Real_rom_roundtrip_with_dsrom_is_byte_identical()
    {
        var romPath = Environment.GetEnvironmentVariable("POKEHUB_REAL_ROM");
        var dsrom = Environment.GetEnvironmentVariable("POKEHUB_DSROM");
        if (string.IsNullOrEmpty(romPath) || !File.Exists(romPath) || string.IsNullOrEmpty(dsrom) || !File.Exists(dsrom)) return;

        var root = Path.Combine(Path.GetTempPath(), $"pokehub-dsrom-{Guid.NewGuid():N}");
        try
        {
            var ws = Path.Combine(root, "workspace");
            var log = new List<string>();
            var meta = await DsRomTool.ExtractAsync(dsrom, romPath, ws, new Progress<string>(log.Add));
            Assert.True(DsRomTool.IsDsRomWorkspace(ws));
            Assert.NotNull(DsRomTool.LoadMeta(ws));

            var output = Path.Combine(root, "rebuilt.nds");
            await DsRomTool.BuildAsync(dsrom, ws, output, new Progress<string>(log.Add));

            var original = File.ReadAllBytes(romPath);
            var rebuilt = File.ReadAllBytes(output);
            uint used = BitConverter.ToUInt32(original, NdsHeader.TotalUsedSize);
            Assert.True(rebuilt.Length >= used);
            int firstDiff = original.AsSpan(0, (int)used).CommonPrefixLength(rebuilt);
            Assert.True(firstDiff == used, $"primeira diferença em 0x{firstDiff:X}; log: {string.Join(" | ", log.TakeLast(5))}");
            Assert.Equal(meta.OriginalSecureAreaCrc, BitConverter.ToUInt16(rebuilt, NdsHeader.SecureAreaCrc));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
