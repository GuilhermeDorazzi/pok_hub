using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class GameIconsTests
{
    private static bool HasPixels(byte[] rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] != 0) return true;
        return false;
    }

    [Theory]
    [InlineData("POKEHUB_TABLES_SS", 536)]
    [InlineData("POKEHUB_TABLES_PT", 467)]
    [InlineData("POKEHUB_TABLES_LEGACY", 536)] // workspace do ndstool, com o arm9 comprimido
    public void Icons_exist_for_items_and_species_on_real_data(string variable, int items)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var dir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(dir), Config = ProjectConfig.Load(Path.Combine(dir, "project.json")) };
        var icons = GameIcons.TryLoad(Gen4Game.For(project));
        Assert.NotNull(icons);

        for (int species = 1; species <= 493; species++)
        {
            var image = icons!.Species(species);
            Assert.NotNull(image);
            Assert.True(HasPixels(image!.Pixels), $"espécie {species}");
        }
        for (int item = 1; item < items; item++)
        {
            var image = icons!.Item(item);
            Assert.NotNull(image);
            Assert.True(HasPixels(image!.Pixels), $"item {item}");
        }
        // o Bulbasaur usa a paleta 1: sem a tabela do arm9 ele sairia com as cores da paleta 0
        Assert.True(icons!.UsesSpeciesPalettes);
        Assert.Null(icons!.Species(494));
        Assert.Null(icons.Item(-1));

        var dump = Environment.GetEnvironmentVariable("OW_DUMP");
        if (dump is null) return;
        // folha para olhar: 12 ícones de Pokémon e 12 de item por linha
        int[] speciesSample = [1, 4, 7, 25, 133, 150, 152, 155, 158, 249, 250, 493];
        int[] itemSample = [1, 2, 4, 17, 28, 50, 68, 149, 217, 328, 400, items - 1];
        var sheet = new byte[32 * 12 * 32 * 2 * 4];
        void Put(byte[]? src, int col, int row)
        {
            if (src is null) return;
            for (int y = 0; y < 32; y++) Buffer.BlockCopy(src, y * 32 * 4, sheet, ((row * 32 + y) * 32 * 12 + col * 32) * 4, 32 * 4);
        }
        for (int i = 0; i < 12; i++)
        {
            Put(icons.Species(speciesSample[i])?.Pixels, i, 0);
            Put(icons.Item(itemSample[i])?.Pixels, i, 1);
        }
        File.WriteAllBytes($"{dump}_icons_{variable[^2..]}.raw", sheet);
    }
}
