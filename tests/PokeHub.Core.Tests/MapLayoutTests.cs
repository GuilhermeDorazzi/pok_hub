using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Processes;
using PokeHub.Core.Projects;
using PokeHub.Core.Runtimes;
using Xunit;
using Xunit.Abstractions;

namespace PokeHub.Core.Tests;

public class MapLayoutTests(ITestOutputHelper output)
{
    /// <summary>Matriz 2×1 com cabeçalhos: a célula 0 é do mapa 7 e a 1 do mapa 8.</summary>
    private static byte[] Matrix()
    {
        using var ms = new MemoryStream();
        ms.Write([2, 1, 1, 0, 2, (byte)'m', (byte)'x']);
        foreach (ushort h in new ushort[] { 7, 8 }) ms.Write(BitConverter.GetBytes(h));
        foreach (ushort l in new ushort[] { 0, 1 }) ms.Write(BitConverter.GetBytes(l));
        return ms.ToArray();
    }

    private static byte[] Land(bool sound)
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(2048)); ms.Write(new byte[12]);
        if (sound) { ms.Write(BitConverter.GetBytes((ushort)0x1234)); ms.Write(BitConverter.GetBytes((ushort)2)); ms.Write(new byte[2]); }
        var perms = new byte[2048];
        perms[(1 * 32 + 3) * 2 + 1] = 0x80; // tile (3,1) bloqueado
        ms.Write(perms);
        return ms.ToArray();
    }

    /// <summary>Um NPC em (x, z), sem o resto.</summary>
    private static EventFile Events(int x, int z)
    {
        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(1));
        var npc = new byte[32];
        BitConverter.TryWriteBytes(npc.AsSpan(24), (ushort)x);
        BitConverter.TryWriteBytes(npc.AsSpan(26), (ushort)z);
        ms.Write(npc);
        ms.Write(BitConverter.GetBytes(0));
        ms.Write(BitConverter.GetBytes(0));
        return new EventFile(ms.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Reads_collisions_with_or_without_the_sound_section(bool sound)
    {
        var layout = MapLayout.Build(Matrix(), _ => Land(sound), 7, Events(5, 6), sound)!;
        Assert.Equal(32, layout.Width);
        Assert.True(layout.IsBlocked(3, 1));
        Assert.False(layout.IsBlocked(4, 1));
    }

    [Fact]
    public void Only_the_map_cells_are_drawn_and_positions_become_local()
    {
        var second = MapLayout.Build(Matrix(), _ => Land(true), 8, Events(40, 2), true)!;
        Assert.Equal(32, second.Width);
        Assert.Equal(8, Assert.Single(second.Markers).X); // 40 no mundo é 8 na célula do mapa
    }

    [Fact]
    public void An_event_in_a_neighbor_cell_widens_the_area()
    {
        var layout = MapLayout.Build(Matrix(), _ => Land(true), 7, Events(40, 2), true)!;
        Assert.Equal(64, layout.Width);
        Assert.Equal(40, Assert.Single(layout.Markers).X);
    }

    [Fact]
    public void Corrupt_files_give_no_layout_or_empty_tiles_instead_of_a_crash()
    {
        Assert.Null(MapLayout.Build([1, 1], _ => null, 0, Events(0, 0), true));
        Assert.Null(MapLayout.Build([9, 9, 1, 0, 0], _ => null, 0, Events(0, 0), true));
        var noLand = MapLayout.Build(Matrix(), _ => [1, 2, 3], 7, Events(1, 1), true)!;
        Assert.False(noLand.IsBlocked(3, 1));
    }

    [Fact]
    public void Java_home_comes_from_the_managed_java_only()
    {
        Assert.Equal(@"C:\hub\runtimes\java8\jre", ToolLauncher.JavaHomeOf(@"C:\hub\runtimes\java8\jre\bin\java.exe"));
        Assert.Null(ToolLauncher.JavaHomeOf("java"));
        Assert.True(RuntimeManager.IsManaged("java8"));
    }

    /// <summary>Com POKEHUB_WORKSPACE (HGSS): eventos caem dentro da área e NPCs quase nunca em parede. Se a leitura estivesse deslocada, cairiam ao acaso.</summary>
    [Fact]
    public void Real_workspace_events_fall_on_walkable_tiles()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_WORKSPACE");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var map = EventMap.Load(project);
        int maps = 0, missing = 0, outside = 0, onWall = 0, total = 0;
        foreach (var id in map.Events.Select(e => e.MapId).Distinct())
        {
            var layout = map.LayoutOf(id);
            if (layout is null) { missing++; continue; }
            maps++;
            foreach (var m in layout.Markers.Where(m => m.Kind == EventKind.Npc))
            {
                total++;
                if (m.X < 0 || m.Y < 0 || m.X >= layout.Width || m.Y >= layout.Height) outside++;
                else if (layout.IsBlocked(m.X, m.Y)) onWall++;
            }
        }
        output.WriteLine($"mapas {maps}, sem planta {missing}, NPCs {total}, fora {outside}, em tile bloqueado {onWall}");
        Assert.True(maps > 300);
        Assert.Equal(0, missing);
        Assert.Equal(0, outside);
        Assert.True(onWall < total / 3, $"{onWall} de {total} NPCs em tile bloqueado: leitura suspeita");

        // os outros tipos de marcador: dentro da área, e warp com destino que existe
        foreach (var kind in new[] { EventKind.Trigger, EventKind.Sign, EventKind.Warp })
        {
            int count = 0, off = 0, blocked = 0;
            foreach (var id in map.Headers.Select(h => h.Id))
            {
                if (map.LayoutOf(id) is not { } layout) continue;
                foreach (var m in layout.Markers.Where(m => m.Kind == kind))
                {
                    count++;
                    if (m.X < 0 || m.Y < 0 || m.X + m.Width > layout.Width || m.Y + m.Height > layout.Height) off++;
                    else if (layout.IsBlocked(m.X, m.Y)) blocked++;
                }
            }
            output.WriteLine($"{kind}: {count}, fora {off}, em tile bloqueado {blocked}");
            Assert.True(count > 0);
            Assert.True(off <= count / 50, $"{off} de {count} marcadores {kind} fora da área");
        }
        var headers = map.Headers.Select(h => h.Id).ToHashSet();
        var warps = map.Headers.SelectMany(h => map.LayoutOf(h.Id)?.Markers.Where(m => m.Kind == EventKind.Warp) ?? []).ToList();
        int dynamic = warps.Count(w => w.TargetMap is null);
        var badTargets = warps.Where(w => w.TargetMap is { } t && !headers.Contains(t)).Select(w => w.TargetMap).Distinct().ToList();
        output.WriteLine($"warps {warps.Count}, dinâmicos {dynamic}, destino inexistente: {string.Join(", ", badTargets)}");
        Assert.Empty(badTargets);
    }
}
