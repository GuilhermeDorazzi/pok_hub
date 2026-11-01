using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Graphics;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class RealMapRendererTests
{
    private static ModelVertex V(float x, float y, float z) => new(x, y, z, 0, 0, 255, 255, 255);

    [Fact]
    public void Top_down_keeps_the_highest_surface_and_skips_invisible_material()
    {
        var model = new NsbmdModel();
        model.Materials.Add(new ModelMaterial());
        model.Materials.Add(new ModelMaterial { Alpha = 0 });
        model.Triangles.Add(new ModelTriangle(V(0, 0, 0), V(4, 0, 0), V(0, 0, 4), 0));
        var raster = new TopDownRaster(4, 4, 1, (10, 20, 30));
        raster.Draw(model, 0, 0, 0, (_, _) => null);
        Assert.Equal(255, raster.Image.Pixels[0]);
        Assert.Equal(10, raster.Image.Pixels[(3 * 4 + 3) * 4]);

        var hidden = new TopDownRaster(4, 4, 1, (10, 20, 30));
        model.Triangles[0] = model.Triangles[0] with { Material = 1 };
        hidden.Draw(model, 0, 0, 0, (_, _) => null);
        Assert.Equal(10, hidden.Image.Pixels[0]);
    }

    [Fact]
    public void Shadow_material_blends_with_what_is_below()
    {
        var model = new NsbmdModel();
        model.Materials.Add(new ModelMaterial { Alpha = 15 });
        model.Triangles.Add(new ModelTriangle(V(0, 0, 0), V(4, 0, 0), V(0, 0, 4), 0));
        var raster = new TopDownRaster(4, 4, 1, (0, 0, 0));
        raster.Draw(model, 0, 0, 0, (_, _) => null);
        Assert.InRange(raster.Image.Pixels[0], 100, 140);
    }

    /// <summary>Com os projetos reais: todo mapa desenha, sem erro e com conteúdo.</summary>
    [Theory]
    [InlineData("POKEHUB_TABLES_SS")]
    [InlineData("POKEHUB_TABLES_PT")]
    public void Every_map_draws_on_real_data(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var map = EventMap.Load(project);
        var renderer = RealMapRenderer.TryLoad(map.Game);
        Assert.NotNull(renderer);

        var dump = Environment.GetEnvironmentVariable("OW_DUMP");
        var report = new List<string>();
        int drawn = 0, flat = 0;
        foreach (var header in map.Headers.Where(h => h.EventsBank >= 0))
        {
            if (map.LayoutOf(header.Id) is not { } layout || layout.Width == 0) continue;
            if (!RealMapRenderer.CanRender(layout))
            {
                Assert.Null(renderer!.Render(layout, header.AreaId));
                continue;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var image = renderer!.Render(layout, header.AreaId);
            Assert.NotNull(image);
            Assert.Equal(layout.Width * RealMapRenderer.PixelsPerTile, image!.Width);
            var colors = new HashSet<int>();
            for (int i = 0; i + 3 < image.Pixels.Length; i += 4 * 7) colors.Add(BitConverter.ToInt32(image.Pixels, i));
            drawn++;
            if (colors.Count < 4) flat++;
            report.Add($"{header.InternalName}\t{header.AreaId}\t{image.Width}x{image.Height}\t{colors.Count}\t{watch.ElapsedMilliseconds}");
            if (dump is not null && Array.IndexOf(["T20R0101", "T20PC0101", "C01PC0101", "C01R0101", "T01R0101", "T01R0201"], header.InternalName) >= 0)
            {
                File.WriteAllBytes($"{dump}_{variable[^2..]}_{header.InternalName}.raw", image.Pixels);
                File.WriteAllText($"{dump}_{variable[^2..]}_{header.InternalName}.txt", $"{image.Width}x{image.Height}");
            }
        }
        if (dump is not null) File.WriteAllLines($"{dump}_report_{variable[^2..]}.tsv", report);
        Assert.True(drawn > 100, $"só {drawn} mapas");
        Assert.True(flat * 10 < drawn, $"{flat} de {drawn} mapas sem conteúdo");
    }
}
