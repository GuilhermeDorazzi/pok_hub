using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class CatchAuditTests
{
    private static Project? ProjectFrom(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return null;
        var dir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        return new Project { Paths = new ProjectPaths(dir), Config = ProjectConfig.Load(Path.Combine(dir, "project.json")) };
    }

    [Fact]
    public void Real_soulsilver_scan_covers_every_species_and_separates_what_it_knows()
    {
        if (ProjectFrom("POKEHUB_TABLES_SS") is not { } project) return;
        var rows = CatchAudit.Build(project);
        Assert.Equal(493, rows.Count);
        Assert.Equal(Enumerable.Range(1, 493), rows.Select(r => r.Species));
        // Rattata na grama, Politoed por troca, Chikorita por lista, Mew de evento
        Assert.Equal(CatchStatus.Found, rows[18].Status);
        Assert.False(string.IsNullOrEmpty(rows[18].Where));
        Assert.True(rows[185].TradeOnly);
        Assert.Equal(CatchStatus.Family, rows[185].Status);
        Assert.Equal(CatchStatus.Known, rows[151].Status);
        Assert.Equal(CatchStatus.Missing, rows[150].Status);
        Assert.Contains(rows, r => r.Status == CatchStatus.Missing && r.Species == 56);
    }

    [Fact]
    public void Trade_evolutions_are_marked_and_the_list_matches_the_game()
    {
        if (ProjectFrom("POKEHUB_TABLES_SS") is not { } project) return;
        var trade = CatchAudit.Build(project).Where(r => r.TradeOnly).Select(r => r.Species).ToHashSet();
        Assert.True(new[] { 186, 65, 68, 76, 199, 94, 208, 464, 230, 212, 466, 467, 233, 474, 477, 367, 368 }.All(trade.Contains));
    }
}
