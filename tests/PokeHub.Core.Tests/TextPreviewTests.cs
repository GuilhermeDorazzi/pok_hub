using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;
using Xunit.Abstractions;

namespace PokeHub.Core.Tests;

public class TextPreviewTests(ITestOutputHelper output)
{
    /// <summary>Fonte com todas as larguras em 6 px e cada glifo com o primeiro pixel em cor 1.</summary>
    private static Gen4Font FakeFont()
    {
        const int Count = 500;
        var data = new byte[16 + Count * 64 + Count];
        BitConverter.TryWriteBytes(data.AsSpan(0), 16);
        BitConverter.TryWriteBytes(data.AsSpan(4), 16 + Count * 64);
        BitConverter.TryWriteBytes(data.AsSpan(8), Count);
        for (int g = 0; g < Count; g++)
        {
            data[16 + Count * 64 + g] = 6;
            data[16 + g * 64 + 1] = 0x40; // palavra 0x4000: pixel 0 da linha 0 em cor 1
        }
        return new Gen4Font(data);
    }

    private static ushort Code(string s) => CharMap.Default.TryCode(s, out var c) ? c : throw new InvalidOperationException(s);

    [Fact]
    public void Lines_are_measured_and_overflow_is_reported()
    {
        var letter = Code("A");
        var codes = Enumerable.Repeat(letter, 10).Append(Code("\\n")).Concat(Enumerable.Repeat(letter, 40)).ToList();
        var preview = TextPreview.Render(codes, FakeFont());
        Assert.Equal(2, preview.Lines.Count);
        Assert.Equal(60, preview.Lines[0].Width);
        Assert.False(preview.Lines[0].TooLong);
        Assert.Equal(240, preview.Lines[1].Width);
        Assert.True(preview.HasOverflow);
    }

    [Fact]
    public void Clear_starts_a_new_box_and_three_lines_need_two_boxes()
    {
        var font = FakeFont();
        var a = Code("A");
        var oneBox = TextPreview.Render([a, Code("\\n"), a], font);
        var cleared = TextPreview.Render([a, Code("\\f"), a], font);
        var threeLines = TextPreview.Render([a, Code("\\n"), a, Code("\\r"), a], font);
        Assert.True(cleared.Image.Height > oneBox.Image.Height);
        Assert.Equal(cleared.Image.Height, threeLines.Image.Height);
    }

    [Fact]
    public void Commands_take_no_space_except_variable_names()
    {
        var font = FakeFont();
        var a = Code("A");
        var color = TextPreview.Render([CharMap.CommandMarker, 0xFF00, 1, 3, a], font);
        Assert.Equal(6, color.Lines[0].Width);
        var name = TextPreview.Render([CharMap.CommandMarker, 0x0100, 2, 0, 0, a], font);
        Assert.Equal(48, name.Lines[0].Width);
        // comando cortado no fim não pode derrubar a prévia
        Assert.Single(TextPreview.Render([a, CharMap.CommandMarker], font).Lines);
    }

    [Fact]
    public void Truncated_font_is_refused()
    {
        var data = new byte[32];
        BitConverter.TryWriteBytes(data.AsSpan(0), 16);
        BitConverter.TryWriteBytes(data.AsSpan(8), 100);
        Assert.Throws<InvalidDataException>(() => new Gen4Font(data));
    }

    /// <summary>Todas as mensagens do jogo: a prévia nunca pode falhar, e o texto original quase sempre cabe, o que confere as larguras.</summary>
    [Theory]
    [InlineData("POKEHUB_WORKSPACE")]
    [InlineData("POKEHUB_PLATINUM_WORKSPACE")]
    public void Real_workspace_every_message_renders(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var game = Gen4Game.For(project);
        var font = Gen4Font.Load(game)!;
        var text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
        int lines = 0, over = 0;
        foreach (var file in text.Files)
            foreach (var m in Gen4TextArchive.Read(file).Messages.Where(m => !m.IsCompressed))
            {
                var preview = TextPreview.Render(m.Codes, font);
                lines += preview.Lines.Count;
                over += preview.Lines.Count(l => l.TooLong);
            }
        output.WriteLine($"{variable}: {lines} linhas, {over} passam de {TextPreview.BoxWidth} px");
        Assert.True(lines > 10000, $"{lines} linhas");
        Assert.True(over < lines / 10, $"{over} de {lines} linhas passam da caixa: largura suspeita");
    }
}
