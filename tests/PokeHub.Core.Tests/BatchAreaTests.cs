using PokeHub.Core.Batch;
using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class BatchAreaTests
{
    private static EvolutionChart Chart() => new(new Dictionary<int, IReadOnlyList<(int, int, int)>>
    {
        [16] = [(4, 18, 17)],
        [17] = [(4, 36, 18)],
        [64] = [(5, 0, 65)],
        [133] = [(7, 83, 135), (7, 84, 134)],
        [290] = [(13, 20, 291), (14, 20, 292)],
    });

    [Theory]
    [InlineData(16, 17, 0, 16)]
    [InlineData(16, 18, 0, 17)]
    [InlineData(16, 40, 0, 18)] // dois estágios de uma vez
    [InlineData(64, 50, 0, 64)] // troca fica de fora com zero
    [InlineData(64, 39, 40, 64)]
    [InlineData(64, 40, 40, 65)]
    [InlineData(133, 40, 40, 135)] // o primeiro caminho vale
    [InlineData(290, 30, 40, 291)] // Nincada vira Ninjask, nunca Shedinja
    [InlineData(1, 100, 40, 1)] // sem linha na tabela
    public void Evolve_follows_the_table(int species, int level, int otherAt, int expected) =>
        Assert.Equal(expected, Chart().Evolve(species, level, otherAt));

    private static Project? Scratch()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_TABLES_SS");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return null;
        var dir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        return new Project { Paths = new ProjectPaths(dir), Config = ProjectConfig.Load(Path.Combine(dir, "project.json")) };
    }

    [Fact]
    public void Real_headers_tell_johto_from_kanto()
    {
        if (Scratch() is not { } project) return;
        var headers = MapHeaders.Load(Gen4Game.For(project));
        Assert.Equal(0, headers.First(h => h.InternalName == "R29").Region);
        Assert.Equal(0, headers.First(h => h.InternalName == "T20").Region);
        Assert.Equal(1, headers.First(h => h.InternalName == "R01").Region);
        Assert.Equal(1, headers.First(h => h.InternalName == "R22").Region);
    }

    [Fact]
    public void Real_area_filter_reaches_only_the_chosen_places_and_preview_changes_nothing()
    {
        if (Scratch() is not { } project) return;
        var repo = EncounterRepository.Load(project);
        var kanto = BatchEditor.PreviewEncounters(repo, new EncounterBatchOptions { Region = EncounterRegion.Second, LevelOffset = 5 });
        var johto = BatchEditor.PreviewEncounters(repo, new EncounterBatchOptions { Region = EncounterRegion.First, LevelOffset = 5 });
        var one = BatchEditor.PreviewEncounters(repo, new EncounterBatchOptions { PlaceFilter = "R29", LevelOffset = 5 });
        Assert.NotEmpty(kanto);
        Assert.NotEmpty(johto);
        Assert.DoesNotContain(kanto, l => l.Contains("[R29"));
        Assert.Contains(johto, l => l.Contains("[R29"));
        Assert.All(one, l => Assert.Contains("R29", l));
        Assert.NotEmpty(one);
        Assert.False(repo.HasChanges);
    }

    [Fact]
    public void Real_evolving_wild_pokemon_matches_preview_and_apply()
    {
        if (Scratch() is not { } project) return;
        var repo = EncounterRepository.Load(project);
        var options = new EncounterBatchOptions { PlaceFilter = "R29", LevelOffset = 40, Evolutions = EvolutionChart.Load(project), EvolveOtherAt = 40 };
        var preview = BatchEditor.PreviewEncounters(repo, options);
        Assert.Contains(preview, l => l.Contains("PIDGEOT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(preview, BatchEditor.ApplyEncounters(repo, options));
        // sem gravar: o arquivo do workspace não muda
    }
}
