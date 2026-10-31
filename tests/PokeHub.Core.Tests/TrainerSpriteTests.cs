using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class TrainerSpriteTests
{
    private const int Tiles = 2, Frames = 2, Side = Tiles * 8;

    /// <summary>Arquivo Nitro mínimo: magic invertido, cabeçalho de 16 bytes e uma seção.</summary>
    private static byte[] Nitro(string fileMagic, string sectionMagic, byte[] body)
    {
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes(fileMagic));
        ms.Write(new byte[] { 0xFF, 0xFE, 0, 1 });
        ms.Write(BitConverter.GetBytes((uint)(16 + 8 + body.Length)));
        ms.Write(BitConverter.GetBytes((ushort)16));
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(System.Text.Encoding.ASCII.GetBytes(sectionMagic));
        ms.Write(BitConverter.GetBytes((uint)(8 + body.Length)));
        ms.Write(body);
        return ms.ToArray();
    }

    private static byte[] Palette()
    {
        var body = new byte[16 + 32];
        BitConverter.TryWriteBytes(body.AsSpan(0), 3u);
        BitConverter.TryWriteBytes(body.AsSpan(8), 32u);
        BitConverter.TryWriteBytes(body.AsSpan(12), 16u);
        BitConverter.TryWriteBytes(body.AsSpan(16 + 2), (ushort)0x001F); // cor 1: vermelho
        return Nitro("RLCN", "TTLP", body);
    }

    private static byte[] Ncbr(byte[] pixels)
    {
        var body = new byte[24 + pixels.Length];
        BitConverter.TryWriteBytes(body.AsSpan(0), (ushort)Tiles);
        BitConverter.TryWriteBytes(body.AsSpan(2), (ushort)(Tiles * Frames));
        BitConverter.TryWriteBytes(body.AsSpan(4), 3u);
        BitConverter.TryWriteBytes(body.AsSpan(16), (uint)pixels.Length);
        BitConverter.TryWriteBytes(body.AsSpan(20), 24u);
        pixels.CopyTo(body, 24);
        return Nitro("RGCN", "RAHC", body);
    }

    /// <summary>Quadro 0 transparente com um quadrado de cor 1 no canto; quadro 1 todo em cor 1, que não pode aparecer.</summary>
    private static byte[] PlainPixels()
    {
        int width = Side * Frames;
        var px = new byte[width * Side / 2];
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < width; x++)
                if (x >= Side || (x < 4 && y < 4))
                    px[y * (width / 2) + x / 2] |= (byte)((x & 1) != 0 ? 0x10 : 0x01);
        px[^1] = 0; px[^2] = 0; // a semente da cifra de trás para frente decifra para zero
        return px;
    }

    private static byte[] EncryptFromEnd(byte[] plain, ushort key)
    {
        var data = plain.ToArray();
        uint seed = key;
        for (int i = data.Length / 2 - 1; i >= 0; i--)
        {
            BitConverter.TryWriteBytes(data.AsSpan(i * 2), (ushort)(BitConverter.ToUInt16(plain, i * 2) ^ (ushort)seed));
            seed = seed * 0x41C64E6D + 0x6073;
        }
        return data;
    }

    private static void AssertSprite(byte[] stored)
    {
        var image = TrainerSprites.Decode(Ncbr(stored), Palette())!;
        Assert.Equal(Side, image.Width);
        Assert.Equal(Side, image.Height);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Pixels[..4]);
        Assert.Equal(0, image.Pixels[(10 * Side + 10) * 4 + 3]); // cor 0 é transparente
    }

    [Fact]
    public void Decrypts_from_end_and_shows_only_the_first_frame() => AssertSprite(EncryptFromEnd(PlainPixels(), 0xBEEF));

    [Fact]
    public void Decrypt_is_forward_when_the_seed_is_the_first_word()
    {
        var encrypted = TrainerSprites.Decrypt(PlainPixels(), fromEnd: false);
        Assert.NotEqual(PlainPixels(), encrypted);
        Assert.Equal(0, BitConverter.ToUInt16(encrypted, 0)); // a primeira palavra é a própria semente
    }

    [Fact]
    public void Plain_data_is_also_read() => AssertSprite(PlainPixels());

    [Fact]
    public void Non_sprite_data_gives_no_image()
    {
        var body = Ncbr(new byte[8]);
        Assert.Null(TrainerSprites.Decode(body, Palette())); // tamanho menor que o declarado pelas dimensões
    }

    /// <summary>Com POKEHUB_WORKSPACE (HGSS).</summary>
    [Fact]
    public void Real_workspace_every_class_has_a_sprite()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_WORKSPACE");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var repo = TrainerRepository.Load(project);
        Assert.NotNull(repo.Sprites);
        foreach (var cls in repo.Trainers.Select(t => (int)t.Class).Distinct())
        {
            var image = repo.Sprites!.Render(cls);
            Assert.NotNull(image);
            Assert.Equal(80, image!.Width);
            int visible = 0;
            for (int i = 3; i < image.Pixels.Length; i += 4) if (image.Pixels[i] != 0) visible++;
            Assert.InRange(visible, 200, 80 * 80 - 200); // nem vazia nem ruído sem fundo
        }
    }
}
