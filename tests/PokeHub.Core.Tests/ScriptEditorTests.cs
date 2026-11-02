using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class ScriptEditorTests
{
    private const int Flag = 0x1E0;

    /// <summary>O que o script faz, sem as posições: comando, valores e, nos saltos, o comando de destino.</summary>
    private static List<string> Behavior(ScriptEditor e, byte[] raw)
    {
        var list = e.Instructions();
        var at = list.ToDictionary(i => i.Offset);
        return list.Select(i => i.Name + ":" + string.Join(",", i.Args.Select(a => a.Type switch
        {
            "script" or "addr" => at.TryGetValue((int)a.Value, out var t) ? "->" + t.Name : "->?",
            "movement" => "mov:" + Convert.ToHexString(raw.AsSpan((int)a.Value, Math.Min(4, raw.Length - (int)a.Value))),
            _ => a.Value.ToString(),
        }))).ToList();
    }

    private static int Opcode(ScriptCommandTable t, string name) =>
        Enumerable.Range(0, t.Commands.Count).First(i => t.Commands[i].Name == name);

    private static byte[] Sample(ScriptCommandTable t)
    {
        // duas entradas: a 1 faz SetFlag, GoTo para o rótulo, e o rótulo termina; a 2 só termina
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(6)); // entrada 1 em 10, logo depois do fim da tabela
        ms.Write(BitConverter.GetBytes(14)); // entrada 2 em 22
        ms.Write(BitConverter.GetBytes((ushort)0xFD13));
        ms.Write(BitConverter.GetBytes((ushort)Opcode(t, "SetFlag")));
        ms.Write(BitConverter.GetBytes((ushort)Flag));
        ms.Write(BitConverter.GetBytes((ushort)Opcode(t, "GoTo")));
        ms.Write(BitConverter.GetBytes(0)); // salta para o comando seguinte
        ms.Write(BitConverter.GetBytes((ushort)Opcode(t, "End")));
        ms.Write(BitConverter.GetBytes((ushort)Opcode(t, "End")));
        return ms.ToArray();
    }

    [Fact]
    public void New_trainer_entries_point_to_the_talk_script_and_keep_the_old_ones()
    {
        var table = ScriptCommandTable.Hgss;
        var raw = Sample(table);
        var before = new ScriptEditor(raw.ToArray(), table);
        var oldBehavior = Behavior(before, before.Raw);

        var grown = new ScriptEditor(NewEventService.WithTrainerScript(raw, table, 6), table);
        Assert.Equal(6, grown.File.EntryOffsets.Count);
        // as entradas antigas continuam no mesmo código, e as novas caem na primeira
        Assert.Equal(oldBehavior, Behavior(grown, grown.Raw));
        Assert.All(grown.File.EntryOffsets.Skip(2), o => Assert.Equal(grown.File.EntryOffsets[0], o));
        Assert.Equal(before.File.Routine(2)!.Instructions.Select(i => i.Name), grown.File.Routine(2)!.Instructions.Select(i => i.Name));
        // já com entradas suficientes, nada muda
        Assert.Equal(grown.Raw, NewEventService.WithTrainerScript(grown.Raw, table, 4));
        Assert.Throws<ArgumentException>(() => NewEventService.WithTrainerScript(raw, table, NewEventService.HgssApproachTrainer));
    }

    [Fact]
    public void Trainer_talk_checks_the_trainer_itself_and_is_rewritten_in_place()
    {
        var table = ScriptCommandTable.Hgss;
        var raw = Sample(table);
        var once = new ScriptEditor(NewEventService.WithTrainerTalk(raw, table, 5, 7), table);
        Assert.Equal(5, once.File.EntryOffsets.Count);
        var talk = once.File.Routine(5)!.Instructions;
        Assert.Contains(talk, i => i.Name == "CheckTrainerFlag" && i.Args[0].Value == 5);
        Assert.Contains(talk, i => i.Name == "SetTrainerFlag" && i.Args[0].Value == 5);
        Assert.Equal(2, talk.Count(i => i.Name == "TrainerBattle" && i.Args[0].Value == 5));
        Assert.Contains(talk, i => i.Name == "NPCMsg" && i.Args[0].Value == 7);
        // nada da agenda do Pokégear
        Assert.DoesNotContain(talk, i => i.Name.Contains("Phone"));
        // as outras entradas novas continuam no script comum
        Assert.Equal(once.File.EntryOffsets[0], once.File.EntryOffsets[3]);
        // de novo, com outra pergunta: regrava no lugar, sem crescer
        var twice = NewEventService.WithTrainerTalk(once.Raw, table, 5, 8);
        Assert.Equal(once.Raw.Length, twice.Length);
        Assert.Contains(new ScriptEditor(twice, table).File.Routine(5)!.Instructions, i => i.Name == "NPCMsg" && i.Args[0].Value == 8);
    }

    [Fact]
    public void Insert_fixes_entries_and_jumps_and_remove_restores_the_file()
    {
        var table = ScriptCommandTable.Platinum;
        var raw = Sample(table);
        var e = new ScriptEditor(raw.ToArray(), table);
        var before = Behavior(e, e.Raw);
        Assert.Equal(2, e.File.EntryOffsets.Count);

        int first = e.File.EntryOffsets[0];
        e.Insert(first, Opcode(table, "SetFlag"), [99]);
        var after = Behavior(e, e.Raw);
        Assert.Equal(before.Count + 1, after.Count);
        Assert.Equal($"SetFlag:99", after[0]);
        Assert.Equal(before, after.Skip(1).ToList());
        Assert.Equal(first, e.File.EntryOffsets[0]); // a entrada passa a executar o novo comando primeiro

        e.Remove(first);
        Assert.Equal(raw, e.Raw);
    }

    [Fact]
    public void Inserted_jump_goes_to_the_chosen_command()
    {
        var table = ScriptCommandTable.Platinum;
        var e = new ScriptEditor(Sample(table), table);
        int entry2 = e.File.EntryOffsets[1];
        e.Insert(e.File.EntryOffsets[0], Opcode(table, "GoTo"), [entry2]);
        var go = e.Instructions()[0];
        Assert.Equal("GoTo", go.Name);
        Assert.Equal(e.File.EntryOffsets[1], go.Args[0].Value);
    }

    [Fact]
    public void Bad_arguments_are_refused_before_touching_the_file()
    {
        var table = ScriptCommandTable.Platinum;
        var raw = Sample(table);
        var e = new ScriptEditor(raw.ToArray(), table);
        int entry = e.File.EntryOffsets[0];
        Assert.Throws<ArgumentException>(() => e.Insert(entry, Opcode(table, "SetFlag"), [70000]));
        Assert.Throws<ArgumentException>(() => e.Insert(entry, Opcode(table, "SetFlag"), []));
        Assert.Throws<ArgumentException>(() => e.Insert(entry + 1, Opcode(table, "End"), []));
        Assert.Throws<ArgumentException>(() => e.Insert(entry, Opcode(table, "GoTo"), [999999]));
        Assert.Equal(raw, e.Raw);
    }

    /// <summary>Em todos os arquivos reais: inserir não muda o que o script faz, movimentos seguem alinhados e remover devolve o arquivo.</summary>
    [Theory]
    [InlineData("POKEHUB_TABLES_SS")]
    [InlineData("POKEHUB_TABLES_PT")]
    public void Real_every_file_keeps_its_behavior(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var game = Gen4Game.For(project);
        var table = game.IsHgss ? ScriptCommandTable.Hgss : ScriptCommandTable.Platinum;
        int flag = Opcode(table, "SetFlag"), end = Opcode(table, "End");
        var narc = Narc.Read(File.ReadAllBytes(game.ScriptsPath));
        int tested = 0;
        foreach (var file in narc.Files)
        {
            var e = new ScriptEditor(file.ToArray(), table);
            if (e.File.EntryOffsets.Count == 0) continue;
            var list = e.Instructions();
            if (list.Count == 0 || list.Any(i => i.Name.StartsWith('?'))) continue;
            var before = Behavior(e, e.Raw);
            // quem salta para o ponto de inserção executa o comando novo de propósito, então o teste usa um comando que não é destino
            var targets = list.SelectMany(i => i.Args).Where(a => a.Type is "script" or "addr").Select(a => (int)a.Value).Concat(e.File.EntryOffsets).ToHashSet();
            int index = list.FindIndex(list.Count / 2, i => !targets.Contains(i.Offset));
            if (index < 0) continue;
            var at = list[index].Offset;
            e.Insert(at, flag, [Flag]);
            var after = Behavior(e, e.Raw);
            Assert.Equal(before.Count + 1, after.Count);
            after.RemoveAt(index);
            Assert.Equal(before, after);
            e.Remove(at);
            Assert.Equal(file, e.Raw);

            // tamanho que não é múltiplo de 4: entra preenchimento antes dos movimentos, que seguem alinhados e com os mesmos dados
            var odd = new ScriptEditor(file.ToArray(), table);
            odd.Insert(at, end, []);
            var aligned = list.SelectMany(i => i.Args).Where(a => a.Type == "movement" && a.Value % 4 == 0).Select(a => Convert.ToHexString(file.AsSpan((int)a.Value, Math.Min(4, file.Length - (int)a.Value)))).ToHashSet();
            var moved = odd.Instructions().SelectMany(i => i.Args).Where(a => a.Type == "movement").ToList();
            foreach (var a in moved.Where(a => aligned.Contains(Convert.ToHexString(odd.Raw.AsSpan((int)a.Value, Math.Min(4, odd.Raw.Length - (int)a.Value))))))
                Assert.Equal(0, a.Value % 4);
            tested++;
        }
        Assert.True(tested > 300, $"{tested} arquivos");
    }
}
