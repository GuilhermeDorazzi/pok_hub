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
        Assert.Equal(["PlaySE", "LockAll", "FacePlayer", "PlayCry", "WaitCry", "CountPartyNonEggs", "CompareVarToValue", "GoToIf", "SetFlag", "StartWildBattle", "ClearFlag", "CheckWonBattle", "CompareVarToValue", "GoToIf", "SetFlag", "ReleaseAll", "End", "BlackOutFromBattle", "ReleaseAll", "End", "ReleaseAll", "End"],
            Commands(NewEventService.BuildScript(table, new PokemonBehavior(25, 10, true, null), [], 0x95F, 3, engageFlag: 0x95E), table));
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
        string[] paths = [game.EventsPath, game.ScriptsPath, game.TextArchivePath, NewEventService.TradesPathOf(game)];
        var saved = paths.Select(File.ReadAllBytes).ToList();
        var ledgerPath = Path.Combine(ws, "pokehub-events.json");
        var savedLedger = File.Exists(ledgerPath) ? File.ReadAllText(ledgerPath) : null;
        try
        {
            var map = EventMap.Load(project);
            var sprites = map.Sprites!;
            foreach (var species in new[] { 25, 35, 39, 54, 129, 396, 387, 390, 393, 483, 484 })
                Assert.NotNull(sprites.Render(NewEventService.SpriteForSpecies(species, platinum: true)!.Value));
            Assert.Null(NewEventService.SpriteForSpecies(1, platinum: true)); // Bulbasaur não tem sprite de campo aqui
            Assert.Equal(8, NewEventService.FreeTradeSlots(game)[0]); // as 4 originais estão em uso

            var header = map.Headers.First(h => h.InternalName == "C01");
            var before = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]);
            var at = before.Objects[0];
            var talk = NewEventService.Create(project, map, new NewNpc(header.Id, at.X + 1, at.Z, at.SpriteId, 1, new TalkBehavior("Fala de teste")));
            var item = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 2, at.Z, at.SpriteId, 1, new GiveItemBehavior(50, 1, "Toma", "Volte sempre")));
            Assert.InRange(item.Flag!.Value, 0x8F0, 0x95F);
            var mon = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 3, at.Z, 0, 1, new PokemonBehavior(25, 7, true, null), NpcMovement.LookAround));
            Assert.Throws<ArgumentException>(() => NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 4, at.Z, 0, 1, new PokemonBehavior(1, 7, true, null))));

            var originalNames = TextRepository.Load(project).Bank(NewEventService.PlatinumTradeNamesBank).Texts.ToList();
            Assert.Equal("Kazza", originalNames[0], ignoreCase: true);
            NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 4, at.Z, 0, 1, new TradeBehavior(16, 1, "HUBBY", "Troca?", "Valeu!", "Esse nao!", "Tudo bem.")));
            var trades = Narc.Read(File.ReadAllBytes(NewEventService.TradesPathOf(game)));
            Assert.Equal(9, trades.Files.Count);
            Assert.Equal(1, BitConverter.ToInt32(trades.Files[8], 0));
            Assert.Equal(16, BitConverter.ToInt32(trades.Files[8], 0x4C));
            Assert.Equal(0, BitConverter.ToInt32(trades.Files[8], 60));
            var names = TextRepository.Load(project).Bank(NewEventService.PlatinumTradeNamesBank).Texts;
            Assert.Equal(originalNames, names.Take(originalNames.Count));
            Assert.Equal("HUBBY", names[8]);
            Assert.Equal(originalNames[4], names[12]);
            Assert.Equal(9, NewEventService.FreeTradeSlots(game)[0]);

            var after = EventMap.Load(project);
            var created = after.Events.Where(e => e.MapId == header.Id && e.Kind == EventKind.Npc).ToList();
            Assert.Equal(before.Objects.Count + 4, created.Count);
            Assert.Contains(created[^3].Messages, m => m.Contains("Toma"));
            Assert.Contains(created[^3].Flags, f => f.Id == item.Flag);
            var finalObject = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]).Objects[mon.ObjectIndex];
            Assert.Equal(NewEventService.SpriteForSpecies(25, platinum: true), finalObject.SpriteId);
            Assert.Equal(2, finalObject.Movement);
            Assert.True(talk.ScriptId > 0);
        }
        finally
        {
            for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], saved[i]);
            if (savedLedger is null) File.Delete(ledgerPath);
            else File.WriteAllText(ledgerPath, savedLedger);
        }
    }
}
