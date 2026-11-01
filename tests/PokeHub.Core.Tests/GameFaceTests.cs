using System.Text;
using PokeHub.Core.Gen4;
using PokeHub.Core.Graphics;
using Xunit;

namespace PokeHub.Core.Tests;

public class GameFaceTests
{
    private const string Banner = "version: Original\ntitle:\n  japanese: |-\n    Pokémon SoulSilver\n    Nintendo\n  english: |-\n    Pokémon SoulSilver\n    Nintendo\n  french: |-\n    A\n  german: |-\n    A\n  italian: |-\n    A\n  spanish: |-\n    A\n    B\nimages:\n  bitmap_path: bitmap.png\n";

    [Fact]
    public void Banner_name_changes_in_every_language_and_keeps_the_rest()
    {
        var result = GameFaceService.WithBannerText(Banner, "Pokémon SS Novo", "Gui");
        Assert.DoesNotContain("SoulSilver", result);
        Assert.Equal(6, result.Split("Pokémon SS Novo").Length - 1);
        Assert.Contains("version: Original\n", result);
        Assert.EndsWith("images:\n  bitmap_path: bitmap.png\n", result);
        Assert.Contains("  spanish: |-\n    Pokémon SS Novo\n    Gui\nimages:", result);
    }

    [Fact]
    public void Banner_rejects_a_name_that_does_not_fit()
    {
        Assert.Throws<ArgumentException>(() => GameFaceService.WithBannerText(Banner, new string('x', 129), ""));
        Assert.Throws<ArgumentException>(() => GameFaceService.WithBannerText(Banner, "  ", ""));
    }

    [Theory]
    [InlineData("pokemon gm", "POKEMON GM")]
    [InlineData("A1._-", "A1._-")]
    public void Rom_title_goes_upper_case(string input, string expected) =>
        Assert.Contains($"title: \"{expected}\"\r\ngamecode", GameFaceService.WithRomTitle("title: POKEMON SS\r\ngamecode: IPGE\r\n", input));

    [Theory]
    [InlineData("TITULO LONGO DEMAIS")]
    [InlineData("ação")]
    [InlineData("")]
    public void Rom_title_rejects_what_the_header_cannot_hold(string input) =>
        Assert.Throws<ArgumentException>(() => GameFaceService.WithRomTitle("title: X\n", input));

    private static RgbaImage Gradient(int width, int height)
    {
        var image = new RgbaImage(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image.Set(x, y, (byte)(x * 255 / width), (byte)(y * 255 / height), (byte)((x + y) * 3 % 256));
        return image;
    }

    [Fact]
    public void Quantizer_respects_the_limit_and_keeps_transparency()
    {
        var image = Gradient(64, 64);
        image.Set(3, 3, 0, 0, 0, 0);
        var reduced = ColorQuantizer.Reduce(image, 15);
        Assert.InRange(reduced.Palette.Length, 2, 15);
        Assert.True(reduced.OriginalColors > 15);
        Assert.Equal(0, reduced.Indices[3 * 64 + 3]);
        Assert.All(reduced.Indices.Where((_, i) => i != 3 * 64 + 3), i => Assert.InRange(i, 1, reduced.Palette.Length));
    }

    [Fact]
    public void Quantizer_keeps_exact_colors_when_they_fit()
    {
        var image = new RgbaImage(2, 1);
        image.Set(0, 0, 248, 0, 0);
        image.Set(1, 0, 0, 0, 248);
        var reduced = ColorQuantizer.Reduce(image, 15);
        Assert.Equal(2, reduced.Palette.Length);
        Assert.Contains(ColorQuantizer.ToDs(248, 0, 0), reduced.Palette);
    }

    [Fact]
    public void Icon_pixels_all_exist_in_its_palette()
    {
        var (bitmap, palette, _) = GameFaceService.BuildIcon(Gradient(32, 32));
        Assert.Equal(16, palette.Width);
        Assert.Equal(0, palette.Pixels[3]);
        var colors = new HashSet<(byte, byte, byte)>();
        for (int i = 1; i < 16; i++) colors.Add((palette.Pixels[i * 4], palette.Pixels[i * 4 + 1], palette.Pixels[i * 4 + 2]));
        for (int i = 0; i < bitmap.Pixels.Length; i += 4)
            if (bitmap.Pixels[i + 3] != 0) Assert.Contains((bitmap.Pixels[i], bitmap.Pixels[i + 1], bitmap.Pixels[i + 2]), colors);
    }

    private static byte[] Ncgr()
    {
        var file = new byte[0x30 + 32 * 15 * 64 + 16];
        Encoding.ASCII.GetBytes("RGCN").CopyTo(file, 0);
        BitConverter.TryWriteBytes(file.AsSpan(0x1C), 4);
        BitConverter.TryWriteBytes(file.AsSpan(0x28), 32 * 15 * 64);
        return file;
    }

    [Fact]
    public void Logo_tiles_follow_the_image_and_tile_zero_stays_empty()
    {
        var pixels = Enumerable.Repeat((byte)7, 256 * 120).ToArray();
        pixels[8 * 256 + 16] = 9; // tile 34 (linha 1, coluna 2), primeiro pixel
        var result = GameFaceService.PatchLogoTiles(Ncgr(), pixels);
        Assert.All(result.Skip(0x30).Take(64), b => Assert.Equal(0, b));
        Assert.Equal(7, result[0x30 + 64]);
        Assert.Equal(9, result[0x30 + 34 * 64]);
        Assert.Equal(7, result[0x30 + 34 * 64 + 1]);
    }

    [Fact]
    public void Logo_palette_keeps_the_background_colors()
    {
        var file = new byte[0x28 + 512 + 16];
        Encoding.ASCII.GetBytes("RLCN").CopyTo(file, 0);
        for (int i = 0; i < 256; i++) BitConverter.TryWriteBytes(file.AsSpan(0x28 + i * 2), (ushort)i);
        var result = GameFaceService.PatchLogoPalette(file, [0x1234, 0x2345]);
        Assert.Equal(0, BitConverter.ToUInt16(result, 0x28));
        Assert.Equal(0x1234, BitConverter.ToUInt16(result, 0x28 + 2));
        Assert.Equal(0x2345, BitConverter.ToUInt16(result, 0x28 + 4));
        Assert.Equal(3, BitConverter.ToUInt16(result, 0x28 + 6));
        Assert.Equal(128, BitConverter.ToUInt16(result, 0x28 + 256));
    }

    [Fact]
    public void Logo_screen_only_has_the_logo_area_in_order()
    {
        var file = new byte[0x24 + 2048];
        Encoding.ASCII.GetBytes("RCSN").CopyTo(file, 0);
        BitConverter.TryWriteBytes(file.AsSpan(0x24 + 3 * 32 * 2), (ushort)27);
        var result = GameFaceService.LogoScreenOnly(file);
        ushort At(int row, int column) => BitConverter.ToUInt16(result, 0x24 + (row * 32 + column) * 2);
        Assert.Equal(0, At(3, 0));
        Assert.Equal(1, At(5, 1));
        Assert.Equal(479, At(19, 31));
        Assert.Equal(0, At(20, 0));
    }

    [Fact]
    public void Png_starts_with_the_signature_and_has_an_end()
    {
        var png = GameFaceService.EncodePng(Gradient(4, 4));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Equal("IEND", Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }
}
