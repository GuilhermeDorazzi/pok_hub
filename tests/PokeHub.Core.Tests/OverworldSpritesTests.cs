using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class OverworldSpritesTests
{
    /// <summary>Um BTX0 de 8x8 em 16 cores montado à mão: confere tamanho, cor e transparência da cor 0.</summary>
    [Fact]
    public void Reads_a_paletted_texture()
    {
        const int tex = 0x14, infoTex = 0x40, infoPal = 0x60, data = 0x80, pal = 0xA0;
        var f = new byte[0xC0];
        "BTX0"u8.CopyTo(f);
        BitConverter.TryWriteBytes(f.AsSpan(0x10), tex);
        BitConverter.TryWriteBytes(f.AsSpan(tex + 0x0E), (ushort)(infoTex - tex));
        BitConverter.TryWriteBytes(f.AsSpan(tex + 0x14), data - tex);
        BitConverter.TryWriteBytes(f.AsSpan(tex + 0x34), infoPal - tex);
        BitConverter.TryWriteBytes(f.AsSpan(tex + 0x38), pal - tex);
        f[infoTex + 1] = 1;
        f[infoPal + 1] = 1;
        BitConverter.TryWriteBytes(f.AsSpan(infoTex + 20), (3u << 26) | (1u << 29));
        f[data] = 0x10; // pixel 0 transparente, pixel 1 com a cor 1
        BitConverter.TryWriteBytes(f.AsSpan(pal + 2), (ushort)0x001F); // vermelho

        var image = Btx0.Render(f)!;
        Assert.Equal(8, image.Width);
        Assert.Equal(0, image.Pixels[3]);
        Assert.Equal(255, image.Pixels[4]);
        Assert.Equal(255, image.Pixels[7]);
        Assert.Equal(0, image.Pixels[5]);
    }

    [Theory]
    [InlineData("POKEHUB_WORKSPACE")]
    [InlineData("POKEHUB_PLATINUM_WORKSPACE")]
    public void Real_workspace_most_npcs_have_a_sprite(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var map = EventMap.Load(project);
        Assert.NotNull(map.Sprites);
        int total = 0, drawn = 0;
        foreach (var header in map.Headers)
        {
            if (map.LayoutOf(header.Id) is not { } layout) continue;
            foreach (var m in layout.Markers.Where(m => m.Kind == EventKind.Npc))
            {
                total++;
                if (map.Sprites!.Render(m.SpriteId!.Value) is not null) drawn++;
            }
        }
        Assert.True(total > 1000);
        Assert.True(drawn > total * 0.7, $"{drawn} de {total}");
    }
}
