using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

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
        string[] paths = [game.EventsPath, game.ScriptsPath, game.TextArchivePath];
        var saved = paths.Select(File.ReadAllBytes).ToList();
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

            var script = new ScriptFile(Narc.Read(File.ReadAllBytes(game.ScriptsPath)).Files[header.ScriptsBank], ScriptCommandTable.Hgss);
            for (int id = 1; id <= oldScript.EntryOffsets.Count; id++)
                Assert.Equal(oldScript.Routine(id)!.Instructions.Select(i => i.Name), script.Routine(id)!.Instructions.Select(i => i.Name));
        }
        finally
        {
            for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(paths[i], saved[i]);
        }
    }
}
