using PokeHub.Core.Plugins;
using Xunit;

namespace PokeHub.Core.Tests;

public class MelonDsKeysTests
{
    private const string Unmapped = "[Emu]\nX = 1\n\n[Instance0.Keyboard]\nSelect = -1\nStart = -1\nRight = -1\nHK_Pause = -1\nA = -1\nY = -1\nLeft = -1\nDown = -1\nB = -1\nUp = -1\nX = -1\nL = -1\nR = -1\n\n[Instance0.Joystick]\nA = -1\nStart = -1\n";

    [Fact]
    public void Unmapped_keyboard_gets_the_defaults_and_nothing_else_changes()
    {
        var result = MelonDsKeys.WithDefaults(Unmapped)!;
        Assert.Contains("\nA = 88\n", result);
        Assert.Contains("\nStart = 16777220\n", result);
        Assert.Contains("\nUp = 16777235\n", result);
        Assert.Contains("HK_Pause = -1", result); // atalhos ficam como estão
        Assert.Contains("[Emu]\nX = 1\n", result);  // X de outra seção também
        Assert.EndsWith("[Instance0.Joystick]\nA = -1\nStart = -1\n", result);
    }

    [Fact]
    public void Any_configured_button_means_hands_off()
    {
        Assert.Null(MelonDsKeys.WithDefaults(Unmapped.Replace("\nB = -1\n", "\nB = 70\n")));
        var applied = MelonDsKeys.WithDefaults(Unmapped)!;
        Assert.Null(MelonDsKeys.WithDefaults(applied)); // aplicar de novo não muda nada
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[Emu]\nX = 1")]
    public void Missing_file_or_section_gets_a_new_section(string? toml)
    {
        var result = MelonDsKeys.WithDefaults(toml)!;
        Assert.Contains("[Instance0.Keyboard]\n", result);
        Assert.Contains("A = 88\n", result);
        Assert.Null(MelonDsKeys.WithDefaults(result));
    }

    [Fact]
    public void Windows_line_endings_and_missing_keys_are_kept_right()
    {
        var result = MelonDsKeys.WithDefaults("[Instance0.Keyboard]\r\nA = -1\r\n\r\n[Mic]\r\nX = -1\r\n")!;
        Assert.DoesNotContain("\n[Instance0", "\r" + result.Replace("\r\n", "\r"));
        Assert.Contains("A = 88\r\n", result);
        Assert.Contains("Down = 16777237\r\n", result); // botão que faltava
        Assert.EndsWith("[Mic]\r\nX = -1\r\n", result);
    }

    [Fact]
    public void Melonds_manifest_uses_the_hook()
    {
        var catalog = PluginCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "plugins"));
        Assert.Equal("melonds-default-keys", catalog.Get("melonds")!.Launch!.Hook);
        Assert.Contains("melonds-default-keys", LaunchHooks.All.Keys);
    }
}
