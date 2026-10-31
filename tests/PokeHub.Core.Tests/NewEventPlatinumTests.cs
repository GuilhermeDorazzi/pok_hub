using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class NewEventPlatinumTests
{
    private static List<string> Commands(byte[] code, ScriptCommandTable table)
    {
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        return new ScriptFile(file, table).Routine(1)!.Instructions.OrderBy(i => i.Offset).Select(i => i.Name).ToList();
    }

    [Fact]
    public void Talk_and_item_match_the_games_own_npcs()
    {
        var table = ScriptCommandTable.Platinum;
        Assert.Equal(["PlaySE", "LockAll", "FacePlayer", "Message", "WaitButton", "CloseMessage", "ReleaseAll", "End"],
            Commands(NewEventService.BuildScript(table, new TalkBehavior("x"), [7], null), table));
        Assert.Equal(["PlaySE", "LockAll", "FacePlayer", "CheckFlag", "GoToIf", "Message", "SetVarFromValue", "SetVarFromValue", "CanFitItem", "CompareVarToValue", "GoToIf", "CallCommonScript", "SetFlag",
            "Message", "WaitButton", "CloseMessage", "ReleaseAll", "End", "CallCommonScript", "CloseMessage", "ReleaseAll", "End"],
            Commands(NewEventService.BuildScript(table, new GiveItemBehavior(217, 1, "a", "b"), [3, 4], 0x95F), table));
    }

    [Fact]
    public void Pokemon_and_trade_match_the_games_own_scripts()
    {
        var table = ScriptCommandTable.Platinum;
        Assert.Equal(["PlaySE", "LockAll", "FacePlayer", "PlayCry", "WaitCry", "StartWildBattle", "CheckWonBattle", "CompareVarToValue", "GoToIf", "SetFlag", "RemoveObject", "ReleaseAll", "End", "BlackOutFromBattle", "ReleaseAll", "End"],
            Commands(NewEventService.BuildScript(table, new PokemonBehavior(25, 10, true, null), [], 0x95F, 3), table));
        var trade = Commands(NewEventService.BuildScript(table, new TradeBehavior(16, 1, null, "a", "b", "c", "d"), [1, 2, 3, 4], 0x95F), table);
        Assert.Contains("ShowYesNoMenu", trade);
        Assert.Contains("OpenPartyMenuForTrade", trade);
        Assert.Contains("InitNPCTrade", trade);
        Assert.Contains("StartNPCTrade", trade);
    }

    [Fact]
    public void Message_index_that_does_not_fit_is_refused()
    {
        Assert.Throws<ArgumentException>(() => NewEventService.BuildScript(ScriptCommandTable.Hgss, new TalkBehavior("x"), [300], null));
    }

    /// <summary>Na cópia descartável: cria os modelos no Platinum, relê tudo e devolve os arquivos como estavam.</summary>
    [Fact]
    public void Real_create_on_platinum()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_TABLES_SCRATCH_PT");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var game = Gen4Game.For(project);
        string[] paths = [game.EventsPath, game.ScriptsPath, game.TextArchivePath];
        var saved = paths.Select(File.ReadAllBytes).ToList();
        try
        {
            var map = EventMap.Load(project);
            var sprites = map.Sprites!;
            foreach (var species in new[] { 25, 35, 39, 54, 129, 396, 387, 390, 393, 483, 484 })
                Assert.NotNull(sprites.Render(NewEventService.SpriteForSpecies(species, platinum: true)!.Value));
            Assert.Null(NewEventService.SpriteForSpecies(1, platinum: true)); // Bulbasaur não tem sprite de campo aqui
            Assert.Empty(NewEventService.FreeTradeSlots(game));

            var header = map.Headers.First(h => h.InternalName == "C01");
            var before = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]);
            var at = before.Objects[0];
            var talk = NewEventService.Create(project, map, new NewNpc(header.Id, at.X + 1, at.Z, at.SpriteId, 1, new TalkBehavior("Fala de teste")));
            var item = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 2, at.Z, at.SpriteId, 1, new GiveItemBehavior(50, 1, "Toma", "Volte sempre")));
            Assert.InRange(item.Flag!.Value, 0x8F0, 0x95F);
            var mon = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 3, at.Z, 0, 1, new PokemonBehavior(25, 7, true, null), NpcMovement.LookAround));
            Assert.Throws<ArgumentException>(() => NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 4, at.Z, 0, 1, new PokemonBehavior(1, 7, true, null))));
            Assert.Throws<InvalidDataException>(() => NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 4, at.Z, 0, 1, new TradeBehavior(16, 1, null, "a", "b", "c", "d"))));

            var after = EventMap.Load(project);
            var created = after.Events.Where(e => e.MapId == header.Id && e.Kind == EventKind.Npc).ToList();
            Assert.Equal(before.Objects.Count + 3, created.Count);
            Assert.Contains(created[^2].Messages, m => m.Contains("Toma"));
            Assert.Contains(created[^2].Flags, f => f.Id == item.Flag);
            var finalObject = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]).Objects[mon.ObjectIndex];
            Assert.Equal(NewEventService.SpriteForSpecies(25, platinum: true), finalObject.SpriteId);
            Assert.Equal(2, finalObject.Movement);
            Assert.True(talk.ScriptId > 0);
        }
        finally
        {
            for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], saved[i]);
        }
    }
}
