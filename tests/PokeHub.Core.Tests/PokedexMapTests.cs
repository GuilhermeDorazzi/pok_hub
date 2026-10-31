using System.IO.Compression;
using PokeHub.Core.Gen4;
using PokeHub.Core.Graphics;
using PokeHub.Core.Hub;
using PokeHub.Core.Projects;
using Xunit;
using Xunit.Abstractions;

namespace PokeHub.Core.Tests;

public class PokedexMapTests(ITestOutputHelper output)
{
    [Fact]
    public void Lz10_decompresses_literals_and_references()
    {
        // "abcabcabcabc": 3 literais + referência de 9 bytes com deslocamento 3
        byte[] packed = [0x10, 12, 0, 0, 0b0001_0000, (byte)'a', (byte)'b', (byte)'c', 0x60, 0x02];
        Assert.Equal("abcabcabcabc"u8.ToArray(), Lz10.Decompress(packed));
    }

    /// <summary>Com POKEHUB_HUB_ROOT e POKEHUB_REPORT_PROJECT (HGSS): renderiza o mapa da Pokédex e grava um PNG em POKEHUB_MAP_PNG.</summary>
    [Fact]
    public void Renders_pokedex_map_from_real_rom()
    {
        var root = Environment.GetEnvironmentVariable("POKEHUB_HUB_ROOT");
        var name = Environment.GetEnvironmentVariable("POKEHUB_REPORT_PROJECT");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(name)) return;
        var project = new ProjectService(new HubPaths(root)).Open(name)!;
        var map = PokedexAreaMap.Load(project);
        output.WriteLine($"mapa {map.Image.Width}x{map.Image.Height}, {map.Overworlds.Count} áreas normais, {map.Dungeons.Count} especiais");
        Assert.Equal(512, map.Image.Width);
        Assert.True(map.Overworlds.Count >= 70 && map.Dungeons.Count >= 26);
        var r29 = map.Overworlds[2]!;
        output.WriteLine($"Route 29: x={r29.X} y={r29.Y} {r29.Width}x{r29.Height}");
        Assert.True(r29.Width >= 1 && r29.X < 32);
        // nenhum pixel magenta (cor de índice fora da paleta)
        int magenta = 0;
        for (int i = 0; i < map.Image.Pixels.Length; i += 4)
            if (map.Image.Pixels[i] == 255 && map.Image.Pixels[i + 1] == 0 && map.Image.Pixels[i + 2] == 255) magenta++;
        Assert.Equal(0, magenta);

        if (Environment.GetEnvironmentVariable("POKEHUB_MAP_PNG") is { } png) WritePng(png, map.Image);
    }

    private static void WritePng(string path, RgbaImage img)
    {
        using var ms = new MemoryStream();
        void Chunk(string type, byte[] data)
        {
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            ms.Write(BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(data.Length)));
            ms.Write(t); ms.Write(data);
            var crc = Patch.Crc32.HashToUInt32([.. t, .. data]);
            ms.Write(BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(crc)));
        }
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(img.Width)).CopyTo(ihdr, 0);
        BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(img.Height)).CopyTo(ihdr, 4);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk("IHDR", ihdr);
        var raw = new MemoryStream();
        for (int y = 0; y < img.Height; y++) { raw.WriteByte(0); raw.Write(img.Pixels, y * img.Width * 4, img.Width * 4); }
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw.ToArray());
        Chunk("IDAT", z.ToArray());
        Chunk("IEND", []);
        File.WriteAllBytes(path, ms.ToArray());
    }
}
