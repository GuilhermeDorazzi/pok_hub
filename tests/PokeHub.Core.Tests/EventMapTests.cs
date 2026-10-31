using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Hub;
using PokeHub.Core.Projects;
using Xunit;
using Xunit.Abstractions;

namespace PokeHub.Core.Tests;

public class EventMapTests(ITestOutputHelper output)
{
    [Fact]
    public void Command_table_and_names_are_embedded()
    {
        var t = ScriptCommandTable.Hgss;
        Assert.True(t.Commands.Count > 800);
        Assert.Equal("SetFlag", t.Get(30)!.Name);
        Assert.Equal("CompareVarToValue", t.Get(17)!.Name);
        Assert.Equal(1, ScriptCommandTable.ArgSize("message"));
        Assert.Equal(4, ScriptCommandTable.ArgSize("script"));
        var n = FlagNames.Hgss;
        Assert.Equal(2912, n.NumFlags);
        Assert.True(n.IsSavedVar(0x4000) && !n.IsSavedVar(0x800C));
    }

    [Fact]
    public void Script_file_parses_offsets_and_follows_calls()
    {
        // 1 entrada: offset 0 -> aponta para o byte logo após 0xFD13. Script: CheckFlag 0x123; GoToIf TRUE -> sub; SetVar 0x4001=5; End. Sub: SetFlag 0x123; Return.
        var code = new List<byte>();
        void U16(int v) { code.Add((byte)v); code.Add((byte)(v >> 8)); }
        void U32(int v) { for (int i = 0; i < 4; i++) code.Add((byte)(v >> (8 * i))); }
        U16(32); U16(0x123);                 // CheckFlag 0x123
        U16(28); code.Add(1); int jumpAt = code.Count; U32(0); // GoToIf TRUE, <sub>
        U16(41); U16(0x4001); U16(5);        // SetVar
        U16(2);                              // End
        int sub = code.Count;
        U16(30); U16(0x123);                 // SetFlag
        U16(27);                             // Return
        int rel = sub - (jumpAt + 4);
        for (int i = 0; i < 4; i++) code[jumpAt + i] = (byte)(rel >> (8 * i));
        byte[] header = [0, 0, 0, 0, 0x13, 0xFD]; // 1 offset (0 => logo após este u32 = byte 4... + 2 do terminador)
        // offset relativo: destino = i + 4 + valor; queremos destino = 6 (fim do header)
        header[0] = 2;
        var raw = header.Concat(code).ToArray();
        var file = new ScriptFile(raw, ScriptCommandTable.Hgss);
        Assert.Single(file.EntryOffsets);
        Assert.Equal(6, file.EntryOffsets[0]);
        var r = file.Routine(1)!;
        var names = r.Instructions.Select(i => i.Name).ToList();
        Assert.Equal(["CheckFlag", "GoToIf", "SetVar", "End", "SetFlag", "Return"], names);
        Assert.Equal(0x123, r.Instructions[0].Args[0].Value);
        Assert.Equal(6 + sub, r.Instructions[1].Arg("script")!.Value);
        Assert.Equal(5, r.Instructions[2].Args[1].Value);
    }

    /// <summary>Com POKEHUB_HUB_ROOT e POKEHUB_REPORT_PROJECT (HGSS): constrói o mapa de eventos da ROM real e imprime um resumo.</summary>
    [Fact]
    public void Builds_event_map_from_real_rom()
    {
        var root = Environment.GetEnvironmentVariable("POKEHUB_HUB_ROOT");
        var name = Environment.GetEnvironmentVariable("POKEHUB_REPORT_PROJECT");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(name)) return;
        var project = new ProjectService(new HubPaths(root)).Open(name)!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var map = EventMap.Load(project);
        output.WriteLine($"{map.MapsWithEvents} mapas com eventos, {map.Events.Count} eventos, {map.FlagUsages.Count} flags usadas, {map.VarUsages.Count} variáveis usadas, {sw.Elapsed.TotalSeconds:F1}s");
        Assert.True(map.Events.Count > 1000);
        var newBark = map.Events.Where(e => e.LocationName == "New Bark Town").ToList();
        output.WriteLine($"New Bark Town: {newBark.Count} eventos");
        foreach (var e in newBark.Take(12))
            output.WriteLine($"  {e.Title} script {e.ScriptId} ({e.InstructionCount} instr) flags[{string.Join(",", e.Flags.Select(f => f.Action + ":" + f.Id.ToString("X")))}] vars[{string.Join(",", e.Vars.Select(v => v.Action + ":" + v.Id.ToString("X") + "=" + v.Value))}] {string.Join(" | ", e.Actions)} :: {string.Join(" / ", e.Messages.Take(2))}");
        var unknown = map.Events.Count(e => e.InstructionCount > 0 && e.Flags.Count == 0 && e.Vars.Count == 0 && e.Messages.Count == 0 && e.Actions.Count == 0);
        output.WriteLine($"eventos com script sem nada reconhecido: {unknown}");
        var free = map.FreeFlags();
        output.WriteLine($"faixas livres de flags: {free.Count}, total {free.Sum(r => r.Count)}; maiores: {string.Join(", ", free.OrderByDescending(r => r.Count).Take(5).Select(r => $"0x{r.From:X}-0x{r.To:X} ({r.Count})"))}");
        var freeV = map.FreeVars();
        output.WriteLine($"faixas livres de variáveis: {freeV.Count}, total {freeV.Sum(r => r.Count)}; maiores: {string.Join(", ", freeV.OrderByDescending(r => r.Count).Take(5).Select(r => $"0x{r.From:X}-0x{r.To:X} ({r.Count})"))}");
        Assert.True(free.Sum(r => r.Count) > 100);
    }
}
