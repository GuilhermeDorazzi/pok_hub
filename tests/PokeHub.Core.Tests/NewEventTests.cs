using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

[Collection("Eventos no workspace real")] // gravam no mesmo workspace: em paralelo, um apaga o que o outro criou
public class NewEventTests
{
    [Fact]
    public void Script_builder_resolves_labels_like_the_game()
    {
        var table = ScriptCommandTable.Hgss;
        var code = new ScriptBuilder(table).Emit("GoToIf", 1, "end").Emit("LockAll").Label("end").Emit("End").Build();
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        var script = new ScriptFile(file, table);
        var first = script.Routine(1)!.Instructions.First();
        Assert.Equal("GoToIf", first.Name);
        var target = (int)first.Arg("script")!.Value;
        Assert.Equal("End", script.RoutineAt(target).Instructions[0].Name);
    }

    [Fact]
    public void Add_entry_keeps_old_scripts_and_reaches_the_new_one()
    {
        var table = ScriptCommandTable.Hgss;
        // dois scripts, o primeiro com salto para o segundo
        var first = new ScriptBuilder(table).Emit("LockAll").Emit("End").Build();
        var second = new ScriptBuilder(table).Emit("ReleaseAll").Emit("End").Build();
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(6));
        ms.Write(BitConverter.GetBytes(6 + first.Length - 4));
        ms.Write(BitConverter.GetBytes((ushort)0xFD13));
        ms.Write(first);
        ms.Write(second);
        var editor = new ScriptEditor(ms.ToArray(), table);
        Assert.Equal(2, editor.File.EntryOffsets.Count);

        var code = BuildTalk(table);
        int id = editor.AddEntry(code);

        Assert.Equal(3, id);
        Assert.Equal(3, editor.File.EntryOffsets.Count);
        Assert.Equal("LockAll", editor.File.Routine(1)!.Instructions[0].Name);
        Assert.Equal("ReleaseAll", editor.File.Routine(2)!.Instructions[0].Name);
        Assert.Equal(["ScrCmd_609", "LockAll", "FacePlayer", "NPCMsg", "WaitButton", "CloseMsg", "ReleaseAll", "End"], editor.File.Routine(3)!.Instructions.Select(i => i.Name));
    }

    private static byte[] BuildTalk(ScriptCommandTable table) => NewEventService.BuildScript(table, new TalkBehavior("x"), [7], null);

    [Fact]
    public void Give_item_script_matches_the_games_own_npcs()
    {
        var table = ScriptCommandTable.Hgss;
        var code = NewEventService.BuildScript(table, new GiveItemBehavior(92, 2, "a", "b"), [3, 4], 0x95F);
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        var script = new ScriptFile(file, table).Routine(1)!;
        var names = script.Instructions.OrderBy(i => i.Offset).Select(i => i.Name).ToList();
        Assert.Equal(["ScrCmd_609", "LockAll", "FacePlayer", "CheckFlag", "GoToIf", "NPCMsg", "SetVar", "SetVar", "HasSpaceForItem", "CompareVarToValue", "GoToIf", "CallStd", "SetFlag",
            "NPCMsg", "WaitButton", "CloseMsg", "ReleaseAll", "End", "CallStd", "CloseMsg", "ReleaseAll", "End"], names);
    }

    [Fact]
    public void Pokemon_script_matches_the_games_static_encounters()
    {
        var table = ScriptCommandTable.Hgss;
        var code = NewEventService.BuildScript(table, new PokemonBehavior(25, 10, true, null), [], 0x95F, 12, engageFlag: 0x95E);
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        var script = new ScriptFile(file, table).Routine(1)!;
        var ordered = script.Instructions.OrderBy(i => i.Offset).ToList();
        Assert.Equal(["PlaySE", "LockAll", "FacePlayer", "PlayCry", "WaitCry", "PartyCountNotEgg", "CompareVarToValue", "GoToIf", "SetFlag", "WildBattle", "ClearFlag", "CheckBattleWon", "CompareVarToValue", "GoToIf", "GetStaticEncounterOutcome", "CompareVarToValue", "GoToIf", "ReleaseAll", "End", "SetFlag", "ReleaseAll", "End", "WhiteOut", "ReleaseAll", "End", "ReleaseAll", "End"], ordered.Select(i => i.Name));
        // só a captura resolve: nocauteado, volta quando o mapa recarregar
        Assert.Equal(4, ordered.SkipWhile(i => i.Name != "GetStaticEncounterOutcome").First(i => i.Name == "CompareVarToValue").Args[1].Value);
        var battle = ordered.First(i => i.Name == "WildBattle");
        Assert.Equal(25, battle.Args[0].Value);
        Assert.Equal(10, battle.Args[1].Value);

        // sem sumir, não usa flag nem esconde
        var stays = NewEventService.BuildScript(table, new PokemonBehavior(25, 10, false, null), [], null);
        Assert.True(stays.Length < code.Length);
    }

    [Fact]
    public void Blocking_pokemon_only_leaves_when_beaten_or_caught()
    {
        var table = ScriptCommandTable.Hgss;
        var code = NewEventService.BuildScript(table, new PokemonBehavior(143, 45, true, null, Blocks: true), [], 0x95F, 12);
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        var ordered = new ScriptFile(file, table).Routine(1)!.Instructions.OrderBy(i => i.Offset).ToList();
        var names = ordered.Select(i => i.Name).ToList();
        // sem flag de encontro: a tela de volta não esconde nada, e fugir deixa o caminho fechado
        Assert.Equal(1, names.Count(n => n == "SetFlag"));
        Assert.DoesNotContain("ClearFlag", names);
        int outcome = names.IndexOf("GetStaticEncounterOutcome");
        Assert.Equal([1L, 4L], ordered.Skip(outcome).Where(i => i.Name == "CompareVarToValue").Take(2).Select(i => (long)i.Args[1].Value));
        int hide = names.IndexOf("HidePerson");
        Assert.True(hide > names.IndexOf("SetFlag"));
        Assert.Equal(12, ordered[hide].Args[0].Value);
    }

    [Theory]
    [InlineData(13, 26, 117)]
    [InlineData(4, 8, 124)]
    public void New_trade_slots_never_share_a_name_with_another_slot(int original, int first, int count)
    {
        var slots = NewEventService.ExtraTradeSlots(original).ToList();
        Assert.Equal(first, slots[0]);
        Assert.Equal(count, slots.Count);
        Assert.True(slots.Max() <= 255);
        var used = new HashSet<int>(Enumerable.Range(0, original * 2));
        foreach (var slot in slots)
        {
            Assert.True(used.Add(slot), $"apelido da vaga {slot}");
            Assert.True(used.Add(slot + original), $"treinador da vaga {slot}");
        }
    }

    [Fact]
    public void Trade_script_matches_the_games_own_npcs()
    {
        var table = ScriptCommandTable.Hgss;
        var code = NewEventService.BuildScript(table, new TradeBehavior(16, 1, null, "a", "b", "c", "d"), [1, 2, 3, 4], 0x95F, 0, 7);
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        var ordered = new ScriptFile(file, table).Routine(1)!.Instructions.OrderBy(i => i.Offset).ToList();
        Assert.Equal(7, ordered.First(i => i.Name == "LoadNPCTrade").Args[0].Value);
        Assert.Equal(["PlaySE", "LockAll", "FacePlayer", "CheckFlag", "GoToIf", "NPCMsg", "TouchscreenMenuHide", "GetMenuChoice", "TouchscreenMenuShow", "CompareVarToValue", "GoToIf", "GoTo",
            "CloseMsg", "FadeScreen", "WaitFade", "ScrCmd_566", "GetPartySelection", "RestoreOverworld", "FadeScreen", "WaitFade", "CompareVarToValue", "GoToIf",
            "LoadNPCTrade", "CopyVar", "GetPartyMonSpecies", "NPCTradeGetReqSpecies", "CompareVarToVar", "GoToIf", "NPCTradeExec", "NPCTradeEnd", "SetFlag",
            "NPCMsg", "WaitButton", "CloseMsg", "ReleaseAll", "End",
            "NPCTradeEnd", "NPCMsg", "WaitButton", "CloseMsg", "ReleaseAll", "End",
            "NPCMsg", "WaitButton", "CloseMsg", "ReleaseAll", "End"], ordered.Select(i => i.Name));
    }

    [Fact]
    public void Every_species_has_a_field_sprite()
    {
        for (int species = 1; species <= 493; species++) Assert.NotNull(NewEventService.SpriteForSpecies(species));
        Assert.Null(NewEventService.SpriteForSpecies(494));
    }

    [Fact]
    public void Header_entry_is_added_keeping_the_trigger_table_reachable()
    {
        // tipo 1 aponta para a tabela de gatilhos, logo depois do fim da lista
        var raw = new byte[] { 1, 1, 0, 0, 0, 0, 0x41, 0x40, 1, 0, 9, 0, 0, 0 };
        var parsedBefore = new MapScriptHeader(raw);
        var added = MapScriptHeader.SetEntry(raw, 4, 7);
        var parsed = new MapScriptHeader(added);
        Assert.Contains(parsed.Entries, e => e.Kind == 4 && e.A == 7);
        Assert.Equal(parsedBefore.Triggers, parsed.Triggers);
        // de novo no mesmo tipo: só troca o número, sem crescer
        var again = MapScriptHeader.SetEntry(added, 4, 8);
        Assert.Equal(added.Length, again.Length);
        Assert.Contains(new MapScriptHeader(again).Entries, e => e.Kind == 4 && e.A == 8);
    }

    [Fact]
    public void Replace_entry_keeps_the_number_and_the_other_scripts()
    {
        var table = ScriptCommandTable.Hgss;
        var first = new ScriptBuilder(table).Emit("LockAll").Emit("End").Build();
        var second = new ScriptBuilder(table).Emit("ReleaseAll").Emit("End").Build();
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(6));
        ms.Write(BitConverter.GetBytes(6 + first.Length - 4));
        ms.Write(BitConverter.GetBytes((ushort)0xFD13));
        ms.Write(first);
        ms.Write(second);
        var editor = new ScriptEditor(ms.ToArray(), table);
        editor.ReplaceEntry(1, new ScriptBuilder(table).Emit("FacePlayer").Emit("End").Build());
        Assert.Equal(2, editor.File.EntryOffsets.Count);
        Assert.Equal("FacePlayer", editor.File.Routine(1)!.Instructions[0].Name);
        Assert.Equal("ReleaseAll", editor.File.Routine(2)!.Instructions[0].Name);
    }

    [Fact]
    public void Objects_can_be_replaced_and_removed_by_number()
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(0));
        var raw = EventFile.AddObject(ms.ToArray(), new ObjectEvent(0, 5, 42, 15, 0, 0, 3, 1, 10, 20));
        raw = EventFile.AddObject(raw, new ObjectEvent(1, 6, 43, 15, 0, 0, 4, 1, 11, 21));
        raw = EventFile.ReplaceObject(raw, new ObjectEvent(1, 6, 44, 2, 0, 0, 4, 1, 12, 22));
        var read = new EventFile(raw);
        Assert.Equal(44, read.Objects[1].SpriteId);
        Assert.Equal(12, read.Objects[1].X);
        raw = EventFile.RemoveObject(raw, 5);
        var o = Assert.Single(new EventFile(raw).Objects);
        Assert.Equal(6, o.Id);
    }

    /// <summary>Na cópia descartável: edita e remove, e confere texto, script, flags, vaga de troca e script de retorno.</summary>
    [Fact]
    public void Real_edit_and_remove_created_npcs()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_TABLES_SCRATCH");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var game = Gen4Game.For(project);
        var ledgerPath = Path.Combine(ws, "pokehub-events.json");
        string[] paths = [game.EventsPath, game.ScriptsPath, game.TextArchivePath, Path.Combine(game.FilesRoot, @"a\1\1\2")];
        var saved = paths.Select(File.ReadAllBytes).ToList();
        var savedLedger = File.Exists(ledgerPath) ? File.ReadAllText(ledgerPath) : null;
        try
        {
            var map = EventMap.Load(project);
            var header = map.Headers.First(h => h.InternalName == "T20");
            var at = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]).Objects[0];
            CreatedNpc Last() => NewEventLedger.Load(project).Npcs[^1];
            Narc Scripts() => Narc.Read(File.ReadAllBytes(game.ScriptsPath));
            EventFile Events() => new(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]);

            // editar: mesma mensagem, mesmo script, posição nova
            var talk = NewEventService.Create(project, map, new NewNpc(header.Id, at.X + 1, at.Z, at.SpriteId, 1, new TalkBehavior("Primeira")));
            var created = Last();
            NewEventService.Update(project, EventMap.Load(project), created, created.ToNewNpc() with { X = at.X + 6, Behavior = new TalkBehavior("Segunda") });
            var edited = Last();
            Assert.Equal(created.ScriptId, edited.ScriptId);
            Assert.Equal(created.Messages, edited.Messages);
            Assert.Equal(at.X + 6, Events().Objects.First(o => o.Id == edited.ObjectId).X);
            Assert.Equal("Segunda", TextRepository.Load(project).Bank(header.MsgBank).Texts[edited.Messages[0]]);

            // Pokémon que some: script de retorno no cabeçalho, e a continuação do que o mapa já tinha
            var oldHeader = new MapScriptHeader(Scripts().Files[header.ScriptHeaderBank]);
            var mon = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 2, at.Z, 0, 1, new PokemonBehavior(25, 5, true, null)));
            var pokemon = Last();
            Assert.NotNull(pokemon.LevelScriptId);
            Assert.NotEqual(pokemon.Flag, pokemon.EngageFlag);
            var newHeader = new MapScriptHeader(Scripts().Files[header.ScriptHeaderBank]);
            Assert.Contains(newHeader.Entries, e => e.Kind == 4 && e.A == pokemon.LevelScriptId);
            Assert.Equal(oldHeader.Triggers, newHeader.Triggers);
            var script = new ScriptFile(Scripts().Files[header.ScriptsBank], ScriptCommandTable.Hgss);
            Assert.Contains(script.Routine(pokemon.LevelScriptId!.Value)!.Instructions, i => i.Name == "HidePerson" && i.Args[0].Value == pokemon.ObjectId);

            // um segundo no mesmo mapa encadeia no primeiro
            NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 3, at.Z, 0, 1, new PokemonBehavior(54, 5, true, null)));
            var second = Last();
            Assert.Equal(pokemon.LevelScriptId, second.ChainScriptId);
            script = new ScriptFile(Scripts().Files[header.ScriptsBank], ScriptCommandTable.Hgss);
            var chained = script.Routine(second.LevelScriptId!.Value)!.Instructions;
            Assert.Contains(chained, i => i.Name == "HidePerson" && i.Args[0].Value == second.ObjectId);
            Assert.Contains(chained, i => i.Name == "HidePerson" && i.Args[0].Value == pokemon.ObjectId);

            // remover: some do mapa, a corrente continua e as flags não voltam
            NewEventService.Remove(project, EventMap.Load(project), pokemon);
            Assert.DoesNotContain(Events().Objects, o => o.Id == pokemon.ObjectId);
            Assert.DoesNotContain(NewEventLedger.Load(project).Npcs, n => n.ObjectId == pokemon.ObjectId);
            Assert.Contains(pokemon.Flag!.Value, NewEventLedger.Load(project).RetiredFlags);
            script = new ScriptFile(Scripts().Files[header.ScriptsBank], ScriptCommandTable.Hgss);
            Assert.Equal("End", script.Routine(pokemon.ScriptId)!.Instructions[0].Name);
            Assert.Contains(script.Routine(second.LevelScriptId!.Value)!.Instructions, i => i.Name == "HidePerson" && i.Args[0].Value == second.ObjectId);
            Assert.DoesNotContain(script.Routine(second.LevelScriptId!.Value)!.Instructions, i => i.Name == "HidePerson" && i.Args[0].Value == pokemon.ObjectId);
            var map2 = EventMap.Load(project);
            Assert.Null(map2.Events.FirstOrDefault(e => e.MapId == header.Id && e.ScriptId == pokemon.ScriptId && e.Kind == EventKind.Npc));
            var nextFlag = NewEventService.PickFreeFlag(map2, 738, NewEventLedger.Load(project).RetiredFlags.ToHashSet());
            Assert.NotEqual(pokemon.Flag, nextFlag);

            // troca removida devolve a vaga
            var freeBefore = NewEventService.FreeTradeSlots(game);
            NewEventService.Create(project, map2, new NewNpc(header.Id, at.X + 4, at.Z, at.SpriteId, 1, new TradeBehavior(16, 1, null, "a", "b", "c", "d")));
            var trade = Last();
            Assert.DoesNotContain(trade.TradeSlot!.Value, NewEventService.FreeTradeSlots(game));
            NewEventService.Remove(project, EventMap.Load(project), trade);
            Assert.Equal(freeBefore, NewEventService.FreeTradeSlots(game));
        }
        finally
        {
            for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], saved[i]);
            if (savedLedger is null) File.Delete(ledgerPath);
            else File.WriteAllText(ledgerPath, savedLedger);
        }
    }

    [Fact]
    public void Add_object_goes_at_the_end_of_the_object_list()
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(0));          // nenhuma placa
        ms.Write(BitConverter.GetBytes(0));          // nenhum NPC
        ms.Write(BitConverter.GetBytes(1));          // um warp
        ms.Write(new byte[12]);
        ms.Write(BitConverter.GetBytes(0));          // nenhum gatilho
        var raw = ms.ToArray();
        var added = EventFile.AddObject(raw, new ObjectEvent(0, 5, 42, 15, 0, 0, 3, 1, 10, 20, 0x1234000));
        var read = new EventFile(added);
        var o = Assert.Single(read.Objects);
        Assert.Equal(42, o.SpriteId);
        Assert.Equal(3, o.ScriptId);
        Assert.Equal(10, o.X);
        Assert.Equal(20, o.Z);
        Assert.Equal(0x1234000, o.Y);
        Assert.Single(read.Warps);
    }

    [Fact]
    public void Wrap_breaks_lines_inside_the_box_and_pages_every_two()
    {
        var wrapped = TextPreview.Wrap("um dois tres quatro cinco seis", (s) => s.Length * 40);
        Assert.Contains("\\n", wrapped);
        Assert.Contains("\\r", wrapped);
        Assert.DoesNotContain(" \\", wrapped);
        Assert.Equal("curto", TextPreview.Wrap("curto", s => s.Length));
    }

    /// <summary>Na cópia descartável: cria os dois modelos, relê tudo e devolve os arquivos como estavam.</summary>
    [Fact]
    public void Real_create_talk_and_item_npcs()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_TABLES_SCRATCH");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var game = Gen4Game.For(project);
        string[] paths = [game.EventsPath, game.ScriptsPath, game.TextArchivePath, Path.Combine(game.FilesRoot, @"a\1\1\2")];
        var saved = paths.Select(File.ReadAllBytes).ToList();
        var ledgerPath = Path.Combine(ws, "pokehub-events.json");
        var savedLedger = File.Exists(ledgerPath) ? File.ReadAllText(ledgerPath) : null;
        try
        {
            var map = EventMap.Load(project);
            var header = map.Headers.First(h => h.InternalName == "T20");
            var before = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]);
            var oldScript = new ScriptFile(Narc.Read(File.ReadAllBytes(game.ScriptsPath)).Files[header.ScriptsBank], ScriptCommandTable.Hgss);
            var at = before.Objects[0];

            var talk = NewEventService.Create(project, map, new NewNpc(header.Id, at.X + 1, at.Z, at.SpriteId, 1, new TalkBehavior("Fala de teste")));
            Assert.Equal(before.Objects.Count, talk.ObjectIndex);
            Assert.Equal(oldScript.EntryOffsets.Count + 1, talk.ScriptId);
            Assert.Null(talk.Flag);

            var map2 = EventMap.Load(project);
            var item = NewEventService.Create(project, map2, new NewNpc(header.Id, at.X + 2, at.Z, at.SpriteId, 1, new GiveItemBehavior(92, 1, "Toma", "Volte sempre")));
            Assert.NotNull(item.Flag);
            Assert.InRange(item.Flag!.Value, 0x822, 0x95F);

            var map3 = EventMap.Load(project);
            var entries = map3.Events.Where(e => e.MapId == header.Id && e.Kind == EventKind.Npc).ToList();
            Assert.Equal(before.Objects.Count + 2, entries.Count);
            var created = entries[^1];
            Assert.Contains(created.Messages, m => m.Contains("Toma"));
            Assert.Contains(created.Messages, m => m.Contains("Volte sempre"));
            Assert.Contains(created.Flags, f => f.Id == item.Flag && f.Action == "set");
            // a flag usada deixa de ser livre, e o próximo NPC recebe outra
            Assert.NotEqual(item.Flag, NewEventService.PickFreeFlag(map3, 738));

            var map4 = EventMap.Load(project);
            var mon = NewEventService.Create(project, map4, new NewNpc(header.Id, at.X + 3, at.Z, 0, 1, new PokemonBehavior(25, 7, true, "Pika!")));
            Assert.NotNull(mon.Flag);
            var finalEvents = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]);
            var pikachu = finalEvents.Objects[^1];
            Assert.Equal(NewEventService.SpriteForSpecies(25), pikachu.SpriteId);
            Assert.Equal(mon.Flag, pikachu.EventFlag);
            Assert.NotNull(map4.Sprites!.Render(pikachu.SpriteId)); // o sprite existe na tabela do jogo
            for (int species = 1; species <= 493; species++)
                Assert.NotNull(map4.Sprites.Render(NewEventService.SpriteForSpecies(species)!.Value));

            // movimento: andar usa alcance, parado não
            var walker = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 4, at.Z, at.SpriteId, 1, new TalkBehavior("Ando"), NpcMovement.Wander, 2));
            var walkerObject = new EventFile(Narc.Read(File.ReadAllBytes(game.EventsPath)).Files[header.EventsBank]).Objects[walker.ObjectIndex];
            Assert.Equal(3, walkerObject.Movement);
            Assert.Equal(2, walkerObject.XRange);
            Assert.Equal(2, walkerObject.ZRange);

            // troca: usa uma vaga que nenhum script carrega, grava as espécies e o apelido
            var freeBefore = NewEventService.FreeTradeSlots(game);
            Assert.NotEmpty(freeBefore);
            Assert.DoesNotContain(6, freeBefore); // Shuckie
            Assert.DoesNotContain(7, freeBefore); // Kenya
            var names = TextRepository.Load(project);
            Assert.Equal("ROCKY", names.Bank(NewEventService.TradeNamesBank).Texts[0], ignoreCase: true); // apelido da vaga 0 no jogo
            var tradeResult = NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 5, at.Z, at.SpriteId, 1,
                new TradeBehavior(16, 1, "HUBBY", "Troca?", "Valeu!", "Esse nao!", "Tudo bem.")));
            var trades = Narc.Read(File.ReadAllBytes(Path.Combine(game.FilesRoot, @"a\1\1\2")));
            var record = trades.Files[freeBefore[0]];
            Assert.Equal(1, BitConverter.ToInt32(record, 0));
            Assert.Equal(16, BitConverter.ToInt32(record, 0x4C));
            Assert.Equal("HUBBY", TextRepository.Load(project).Bank(NewEventService.TradeNamesBank).Texts[freeBefore[0]]);
            Assert.DoesNotContain(freeBefore[0], NewEventService.FreeTradeSlots(game));

            // acabadas as livres do jogo, a próxima troca abre uma vaga nova sem mexer nas originais
            var originalNames = TextRepository.Load(project).Bank(NewEventService.TradeNamesBank).Texts.ToList();
            var originalRecords = Narc.Read(File.ReadAllBytes(NewEventService.TradesPathOf(game))).Files.Select(f => f.ToArray()).ToList();
            foreach (var _ in freeBefore.Where(slot => slot < 13).Skip(1))
                NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 6, at.Z, at.SpriteId, 1, new TradeBehavior(16, 1, null, "a", "b", "c", "d")));
            int newSlot = NewEventService.FreeTradeSlots(game)[0];
            Assert.Contains(newSlot, NewEventService.ExtraTradeSlots(13));
            NewEventService.Create(project, EventMap.Load(project), new NewNpc(header.Id, at.X + 7, at.Z, at.SpriteId, 1, new TradeBehavior(25, 133, "NOVA", "Troca?", "Valeu!", "Esse nao!", "Tudo bem.")));
            var grown = Narc.Read(File.ReadAllBytes(NewEventService.TradesPathOf(game)));
            Assert.Equal(newSlot + 1, grown.Files.Count);
            Assert.Equal(133, BitConverter.ToInt32(grown.Files[newSlot], 0));
            Assert.Equal(25, BitConverter.ToInt32(grown.Files[newSlot], 0x4C));
            Assert.Equal(0, BitConverter.ToInt32(grown.Files[newSlot], 60));
            for (int slot = 0; slot < 13; slot++)
                if (!freeBefore.Contains(slot)) Assert.Equal(originalRecords[slot], grown.Files[slot]);
            var grownNames = TextRepository.Load(project).Bank(NewEventService.TradeNamesBank).Texts;
            Assert.Equal(originalNames.Take(26).Where((_, i) => !freeBefore.Contains(i)), grownNames.Take(26).Where((_, i) => !freeBefore.Contains(i)));
            Assert.Equal("NOVA", grownNames[newSlot]);
            Assert.Equal(originalNames[13], grownNames[newSlot + 13]);
            var newScript = new ScriptFile(Narc.Read(File.ReadAllBytes(game.ScriptsPath)).Files[header.ScriptsBank], ScriptCommandTable.Hgss);
            Assert.Equal(newSlot, newScript.Routine(newScript.EntryOffsets.Count)!.Instructions.First(i => i.Name == "LoadNPCTrade").Args[0].Value);

            var script = new ScriptFile(Narc.Read(File.ReadAllBytes(game.ScriptsPath)).Files[header.ScriptsBank], ScriptCommandTable.Hgss);
            for (int id = 1; id <= oldScript.EntryOffsets.Count; id++)
                Assert.Equal(oldScript.Routine(id)!.Instructions.Select(i => i.Name), script.Routine(id)!.Instructions.Select(i => i.Name));
        }
        finally
        {
            for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], saved[i]);
            if (savedLedger is null) File.Delete(ledgerPath);
            else File.WriteAllText(ledgerPath, savedLedger);
        }
    }
}
