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

    private static byte[] Nclr()
    {
        var file = new byte[0x28 + 512 + 16];
        Encoding.ASCII.GetBytes("RLCN").CopyTo(file, 0);
        BitConverter.TryWriteBytes(file.AsSpan(0x28 + 40 * 2), (ushort)0x7FFF);
        return file;
    }

    private static byte Pixel(byte[] ncgr, int x, int y) => ncgr[0x30 + (y / 8 * 32 + x / 8) * 64 + y % 8 * 8 + x % 8];

    [Fact]
    public void Version_stamp_uses_the_brightest_color_and_replaces_the_old_one()
    {
        var v8 = GameFaceService.StampLogoVersion(Ncgr(), Nclr(), "V8");
        Assert.Equal(40, Pixel(v8, 182, 106));
        Assert.Equal(0, Pixel(v8, 200, 106));
        var v1 = GameFaceService.StampLogoVersion(v8, Nclr(), "v1");
        Assert.NotEqual(v8, v1);
        Assert.Equal(v1, GameFaceService.StampLogoVersion(Ncgr(), Nclr(), "V1"));
    }

    [Fact]
    public void Long_version_drops_to_the_small_size_and_stays_in_its_area()
    {
        var stamped = GameFaceService.StampLogoVersion(Ncgr(), Nclr(), "V123");
        for (int y = 0; y < 120; y++)
            for (int x = 0; x < 256; x++)
                if (Pixel(stamped, x, y) != 0)
                {
                    Assert.InRange(x, 182, 207);
                    Assert.InRange(y, 112, 119);
                }
    }

    [Fact]
    public void Styled_version_always_starts_from_the_original()
    {
        var original = Ncgr();
        // cor do degradê, tirada da letra I
        for (int y = 100; y < 120; y++) original[0x30 + (y / 8 * 32 + 138 / 8) * 64 + y % 8 * 8 + 138 % 8] = 9;
        var clean = (byte[])original.Clone();
        var v12 = GameFaceService.StampStyledVersion(original, "V12");
        var v1 = GameFaceService.StampStyledVersion(original, "v1");
        Assert.NotEqual(v12, v1);
        Assert.Equal(v1, GameFaceService.StampStyledVersion(original, "V1"));
        Assert.Equal(clean, original);
        Assert.Throws<ArgumentException>(() => GameFaceService.StampStyledVersion(original, "V100"));
    }

    [Fact]
    public void Styled_version_stays_off_the_rest_of_the_logo()
    {
        // um pixel da cauda da Lugia onde o número cairia: melhor recusar do que cobrir
        var logo = Ncgr();
        const int x = 188, y = 110;
        logo[0x30 + (y / 8 * 32 + x / 8) * 64 + y % 8 * 8 + x % 8] = 5;
        Assert.Throws<ArgumentException>(() => GameFaceService.StampStyledVersion(logo, "V88"));
    }

    [Fact]
    public void Plain_mark_is_removed_for_the_original()
    {
        var stamped = GameFaceService.StampLogoVersion(Ncgr(), Nclr(), "V1");
        Assert.NotEqual(Ncgr(), stamped);
        Assert.Equal(Ncgr(), GameFaceService.WithoutPlainStamp(stamped));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("V")]
    [InlineData("V1000")]
    [InlineData("V1a")]
    public void Version_rejects_other_formats(string version) =>
        Assert.Throws<ArgumentException>(() => GameFaceService.StampLogoVersion(Ncgr(), Nclr(), version));

    [Theory]
    [InlineData("Pokémon SoulSilver", "Pokémon SoulSilver V2")]
    [InlineData("Pokémon SoulSilver V1", "Pokémon SoulSilver V2")]
    [InlineData("Pokémon V1 SoulSilver V10", "Pokémon V1 SoulSilver V2")]
    public void Version_goes_at_the_end_of_the_name(string name, string expected) =>
        Assert.Equal(expected, GameFaceService.WithVersion(name, "V2"));

    [Fact]
    public void Png_starts_with_the_signature_and_has_an_end()
    {
        var png = GameFaceService.EncodePng(Gradient(4, 4));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Equal("IEND", Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }
}
