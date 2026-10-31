using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;
using Xunit;

namespace PokeHub.Core.Tests;

public class EncounterTests
{
    [Fact]
    public void Hgss_roundtrip_synthetic()
    {
        var e = new HgssEncounters { WalkingRate = 25, SurfRate = 10, OldRodRate = 25 };
        for (int i = 0; i < 12; i++) { e.WalkingLevels[i] = (byte)(2 + i); e.Morning[i] = 16; e.Day[i] = 161; e.Night[i] = 163; }
        e.HoennSound[0] = 311; e.SinnohSound[1] = 403;
        e.Surf[0] = new EncounterSlot { Species = 129, MinLevel = 10, MaxLevel = 20 };
        e.Swarm[0] = 16;
        var bytes = e.Serialize();
        Assert.Equal(HgssEncounters.Size, bytes.Length);
        var back = HgssEncounters.Parse(bytes);
        Assert.Equal(bytes, back.Serialize());
        Assert.Equal(161, back.Day[5]);
        Assert.Equal(129, back.Surf[0].Species);
        Assert.Equal(20, back.Surf[0].MaxLevel);
        Assert.Equal(403, back.SinnohSound[1]);
    }

    [Fact]
    public void Dppt_roundtrip_synthetic()
    {
        var e = new DpptEncounters { WalkingRate = 30, SurfRate = 5, OldRodRate = 25, GoodRodRate = 50, SuperRodRate = 75, Unknown = 7 };
        for (int i = 0; i < 12; i++) { e.Walking[i].Species = (ushort)(396 + i % 3); e.Walking[i].MinLevel = e.Walking[i].MaxLevel = (byte)(2 + i); }
        e.Day[0] = 400; e.Night[1] = 401; e.Radar[3] = 420; e.FormData[2] = 1; e.Emerald[1] = 335;
        e.Surf[2] = new EncounterSlot { Species = 54, MinLevel = 20, MaxLevel = 30 };
        e.SuperRod[4] = new EncounterSlot { Species = 130, MinLevel = 40, MaxLevel = 55 };
        var bytes = e.Serialize();
        Assert.Equal(DpptEncounters.Size, bytes.Length);
        var back = DpptEncounters.Parse(bytes);
        Assert.Equal(bytes, back.Serialize());
        Assert.Equal(30, back.Surf[2].MaxLevel);
        Assert.Equal(130, back.SuperRod[4].Species);
        Assert.Equal(335, back.Emerald[1]);
        Assert.Equal(7u, back.Unknown);
    }

    [Fact]
    public void Blz_passes_through_uncompressed_data()
    {
        var raw = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0, 0, 0, 0, 0, 0 }; // rodapé com acréscimo 0
        Assert.Equal(raw, Blz.Decompress(raw));
        Assert.False(Blz.LooksCompressed(raw));
    }

    /// <summary>Com POKEHUB_WORKSPACE (HGSS) e, opcionalmente, POKEHUB_DSROM_ARM9 (arm9.bin descomprimido pelo ds-rom) para conferir o BLZ.</summary>
    [Fact]
    public void Real_workspace_route_29_and_headers()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_WORKSPACE");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        if (project.Config.BaseGame is not (BaseGame.HeartGold or BaseGame.SoulSilver)) return;

        var repo = EncounterRepository.Load(project);
        Assert.True(repo.Entries.Count > 100);
        Assert.True(repo.Headers.Count > 500, $"cabeçalhos: {repo.Headers.Count}");

        var r29 = repo.Headers.First(h => h.InternalName == "R29");
        Assert.Equal("Route 29", repo.LocationName(r29.LocationNameIndex));
        Assert.NotNull(r29.EncounterFile);
        var e = repo.Entries[r29.EncounterFile!.Value];
        Assert.Contains(e.Maps, m => m.InternalName == "R29");
        Assert.Equal(25, e.Hgss!.WalkingRate);
        Assert.Equal("PIDGEY", repo.Species[e.Hgss.Morning[0]]);
        Assert.Equal("HOOTHOOT", repo.Species[e.Hgss.Night[0]]);
        Assert.Equal("PLUSLE", repo.Species[e.Hgss.HoennSound[0]]);
        Assert.Contains("Route 29", repo.Label(e));

        // regravar sem mudanças mantém o NARC idêntico
        var before = File.ReadAllBytes(repo.Game.EncountersPath);
        try
        {
            Assert.False(repo.HasChanges);
            repo.Save();
            Assert.Equal(before, File.ReadAllBytes(repo.Game.EncountersPath));
            e.Hgss.Morning[0] = 25;
            Assert.True(repo.HasChanges);
            repo.Save();
            var again = EncounterRepository.Load(project);
            Assert.Equal(25, again.Entries[e.Id].Hgss!.Morning[0]);
        }
        finally { File.WriteAllBytes(repo.Game.EncountersPath, before); }

        var dsromArm9 = Environment.GetEnvironmentVariable("POKEHUB_DSROM_ARM9");
        if (!string.IsNullOrEmpty(dsromArm9) && File.Exists(dsromArm9))
        {
            var mine = Blz.DecompressArm9(File.ReadAllBytes(repo.Game.Arm9Path));
            var theirs = File.ReadAllBytes(dsromArm9);
            int n = Math.Min(mine.Length, theirs.Length);
            Assert.True(mine.AsSpan(0, n).SequenceEqual(theirs.AsSpan(0, n)), $"BLZ difere do ds-rom em 0x{mine.AsSpan(0, n).CommonPrefixLength(theirs.AsSpan(0, n)):X}");
        }
    }
}
