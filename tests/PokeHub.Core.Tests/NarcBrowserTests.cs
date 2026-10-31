using PokeHub.Core.Gen4;
using PokeHub.Core.Graphics;
using PokeHub.Core.Projects;
using Xunit;
using Xunit.Abstractions;

namespace PokeHub.Core.Tests;

public class NarcBrowserTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pokehub-narcs").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Nitro(string magic, string section, byte[] body)
    {
        using var ms = new MemoryStream();
        ms.Write(System.Text.Encoding.ASCII.GetBytes(magic));
        ms.Write(new byte[] { 0xFF, 0xFE, 0, 1 });
        ms.Write(BitConverter.GetBytes((uint)(24 + body.Length)));
        ms.Write(BitConverter.GetBytes((ushort)16));
        ms.Write(BitConverter.GetBytes((ushort)1));
        ms.Write(System.Text.Encoding.ASCII.GetBytes(section));
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
        BitConverter.TryWriteBytes(body.AsSpan(16 + 2), (ushort)0x001F);
        return Nitro("RLCN", "TTLP", body);
    }

    /// <summary>NCGR em tiles, 2×1, com o primeiro pixel em cor 1.</summary>
    private static byte[] Tiles()
    {
        var body = new byte[24 + 64];
        BitConverter.TryWriteBytes(body.AsSpan(0), (ushort)1);
        BitConverter.TryWriteBytes(body.AsSpan(2), (ushort)2);
        BitConverter.TryWriteBytes(body.AsSpan(4), 3u);
        BitConverter.TryWriteBytes(body.AsSpan(16), 64u);
        BitConverter.TryWriteBytes(body.AsSpan(20), 24u);
        body[24] = 0x01;
        return Nitro("RGCN", "RAHC", body);
    }

    private string WriteNarc(params byte[][] files)
    {
        var narc = new Narc();
        narc.Files.AddRange(files);
        var path = Path.Combine(_dir, "x");
        File.WriteAllBytes(path, narc.Write());
        return path;
    }

    [Fact]
    public void Kinds_come_from_the_content()
    {
        Assert.Equal("NCGR", NarcBrowser.KindOf(Tiles()));
        Assert.Equal("NCLR", NarcBrowser.KindOf(Palette()));
        Assert.Equal("BMD0", NarcBrowser.KindOf("BMD0xxxx"u8.ToArray()));
        Assert.Equal("dados", NarcBrowser.KindOf([1, 2, 3, 4, 5]));
        Assert.Equal("vazio", NarcBrowser.KindOf([]));
    }

    [Fact]
    public void Scan_finds_narcs_without_extension_and_ignores_other_files()
    {
        WriteNarc(Palette());
        Directory.CreateDirectory(Path.Combine(_dir, "a"));
        File.WriteAllBytes(Path.Combine(_dir, "a", "outro"), [1, 2, 3, 4, 5, 6]);
        File.WriteAllBytes(Path.Combine(_dir, "a", "curto"), [1]);
        Assert.Equal("x", Assert.Single(NarcBrowser.Scan(_dir)).RelativePath);
    }

    [Fact]
    public void Tiles_use_the_nearest_palette_and_dimensions_from_the_header()
    {
        var browser = NarcBrowser.Open(WriteNarc(Palette(), Tiles(), [1, 2, 3, 4, 5]));
        var image = browser.Render(1)!;
        Assert.Equal(16, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image.Pixels[..4]);
        Assert.Null(browser.Render(2)); // dados não viram imagem
    }

    [Fact]
    public void Tiles_without_a_palette_still_draw_in_gray()
    {
        var image = NarcBrowser.Open(WriteNarc(Tiles())).Render(0)!;
        Assert.Equal(image.Pixels[0], image.Pixels[1]);
        Assert.Equal(255, image.Pixels[3]);
    }

    [Fact]
    public void Replace_keeps_the_rest_and_refuses_another_kind()
    {
        var other = new byte[] { 9, 9, 9, 9, 9 };
        var path = WriteNarc(Palette(), other);
        var browser = NarcBrowser.Open(path);
        Assert.Throws<InvalidDataException>(() => browser.Replace(0, Tiles()));

        var newer = Palette();
        newer[^1] = 0x7C;
        browser.Replace(0, newer);
        var saved = Narc.Read(File.ReadAllBytes(path));
        Assert.Equal(newer, saved.Files[0]);
        Assert.Equal(other, saved.Files[1]);
    }

    /// <summary>Com POKEHUB_WORKSPACE e POKEHUB_PLATINUM_WORKSPACE: todo gráfico da ROM tem de abrir sem derrubar a janela.</summary>
    [Theory]
    [InlineData("POKEHUB_WORKSPACE")]
    [InlineData("POKEHUB_PLATINUM_WORKSPACE")]
    public void Real_workspace_every_graphic_renders_or_is_skipped(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var narcs = NarcBrowser.Scan(Gen4Game.For(project).FilesRoot);
        int graphics = 0, drawn = 0;
        foreach (var entry in narcs)
        {
            NarcBrowser browser;
            try { browser = NarcBrowser.Open(entry.FullPath); }
            catch (InvalidDataException) { continue; }
            foreach (var m in browser.Members.Where(m => m.Kind.StartsWith("NCGR") || m.Kind.StartsWith("NSCR") || m.Kind.StartsWith("NCLR")))
            {
                graphics++;
                if (browser.Render(m.Index) is not null) drawn++;
            }
        }
        output.WriteLine($"{variable}: {narcs.Count} NARCs, {graphics} gráficos, {drawn} desenhados");
        Assert.True(narcs.Count > 50);
        Assert.True(drawn > graphics * 0.9, $"só {drawn} de {graphics} gráficos desenhados");
    }
}
