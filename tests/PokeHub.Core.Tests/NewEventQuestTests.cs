using PokeHub.Core.Batch;
using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

[Collection("Eventos no workspace real")] // gravam no mesmo workspace: em paralelo, um apaga o que o outro criou
public class NewEventQuestTests
{
    private static readonly ScriptCommandTable Table = ScriptCommandTable.Hgss;

    private static List<ScriptInstruction> Decode(byte[] code)
    {
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        return new ScriptFile(file, Table).Routine(1)!.Instructions.OrderBy(i => i.Offset).ToList();
    }

    private static List<string> Names(byte[] code) => Decode(code).Select(i => i.Name).ToList();

    private static QuestBehavior ItemQuest(int? rewardSpecies = null) => new(50, 2, true, null, rewardSpecies is null ? 17 : null, 1, rewardSpecies, 12, 0, "a", "b", "c", "d", "e");

    [Fact]
    public void Quest_checks_everything_before_taking_the_item()
    {
        var names = Names(NewEventService.BuildScript(Table, ItemQuest(), [0, 1, 2, 3, 4], 0x900));
        int has = names.IndexOf("HasItem"), space = names.IndexOf("HasSpaceForItem"), take = names.IndexOf("TakeItem"), give = names.IndexOf("CallStd");
        Assert.True(has >= 0 && space > has && take > space && give > take, string.Join(",", names));
        Assert.True(names.IndexOf("SetFlag") < give);
    }

    [Fact]
    public void Enigma_stone_reward_also_writes_the_mark_the_latias_event_reads()
    {
        var quest = new QuestBehavior(50, 1, true, null, 536, 1, null, 12, 0, "a", "b", "c", "d", "e");
        var ins = Decode(NewEventService.BuildScript(Table, quest, [0, 1, 2, 3, 4], 0x900));
        var mark = ins.Single(i => i.Name == "SetVar" && i.Args[0].Value == 0x4043);
        Assert.Equal(0x06F2, mark.Args[1].Value);
        Assert.True(ins.IndexOf(mark) < ins.FindIndex(i => i.Name == "CallStd"));
        Assert.DoesNotContain(Decode(NewEventService.BuildScript(Table, ItemQuest(), [0, 1, 2, 3, 4], 0x900)), i => i.Name == "SetVar" && i.Args[0].Value == 0x4043);
    }

    [Fact]
    public void Quest_with_pokemon_reward_checks_the_party_and_gives_with_fanfare()
    {
        var ins = Decode(NewEventService.BuildScript(Table, ItemQuest(rewardSpecies: 133), [0, 1, 2, 3, 4], 0x900));
        var names = ins.Select(i => i.Name).ToList();
        Assert.True(names.IndexOf("GetPartyCount") < names.IndexOf("TakeItem"));
        Assert.True(names.IndexOf("TakeItem") < names.IndexOf("GiveMon"));
        var give = ins.First(i => i.Name == "GiveMon");
        Assert.Equal(133, give.Args[0].Value);
        Assert.Equal(12, give.Args[1].Value);
        Assert.Contains("PlayFanfare", names);
    }

    [Fact]
    public void Quest_showing_a_pokemon_does_not_take_anything()
    {
        var quest = new QuestBehavior(null, 1, false, 25, null, 1, null, 1, 500, "a", "b", "c", "d", "e");
        var ins = Decode(NewEventService.BuildScript(Table, quest, [0, 1, 2, 3, 4], 0x900));
        Assert.Equal(25, ins.First(i => i.Name == "PlayerHasSpecies").Args[1].Value);
        Assert.DoesNotContain(ins, i => i.Name == "TakeItem");
        Assert.Equal(500, ins.First(i => i.Name == "AddMoney").Args[0].Value);
    }

    [Fact]
    public void Sale_charges_only_after_money_and_party_checks()
    {
        var ins = Decode(NewEventService.BuildScript(Table, new SellBehavior(133, 15, 9000, true, "a", "b", "c", "d", "e", "f"), [0, 1, 2, 3, 4, 5], 0x901));
        var names = ins.Select(i => i.Name).ToList();
        int money = names.IndexOf("HasEnoughMoneyImmediate"), party = names.IndexOf("GetPartyCount"), charge = names.IndexOf("SubMoneyImmediate"), give = names.IndexOf("GiveMon");
        Assert.True(money >= 0 && party > money && charge > party && give > charge, string.Join(",", names));
        Assert.Equal(9000, ins.First(i => i.Name == "SubMoneyImmediate").Args[0].Value);
        Assert.Equal(names.Count(n => n == "ShowMoneyBox"), 1);
        // todo caminho depois da caixa de dinheiro a fecha: cada fim tem HideMoneyBox, menos o da oferta única esgotada, que fala antes de abrir
        Assert.Equal(4, names.Count(n => n == "HideMoneyBox"));
        Assert.Equal(5, names.Count(n => n == "End"));
    }

    [Fact]
    public void Repeatable_sale_has_no_flag()
    {
        var names = Names(NewEventService.BuildScript(Table, new SellBehavior(133, 15, 9000, false, "a", "b", "c", "d", "e", "f"), [0, 1, 2, 3, 4, 5], null));
        Assert.DoesNotContain("CheckFlag", names);
        Assert.DoesNotContain("SetFlag", names);
    }

    [Fact]
    public void Trainer_uses_its_own_flag_and_whites_out_on_defeat()
    {
        var ins = Decode(NewEventService.BuildScript(Table, new TrainerBehavior(200, "a", "b", "c"), [0, 1, 2], 0x902, objectId: 9));
        var names = ins.Select(i => i.Name).ToList();
        Assert.Equal(200, ins.First(i => i.Name == "TrainerBattle").Args[0].Value);
        Assert.Equal(0x902, ins.First(i => i.Name == "SetFlag").Args[0].Value);
        Assert.Contains("WhiteOut", names);
        Assert.DoesNotContain("SetTrainerFlag", names);
        Assert.DoesNotContain("HidePerson", names);
    }

    [Fact]
    public void Vanishing_trainer_hides_itself()
    {
        var ins = Decode(NewEventService.BuildScript(Table, new TrainerBehavior(200, "a", "b", "c", Vanish: true), [0, 1, 2], 0x902, objectId: 9));
        Assert.Equal(9, ins.First(i => i.Name == "HidePerson").Args[0].Value);
    }

    [Fact]
    public void Rematch_after_elite_checks_the_game_clear_flag_and_can_use_another_party()
    {
        var t = new TrainerBehavior(200, "a", "b", "c", Rematch: TrainerRematch.AfterElite, RematchTrainer: 300, RematchText: "d");
        var ins = Decode(NewEventService.BuildScript(Table, t, [0, 1, 2, 3], 0x902));
        Assert.Contains(ins, i => i.Name == "CheckFlag" && i.Args[0].Value == NewEventService.GameClearFlag);
        Assert.Equal([200L, 300L], ins.Where(i => i.Name == "TrainerBattle").Select(i => i.Args[0].Value));
        Assert.Contains(ins, i => i.Name == "GetMenuChoice");
    }

    [Fact]
    public void Condition_checks_come_first_and_lead_to_the_locked_line()
    {
        var condition = new NpcCondition("x", AfterElite: true, MinBadges: 8, Badge: 3, AfterFlag: 0x901);
        var ins = Decode(NewEventService.BuildScript(Table, new TalkBehavior("t"), [0, 7], null, condition: condition));
        var names = ins.Select(i => i.Name).ToList();
        Assert.Equal(["PlaySE", "LockAll", "FacePlayer", "CheckFlag", "GoToIf", "CheckFlag", "GoToIf", "CheckBadge", "CompareVarToValue", "GoToIf", "CountBadges", "CompareVarToValue", "GoToIf"], names.Take(13));
        Assert.Equal(0x964, ins[3].Args[0].Value);
        Assert.Equal(0x901, ins[5].Args[0].Value);
        Assert.Equal(3, ins[7].Args[0].Value);
        Assert.Equal(8, ins[11].Args[1].Value);
        Assert.Equal(7, ins.Last(i => i.Name == "NPCMsg").Args[0].Value);
    }

    [Fact]
    public void Scripts_without_condition_keep_the_exact_old_bytes_for_talk()
    {
        var names = Names(NewEventService.BuildScript(Table, new TalkBehavior("t"), [0], null));
        Assert.Equal(["ScrCmd_609", "LockAll", "FacePlayer", "NPCMsg", "WaitButton", "CloseMsg", "ReleaseAll", "End"], names);
    }

    [Fact]
    public void Trainer_script_number_follows_the_games_rule() => Assert.Equal(3280, NewEventService.TrainerScriptOf(281));

    private static TrainerRepository Repo(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable)!;
        var dir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        return TrainerRepository.Load(new Project { Paths = new ProjectPaths(dir), Config = ProjectConfig.Load(Path.Combine(dir, "project.json")) });
    }

    [Fact]
    public void Batch_turns_ai_flags_on_and_off_and_sets_battle_items_with_the_same_preview()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_TABLES_SS");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var repo = Repo("POKEHUB_TABLES_SS");
        var target = repo.Trainers.First(t => t.Id == 1);
        uint before = target.AiFlags;
        var options = new TrainerBatchOptions { NameFilter = repo.NameOf(target), AiFlagsOn = 1u << 2, AiFlagsOff = 1u << 10, BattleItems = [26, 26, 0, 0] };
        var preview = BatchEditor.PreviewTrainers(repo, options);
        Assert.NotEmpty(preview);
        Assert.Equal(before, target.AiFlags);
        Assert.Equal(preview, BatchEditor.ApplyTrainers(repo, options));
        Assert.True((target.AiFlags & 1u << 2) != 0);
        Assert.Equal(0u, target.AiFlags & 1u << 10);
        Assert.Equal(new ushort[] { 26, 26, 0, 0 }, target.Items);
    }

    /// <summary>Na cópia descartável: cria todos os modelos novos, relê e devolve os arquivos como estavam.</summary>
    [Theory]
    [InlineData("POKEHUB_TABLES_SCRATCH")]
    [InlineData("POKEHUB_TABLES_SCRATCH_LEGACY")]
    public void Real_create_edit_and_remove_new_kinds(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var game = Gen4Game.For(project);
        string[] paths = [game.EventsPath, game.ScriptsPath, game.TextArchivePath, game.TrainerDataPath, game.TrainerPartyPath];
        var saved = paths.Select(File.ReadAllBytes).ToList();
        var ledgerPath = Path.Combine(ws, "pokehub-events.json");
        var savedLedger = File.Exists(ledgerPath) ? File.ReadAllText(ledgerPath) : null;
        try
        {
            var map = EventMap.Load(project);
            var header = map.Headers.First(h => h.InternalName == "R29");
            var at = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]).Objects[0];
            NewNpc Npc(int dx, NpcBehavior b, NpcCondition? c = null) => new(header.Id, at.X + dx, at.Z, at.SpriteId, 1, b, Condition: c);

            var quest = NewEventService.Create(project, map, Npc(1, ItemQuest()));
            var chained = NewEventService.Create(project, EventMap.Load(project), Npc(2, new QuestBehavior(null, 1, false, 25, null, 1, 133, 5, 0, "a", "b", "c", "d", "e"), new NpcCondition("cedo", AfterFlag: quest.Flag)));
            var sale = NewEventService.Create(project, EventMap.Load(project), Npc(3, new SellBehavior(133, 15, 9000, true, "a", "b", "c", "d", "e", "f")));
            var trainer = NewEventService.Create(project, EventMap.Load(project), Npc(4, new TrainerBehavior(200, "a", "b", "c", Vanish: true)));
            var seeing = NewEventService.Create(project, EventMap.Load(project), Npc(5, new TrainerBehavior(201, "", "", "", Sight: 4)));
            Assert.Equal(NewEventService.TrainerScriptOf(201), seeing.ScriptId);
            Assert.Throws<ArgumentException>(() => NewEventService.Create(project, EventMap.Load(project), Npc(6, new TrainerBehavior(99999, "a", "b", "c"))));
            Assert.Throws<ArgumentException>(() => NewEventService.Create(project, EventMap.Load(project), Npc(6, new TalkBehavior("x"), new NpcCondition(""))));

            var events = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]);
            var seeingObject = events.Objects.First(o => o.ScriptId == seeing.ScriptId);
            Assert.Equal(1, seeingObject.Type);
            Assert.Equal(4, seeingObject.Sight);
            var vanishing = events.Objects.First(o => o.ScriptId == trainer.ScriptId);
            Assert.Equal(trainer.Flag, vanishing.EventFlag);
            Assert.Equal(new[] { 0, 0 }, new[] { events.Objects[0].Type & 0, 0 });

            var ledger = NewEventLedger.Load(project);
            var chainedNpc = ledger.Npcs.First(n => n.ScriptId == chained.ScriptId);
            Assert.Equal("cedo", chainedNpc.ConditionText);
            Assert.Equal(quest.Flag, chainedNpc.ConditionAfterFlag);
            Assert.IsType<QuestBehavior>(chainedNpc.ToBehavior());

            // editar mantém script e flag, e a condição continua lá
            var updated = NewEventService.Update(project, EventMap.Load(project), chainedNpc, chainedNpc.ToNewNpc());
            Assert.Equal(chained.ScriptId, updated.ScriptId);
            Assert.Equal(chained.Flag, updated.Flag);

            // tudo relê sem erro, e os NPCs originais continuam iguais
            var reread = EventMap.Load(project);
            Assert.Contains(reread.Events, e => e.MapId == header.Id && e.ScriptId == sale.ScriptId);

            // remover o de visão não tenta esvaziar o script padrão
            NewEventService.Remove(project, EventMap.Load(project), NewEventLedger.Load(project).Npcs.First(n => n.ScriptId == seeing.ScriptId));
            Assert.DoesNotContain(new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]).Objects, o => o.ScriptId == seeing.ScriptId);
            NewEventService.Remove(project, EventMap.Load(project), NewEventLedger.Load(project).Npcs.First(n => n.ScriptId == sale.ScriptId));

            // treinador novo, copiado
            var repo = TrainerRepository.Load(project);
            int count = repo.Trainers.Count;
            var copy = repo.AddCopy(repo.Trainers[200], "NOVO");
            repo.Save();
            var again = TrainerRepository.Load(project);
            Assert.Equal(count + 1, again.Trainers.Count);
            Assert.Equal("NOVO", again.NameOf(again.Trainers[copy.Id]));
            Assert.Equal(again.Trainers[200].Party.Select(p => p.Species), again.Trainers[copy.Id].Party.Select(p => p.Species));
            Assert.Equal(repo.Names[199], again.Names[199]);
            NewEventService.Create(project, EventMap.Load(project), Npc(7, new TrainerBehavior(copy.Id, "a", "b", "c")));
        }
        finally
        {
            for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], saved[i]);
            if (savedLedger is null) File.Delete(ledgerPath);
            else File.WriteAllText(ledgerPath, savedLedger);
        }
    }
}
