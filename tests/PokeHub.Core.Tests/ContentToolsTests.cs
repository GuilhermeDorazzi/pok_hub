using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

[Collection("Eventos no workspace real")] // gravam no mesmo workspace: em paralelo, um apaga o que o outro criou
public class ContentToolsTests
{
    private static readonly ScriptCommandTable Table = ScriptCommandTable.Hgss;

    private static Project? From(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return null;
        var dir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        return new Project { Paths = new ProjectPaths(dir), Config = ProjectConfig.Load(Path.Combine(dir, "project.json")) };
    }

    private static List<ScriptInstruction> Decode(byte[] code)
    {
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        return new ScriptFile(file, Table).Routine(1)!.Instructions.OrderBy(i => i.Offset).ToList();
    }

    [Fact]
    public void Guide_speaks_the_first_step_not_finished()
    {
        var guide = new GuideBehavior([new GuideStep(0x900, "a"), new GuideStep(0x901, "b")], "fim");
        var ins = Decode(NewEventService.BuildScript(Table, guide, [10, 11, 12], null));
        Assert.Equal([0x900L, 0x901L], ins.Where(i => i.Name == "CheckFlag").Select(i => i.Args[0].Value));
        Assert.All(ins.Where(i => i.Name == "GoToIf").Take(2), i => Assert.Equal(0, i.Args[0].Value)); // flag desligada salta
        Assert.Equal(12, ins.First(i => i.Name == "NPCMsg").Args[0].Value); // tudo feito: a última fala
        Assert.Equal(3, ins.Count(i => i.Name == "NPCMsg"));
    }

    [Fact]
    public void Trade_checks_the_level_only_when_asked_and_before_trading()
    {
        var plain = Decode(NewEventService.BuildScript(Table, new TradeBehavior(25, 26, null, "a", "b", "c", "d"), [0, 1, 2, 3], 0x900, tradeSlot: 5));
        Assert.DoesNotContain(plain, i => i.Name == "MonGetLevel");
        var ins = Decode(NewEventService.BuildScript(Table, new TradeBehavior(25, 26, null, "a", "b", "c", "d", 30, "e"), [0, 1, 2, 3, 4], 0x900, tradeSlot: 5));
        var level = ins.Single(i => i.Name == "MonGetLevel");
        Assert.True(ins.IndexOf(level) < ins.FindIndex(i => i.Name == "NPCTradeExec"));
        Assert.True(ins.IndexOf(level) > ins.FindIndex(i => i.Name == "NPCTradeGetReqSpecies"));
        Assert.Equal(30, ins[ins.IndexOf(level) + 1].Args[1].Value);
        Assert.Contains(ins, i => i.Name == "NPCMsg" && i.Args[0].Value == 4);
    }

    [Fact]
    public void Ground_item_marks_before_giving_and_only_the_ball_hides()
    {
        var ball = Decode(NewEventService.BuildScript(Table, new GroundItemBehavior(103, 1, false), [], 0x900, objectId: 7));
        Assert.True(ball.FindIndex(i => i.Name == "HasSpaceForItem") < ball.FindIndex(i => i.Name == "SetFlag"));
        Assert.Equal(7, ball.Single(i => i.Name == "HidePerson").Args[0].Value);
        var hidden = Decode(NewEventService.BuildScript(Table, new GroundItemBehavior(103, 1, true), [], 0x900));
        Assert.DoesNotContain(hidden, i => i.Name == "HidePerson");
        Assert.Equal(0x900, hidden.First(i => i.Name == "CheckFlag").Args[0].Value);
    }

    [Fact]
    public void Real_census_lists_routes_from_empty_to_full()
    {
        if (From("POKEHUB_TABLES_SS") is not { } project) return;
        var rows = TrainerCensus.Build(project);
        Assert.NotEmpty(rows);
        Assert.Equal(rows.OrderBy(r => r.Trainers).Select(r => r.Trainers), rows.Select(r => r.Trainers));
        var r32 = rows.First(r => r.Map == "R32");
        Assert.True(r32.Trainers >= 8 && r32.TopLevel > 0, $"{r32}");
        Assert.Contains(rows, r => r.Map == "R29" && r.Trainers == 0);
    }

    private sealed record Saved(string[] Paths, List<byte[]> Bytes, string Ledger, string? LedgerText);

    private static Saved Keep(Project project)
    {
        var game = Gen4Game.For(project);
        string[] paths = [game.EventsPath, game.ScriptsPath, game.TextArchivePath, game.TrainerDataPath, game.TrainerPartyPath,
            Path.Combine(game.FilesRoot, "a", "0", "5", "7"), Path.Combine(game.FilesRoot, "a", "1", "3", "1")];
        var ledger = Path.Combine(project.Paths.Workspace, "pokehub-events.json");
        return new Saved(paths, paths.Select(File.ReadAllBytes).ToList(), ledger, File.Exists(ledger) ? File.ReadAllText(ledger) : null);
    }

    private static void Restore(Saved s)
    {
        for (int i = 0; i < s.Paths.Length; i++) File.WriteAllBytes(s.Paths[i], s.Bytes[i]);
        if (s.LedgerText is null) File.Delete(s.Ledger);
        else File.WriteAllText(s.Ledger, s.LedgerText);
    }

    [Theory]
    [InlineData("POKEHUB_TABLES_SCRATCH")]
    [InlineData("POKEHUB_TABLES_SCRATCH_LEGACY")]
    public void Real_new_trainer_gets_lines_without_touching_the_old_ones(string variable)
    {
        if (From(variable) is not { } project) return;
        var keep = Keep(project);
        try
        {
            var repo = TrainerRepository.Load(project);
            var copy = repo.AddCopy(repo.Trainers[15], "NOVO");
            repo.Save();
            var game = Gen4Game.For(project);
            var map = EventMap.Load(project);
            var header = map.Headers.First(h => h.InternalName == "R29");
            var at = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]).Objects[0];

            var before = TextRepository.Load(project);
            string? oldIntro = TrainerMessages.Load(project).Text(before, 15, TrainerMessages.Intro);
            Assert.NotNull(oldIntro);

            NewEventService.Create(project, map, new NewNpc(header.Id, at.X + 1, at.Z, at.SpriteId, 1, new TrainerBehavior(copy.Id, "Oi", "Perdi", "Depois", Sight: 3)));
            var table = TrainerMessages.Load(project);
            var texts = TextRepository.Load(project);
            Assert.Equal("Oi", table.Text(texts, copy.Id, TrainerMessages.Intro));
            Assert.Equal("Perdi", table.Text(texts, copy.Id, TrainerMessages.Lose));
            Assert.Equal("Depois", table.Text(texts, copy.Id, TrainerMessages.After));
            Assert.Equal(oldIntro, table.Text(texts, 15, TrainerMessages.Intro));

            // sem visão, só preenche a fala de derrota que faltar: o registro antigo fica como estava
            NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 2, at.Z, at.SpriteId, 1, new TrainerBehavior(15, "a", "b", "c")));
            texts = TextRepository.Load(project);
            Assert.NotEqual("b", TrainerMessages.Load(project).Text(texts, 15, TrainerMessages.Lose));
            NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 3, at.Z, at.SpriteId, 1, new TrainerBehavior(2, "a", "b", "c")));
            texts = TextRepository.Load(project);
            Assert.Equal("b", TrainerMessages.Load(project).Text(texts, 2, TrainerMessages.Lose));
        }
        finally { Restore(keep); }
    }

    [Fact]
    public void Real_ground_items_create_edit_and_remove()
    {
        if (From("POKEHUB_TABLES_SCRATCH") is not { } project) return;
        var keep = Keep(project);
        try
        {
            var game = Gen4Game.For(project);
            var header = EventMap.Load(project).Headers.First(h => h.InternalName == "R29");
            EventFile Events() => new(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]);
            var at = Events().Objects[0];
            int bgs = Events().Bg.Count, objects = Events().Objects.Count;

            var ball = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 1, at.Z, 0, 0, new GroundItemBehavior(103, 1, false)));
            var made = Events().Objects.Single(o => o.ScriptId == ball.ScriptId);
            Assert.Equal(87, made.SpriteId);
            Assert.Equal(ball.Flag, made.EventFlag);

            var hidden = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 2, at.Z, 0, 0, new GroundItemBehavior(103, 1, true)));
            Assert.Equal(bgs + 1, Events().Bg.Count);
            Assert.Equal(objects + 1, Events().Objects.Count);
            var bg = Events().Bg.Single(b => b.ScriptId == hidden.ScriptId);
            Assert.Equal(at.X + 2, bg.X);
            Assert.Equal(at.X + 1, Events().Objects.Single(o => o.ScriptId == ball.ScriptId).X); // objetos seguem lidos certo depois do evento novo

            var created = NewEventLedger.Load(project).Npcs.Single(n => n.ScriptId == hidden.ScriptId);
            Assert.True(created.ObjectId < 0);
            Assert.IsType<GroundItemBehavior>(created.ToBehavior());
            NewEventService.Update(project, EventMap.Load(project), created, created.ToNewNpc() with { X = at.X + 3 });
            Assert.Equal(at.X + 3, Events().Bg.Single(b => b.ScriptId == hidden.ScriptId).X);

            NewEventService.Remove(project, EventMap.Load(project), NewEventLedger.Load(project).Npcs.Single(n => n.ScriptId == hidden.ScriptId));
            Assert.Equal(bgs, Events().Bg.Count);
            NewEventService.Remove(project, EventMap.Load(project), NewEventLedger.Load(project).Npcs.Single(n => n.ScriptId == ball.ScriptId));
            Assert.Equal(objects, Events().Objects.Count);
        }
        finally { Restore(keep); }
    }
}
