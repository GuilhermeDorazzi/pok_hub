using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class PokedexAreaTests
{
    [Fact]
    public void File_index_matches_pk_dex_editor_offsets()
    {
        Assert.Equal(16 + 1487, PokedexAreaRepository.FileIndex(16, PokedexAreaBlock.NormalMorning));
        Assert.Equal(16 + 1982, PokedexAreaRepository.FileIndex(16, PokedexAreaBlock.NormalDay));
        Assert.Equal(16 + 2477, PokedexAreaRepository.FileIndex(16, PokedexAreaBlock.NormalNight));
        Assert.Equal(16 + 3467, PokedexAreaRepository.FileIndex(16, PokedexAreaBlock.NormalAll));
        Assert.Equal(16 + 2, PokedexAreaRepository.FileIndex(16, PokedexAreaBlock.SpecialMorning));
        Assert.Equal(16 + 497, PokedexAreaRepository.FileIndex(16, PokedexAreaBlock.SpecialDay));
        Assert.Equal(16 + 992, PokedexAreaRepository.FileIndex(16, PokedexAreaBlock.SpecialNight));
        Assert.Equal(16 + 2972, PokedexAreaRepository.FileIndex(16, PokedexAreaBlock.SpecialAll));
        Assert.Equal(63, PokedexAreaRepository.NormalLocations.Count);
        Assert.Equal(24, PokedexAreaRepository.SpecialLocations.Count);
        Assert.DoesNotContain(PokedexAreaRepository.SpecialLocations, l => l.Id == 0x13); // Olivine [SS Aqua] removido como no original
    }

    /// <summary>Com POKEHUB_WORKSPACE (HGSS): lê o Pidgey, regrava sem mudar nada e exige NARC idêntico; depois edita e confere.</summary>
    [Fact]
    public void Real_workspace_reads_pidgey_and_rewrites_identically()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_WORKSPACE");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        if (project.Config.BaseGame is not (Rom.BaseGame.HeartGold or Rom.BaseGame.SoulSilver)) return;

        var repo = PokedexAreaRepository.Load(project);
        var narcPath = Path.Combine(repo.Game.FilesRoot, PokedexAreaRepository.HgssNarcRelativePath);
        var before = File.ReadAllBytes(narcPath);
        try
        {
            var morning = repo.GetAreas(16, PokedexAreaBlock.NormalMorning);
            Assert.Contains(0x02u, morning); // Route 29
            Assert.Contains(0x2Bu, morning); // Route 1
            Assert.Contains(0x18u, repo.GetAreas(16, PokedexAreaBlock.SpecialMorning)); // Viridian Forest
            Assert.Equal("PIDGEY", repo.SpeciesNames[16]);

            repo.SetAreas(16, PokedexAreaBlock.NormalMorning, morning);
            Assert.False(repo.HasChanges);
            repo.Save();
            Assert.Equal(before, File.ReadAllBytes(narcPath));

            repo.SetAreas(16, PokedexAreaBlock.NormalNight, [0x01u, 0x02u]);
            Assert.True(repo.HasChanges);
            repo.Save();
            var again = PokedexAreaRepository.Load(project);
            Assert.Equal([0x01u, 0x02u], again.GetAreas(16, PokedexAreaBlock.NormalNight));
        }
        finally { File.WriteAllBytes(narcPath, before); }
    }
}
