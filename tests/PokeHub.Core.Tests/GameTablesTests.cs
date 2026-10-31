using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class GameTablesTests
{
    private static IReadOnlyList<GameTable>? Open(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return null;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        return GameTables.Load(project);
    }

    private static int Cell(GameTable t, TableRow row, string column) => row.Get(t.Columns.First(c => c.Key == column));

    [Fact]
    public void Row_reads_bits_signed_values_and_keeps_neighbors()
    {
        var data = new byte[] { 0xFF, 0xFF, 0xFF };
        var row = new TableRow("x", [data], [0]);
        var low = new TableColumn("low", 0, 2, Bit: 0, Bits: 9);
        var high = new TableColumn("high", 0, 2, Bit: 9, Bits: 7);
        row.Set(low, 33);
        Assert.Equal(33, row.Get(low));
        Assert.Equal(127, row.Get(high));
        row.Set(high, 500); // limite do campo
        Assert.Equal(127, row.Get(high));
        Assert.Equal(0xFF, data[2]);

        var signed = new TableColumn("s", 2, 1, Signed: true);
        row.Set(signed, -3);
        Assert.Equal(-3, row.Get(signed));
        Assert.Equal(0xFD, data[2]);
    }

    [Fact]
    public void Bit_row_changes_a_single_bit()
    {
        var data = new byte[4];
        var row = new TableBitRow("TM", data, 1, 9);
        var c = new TableColumn("learns", 0, 1, TableCellKind.Flag);
        row.Set(c, 1);
        Assert.Equal(2, data[2]);
        Assert.Equal(1, row.Get(c));
        row.Set(c, 0);
        Assert.Equal(0, data[2]);
    }

    [Theory]
    [InlineData("POKEHUB_TABLES_SS", true)]
    [InlineData("POKEHUB_TABLES_PT", false)]
    public void Real_workspace_known_values(string variable, bool hgss)
    {
        var tables = Open(variable);
        if (tables is null) return;
        GameTable T(string id) => tables.First(t => t.Id == id);

        var moves = T("moves").Rows(0);
        Assert.Equal(40, Cell(T("moves"), moves[1], "power")); // Pound
        Assert.Equal(35, Cell(T("moves"), moves[1], "pp"));
        Assert.Equal(0, Cell(T("moves"), moves[1], "category"));
        Assert.Equal(2, Cell(T("moves"), moves[45], "category")); // Growl
        Assert.Equal(1, Cell(T("moves"), moves[98], "priority")); // Quick Attack

        Assert.Equal(200, Cell(T("items"), T("items").Rows(0)[4], "price")); // Poké Ball
        Assert.Equal(16, Cell(T("evolutions"), T("evolutions").Rows(1)[0], "param"));
        Assert.Equal(2, Cell(T("evolutions"), T("evolutions").Rows(1)[0], "target"));
        Assert.Equal(4, Cell(T("evolutions"), T("evolutions").Rows(1)[0], "method"));

        var learn = T("learnsets").Rows(1);
        Assert.Equal(1, Cell(T("learnsets"), learn[0], "level"));
        Assert.Equal(33, Cell(T("learnsets"), learn[0], "move")); // Tackle

        Assert.Equal(1, Cell(T("babies"), T("babies").Rows(0)[2], "baby")); // Venusaur: Bulbasaur
        var dex = T("heightWeight").Rows(0)[24];
        Assert.Equal(4, Cell(T("heightWeight"), dex, "height")); // Pikachu
        Assert.Equal(60, Cell(T("heightWeight"), dex, "weight"));

        Assert.StartsWith("TM01", T("tmMoves").Rows(0)[0].Name);
        Assert.Equal(264, Cell(T("tmMoves"), T("tmMoves").Rows(0)[0], "move")); // Focus Punch
        Assert.Equal(337, Cell(T("tmMoves"), T("tmMoves").Rows(0)[1], "move")); // Dragon Claw

        // Charmander aprende Dragon Claw e não aprende Focus Punch... confere contra o bit do arquivo
        var compat = T("tmCompat");
        Assert.Equal(1, Cell(compat, compat.Rows(4)[1], "learns"));

        Assert.True(T("trades").Rows(0).Count > 0);
        Assert.InRange(Cell(T("trades"), T("trades").Rows(0)[0], "giveSpecies"), 1, 493);
        Assert.InRange(Cell(T("trades"), T("trades").Rows(0)[0], "askSpecies"), 1, 493);

        Assert.True(T("marts").Owners!.Count >= 10);
        Assert.All(T("marts").Rows(0), r => Assert.InRange(Cell(T("marts"), r, "item"), 1, 600));

        var chart = T("typeChart").Rows(0);
        Assert.True(chart.Count > 100, $"{chart.Count} entradas");
        Assert.Contains(chart, r => Cell(T("typeChart"), r, "attacker") == 10 && Cell(T("typeChart"), r, "defender") == 12 && Cell(T("typeChart"), r, "multiplier") == 20); // Fogo em Planta
        Assert.Contains(chart, r => Cell(T("typeChart"), r, "attacker") == 0 && Cell(T("typeChart"), r, "defender") == 7 && Cell(T("typeChart"), r, "multiplier") == 0); // Normal em Fantasma

        var frontier = T("frontierMons").Rows(0);
        Assert.True(frontier.Count > 900);
        Assert.Equal(1, Cell(T("frontierMons"), frontier[1], "species"));
        Assert.Equal(230, Cell(T("frontierMons"), frontier[1], "move4")); // Sweet Scent
        Assert.Equal(213, Cell(T("frontierMons"), frontier[1], "heldItem"));

        Assert.True(T("eggMoves").Owners!.Count > 50);
        var bulbaEgg = T("eggMoves").Owners!.ToList().FindIndex(o => o.StartsWith("001"));
        Assert.True(T("eggMoves").Rows(bulbaEgg).Count > 0);

        if (hgss)
        {
            var athlon = T("pokeathlon");
            var pikachu = athlon.Rows(0).First(r => r.Name.EndsWith("PIKACHU"));
            Assert.True(athlon.Rows(0).Count > 493);
            foreach (var r in athlon.Rows(0))
                foreach (var stat in new[] { "power", "stamina", "jump", "skill" })
                {
                    Assert.InRange(Cell(athlon, r, stat), 0, 5);
                    Assert.True(Cell(athlon, r, stat + "Min") <= Cell(athlon, r, stat + "Max"), r.Name);
                    Assert.InRange(Cell(athlon, r, stat + "Max"), 0, 5);
                }
            Assert.InRange(Cell(athlon, pikachu, "speedStat"), 1, 5);
        }
        else
        {
            var contest = T("contestOpponents");
            Assert.Equal(96, contest.Rows(0).Count);
            Assert.EndsWith("Sparky", contest.Rows(0)[0].Name);
            Assert.Equal(25, Cell(contest, contest.Rows(0)[0], "species")); // Pikachu
            Assert.Equal(84, Cell(contest, contest.Rows(0)[0], "move1")); // Thunder Shock
            Assert.All(contest.Rows(0), r =>
            {
                Assert.InRange(Cell(contest, r, "species"), 1, 493);
                Assert.InRange(Cell(contest, r, "move1"), 1, 467);
            });
        }

        var sprite = T("spritePosition");
        Assert.Equal(493, sprite.Rows(0).Count);
        Assert.All(sprite.Rows(0), r => Assert.InRange(Cell(sprite, r, "shadowSize"), 0, 3)); // sombra pequena a enorme: fora disso, a coluna estaria errada

        var mart = T("martBadges").Rows(0);
        Assert.Equal(19, mart.Count);
        Assert.Equal(4, Cell(T("martBadges"), mart[0], "item")); // Poké Ball
        Assert.Equal(1, Cell(T("martBadges"), mart[0], "badges"));

        var trainer = T("frontierTrainers").Rows(1);
        Assert.Equal(2, Cell(T("frontierTrainers"), trainer[0], "set")); // classe
        Assert.Equal(51, trainer.Count); // classe e 50 conjuntos

        Assert.Equal(291, Cell(T("tutors"), T("tutors").Rows(0)[0], "move")); // Dive
        Assert.Equal(189, Cell(T("tutors"), T("tutors").Rows(0)[1], "move")); // Mud-Slap

        if (hgss)
        {
            var headbutt = T("headbutt");
            var route1 = headbutt.Owners!.ToList().FindIndex(o => o.EndsWith(" R01"));
            Assert.True(route1 >= 0);
            Assert.Equal(163, Cell(headbutt, headbutt.Rows(route1)[0], "species")); // Hoothoot
            Assert.Equal(2, Cell(headbutt, headbutt.Rows(route1)[0], "minLevel"));

            var safari = T("safari");
            Assert.Equal(12, safari.Owners!.Count);
            Assert.Equal(150, safari.Rows(0).Count);
            Assert.Equal(19, Cell(safari, safari.Rows(0)[0], "species")); // Rattata
            Assert.Equal(15, Cell(safari, safari.Rows(0)[0], "level"));

            Assert.True(T("eggMoves").Owners!.Count > 50);
            Assert.Equal(52, T("tutors").Rows(0).Count);
            Assert.Equal(291, Cell(T("tutors"), T("tutors").Rows(0)[0], "move")); // Dive
            Assert.Equal(40, Cell(T("tutors"), T("tutors").Rows(0)[0], "cost"));
        }

        // scripts: toda linha de argumento relê o próprio valor, e gravar o mesmo valor não muda o arquivo
        var scripts = T("scripts");
        int args = 0;
        for (int o = 0; o < scripts.Owners!.Count; o += 25)
            foreach (var r in scripts.Rows(o))
            {
                r.Set(scripts.Columns[0], r.Get(scripts.Columns[0]));
                args++;
            }
        Assert.True(args > 500, $"{args} argumentos");

        Assert.All(tables, t => Assert.False(t.HasChanges, t.Id));
        foreach (var t in tables.Where(t => t.CanResize))
            for (int o = 0; o < t.Owners!.Count; o++)
                _ = t.Rows(o);
        Assert.All(tables, t => Assert.False(t.HasChanges, t.Id)); // abrir tudo não muda nada
    }

    [Fact]
    public void Real_workspace_save_learnset_round_trip()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_TABLES_SCRATCH");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var t = GameTables.Load(project).First(x => x.Id == "learnsets");
        int before = t.Rows(25).Count;
        var row = t.AddRow(25);
        row.Set(t.Columns[0], 1);
        row.Set(t.Columns[1], 94);
        Assert.True(t.HasChanges);
        t.Save();
        var again = GameTables.Load(project).First(x => x.Id == "learnsets");
        Assert.Equal(before + 1, again.Rows(25).Count);
        Assert.Contains(again.Rows(25), r => r.Get(again.Columns[1]) == 94);
        Assert.False(again.HasChanges);
        var at = again.Rows(25).ToList().FindIndex(r => r.Get(again.Columns[1]) == 94);
        again.RemoveRow(25, at);
        again.Save();
        var final = GameTables.Load(project).First(x => x.Id == "learnsets");
        Assert.Equal(before, final.Rows(25).Count);
    }
}
