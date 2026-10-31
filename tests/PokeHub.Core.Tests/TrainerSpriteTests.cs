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
        var image = SpriteStrip.Decode(Ncbr(stored), Palette())!;
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
        var encrypted = SpriteStrip.Decrypt(PlainPixels(), fromEnd: false);
        Assert.NotEqual(PlainPixels(), encrypted);
        Assert.Equal(0, BitConverter.ToUInt16(encrypted, 0)); // a primeira palavra é a própria semente
    }

    [Fact]
    public void Plain_data_is_also_read() => AssertSprite(PlainPixels());

    [Fact]
    public void Non_sprite_data_gives_no_image()
    {
        var body = Ncbr(new byte[8]);
        Assert.Null(SpriteStrip.Decode(body, Palette())); // tamanho menor que o declarado pelas dimensões
    }

    [Fact]
    public void Trim_crops_a_centered_square_around_visible_pixels()
    {
        var image = new PokeHub.Core.Graphics.RgbaImage(16, 16);
        image.Set(4, 6, 1, 2, 3);
        image.Set(8, 7, 1, 2, 3);
        var trimmed = image.TrimmedToOpaque()!;
        Assert.Equal(5, trimmed.Width);
        Assert.Equal(5, trimmed.Height);
        Assert.Null(new PokeHub.Core.Graphics.RgbaImage(4, 4).TrimmedToOpaque());
    }

    [Fact]
    public void Item_table_is_found_by_its_first_entries()
    {
        ushort[] words = [0xAAAA, 0xBBBB, 0, 793, 794, 0, 1, 2, 3, 1, 2, 4, 5, 2, 3, 6, 7, 3];
        var arm9 = new byte[words.Length * 2];
        Buffer.BlockCopy(words, 0, arm9, 0, arm9.Length);
        Assert.Equal(4, ItemIcons.FindTable(arm9));
        Assert.Equal(-1, ItemIcons.FindTable(new byte[64]));
    }

    private static Project? RealProject(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return null;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        return new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
    }

    /// <summary>Formas, fêmeas e ícones. POKEHUB_WORKSPACE para HGSS ou Pt; com POKEHUB_SPRITE_SHEET grava uma folha PNG para conferir a olho.</summary>
    [Theory]
    [InlineData("POKEHUB_WORKSPACE")]
    [InlineData("POKEHUB_PLATINUM_WORKSPACE")]
    public void Real_workspace_forms_and_item_icons(string variable)
    {
        if (RealProject(variable) is not { } project) return;
        var game = Gen4Game.For(project);
        var sprites = PokemonSprites.Load(game)!;
        var icons = ItemIcons.Load(game);
        Assert.NotNull(icons);
        Assert.Null(icons!.Render(0));
        for (int item = 1; item <= 400; item++) Assert.NotNull(icons.Render(item));

        (int Species, int Forms)[] withForms = [(201, 28), (351, 4), (386, 4), (412, 3), (413, 3), (421, 2), (422, 2), (423, 2), (479, 6), (487, 2), (492, 2), (493, 18)];
        foreach (var (species, forms) in withForms)
            for (int form = 0; form < forms; form++)
                Assert.NotNull(sprites.Render(species, form));

        // formas diferentes precisam dar imagens diferentes, senão a numeração está errada
        foreach (var species in new[] { 201, 351, 386, 412, 413, 421, 422, 423, 479, 487, 492, 493 })
            Assert.NotEqual(sprites.Render(species, 0)!.Pixels, sprites.Render(species, 1)!.Pixels);
        Assert.NotEqual(sprites.Render(25)!.Pixels, sprites.Render(25, female: true)!.Pixels); // Pikachu tem a cauda diferente

        if (Environment.GetEnvironmentVariable("POKEHUB_SPRITE_SHEET") is not { } sheet) return;
        var images = new List<PokeHub.Core.Graphics.RgbaImage?>();
        foreach (var (species, forms) in withForms) for (int form = 0; form < Math.Min(forms, 6); form++) images.Add(sprites.Render(species, form));
        images.Add(sprites.Render(25)); images.Add(sprites.Render(25, female: true));
        foreach (var item in new[] { 1, 4, 17, 38, 149, 234, 275, 328 }) images.Add(icons.Render(item));
        var trainers = TrainerSprites.Load(game)!;
        foreach (var cls in new[] { 0, 1, 20, 63 }) images.Add(trainers.Render(cls));
        WriteSheet(sheet + "-" + variable + ".png", images);
    }

    /// <summary>Folha de 10 colunas com fundo cinza; PNG mínimo, sem depender do WPF neste projeto.</summary>
    private static void WriteSheet(string file, List<PokeHub.Core.Graphics.RgbaImage?> images)
    {
        const int Cell = 80, Columns = 10;
        int rows = (images.Count + Columns - 1) / Columns, width = Cell * Columns, height = Cell * rows;
        var raw = new byte[(width * 3 + 1) * height];
        for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i % (width * 3 + 1) == 0 ? 0 : 0xC0);
        for (int n = 0; n < images.Count; n++)
        {
            if (images[n] is not { } img) continue;
            for (int y = 0; y < Math.Min(img.Height, Cell); y++)
                for (int x = 0; x < Math.Min(img.Width, Cell); x++)
                {
                    int s = (y * img.Width + x) * 4;
                    if (img.Pixels[s + 3] == 0) continue;
                    int d = (n / Columns * Cell + y) * (width * 3 + 1) + 1 + (n % Columns * Cell + x) * 3;
                    raw[d] = img.Pixels[s]; raw[d + 1] = img.Pixels[s + 1]; raw[d + 2] = img.Pixels[s + 2];
                }
        }
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            png.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(data.Length)));
            png.Write(body);
            png.Write(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder((int)PokeHub.Core.Patch.Crc32.HashToUInt32(body))));
        }
        var header = new byte[13];
        BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(width)).CopyTo(header, 0);
        BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(height)).CopyTo(header, 4);
        header[8] = 8; header[9] = 2;
        Chunk("IHDR", header);
        using (var z = new MemoryStream())
        {
            using (var deflate = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(raw);
            Chunk("IDAT", z.ToArray());
        }
        Chunk("IEND", []);
        File.WriteAllBytes(file, png.ToArray());
    }

    /// <summary>Com POKEHUB_WORKSPACE (HGSS): todas as espécies têm imagem e o espaço vazio não.</summary>
    [Fact]
    public void Real_workspace_every_species_has_a_sprite()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_WORKSPACE");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var sprites = PokemonSprites.Load(Gen4Game.For(project))!;
        Assert.Null(sprites.Render(0));
        Assert.Null(sprites.Render(9999));
        for (int species = 1; species <= 493; species++)
        {
            var image = sprites.Render(species);
            Assert.NotNull(image);
            int transparent = 0;
            for (int i = 3; i < image!.Pixels.Length; i += 4) if (image.Pixels[i] == 0) transparent++;
            Assert.True(transparent > 0, $"espécie {species} sem fundo: decifrada errado");
        }
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
