using PokeHub.Core.Gen4.Scripts;
using Xunit;

namespace PokeHub.Core.Tests;

public class ScriptListingTests
{
    private static ushort Opcode(ScriptCommandTable table, string name) =>
        (ushort)table.Commands.Select((c, i) => (c.Name, i)).First(c => c.Name == name).i;

    private static ScriptListing Listing(int flag)
    {
        var table = ScriptCommandTable.Hgss;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(2); // o script 1 começa logo depois do fim da tabela
        w.Write((ushort)0xFD13);
        w.Write(Opcode(table, "SetFlag")); w.Write((ushort)flag);
        w.Write(Opcode(table, "GoTo")); w.Write(0); // salta para a linha seguinte
        w.Write(Opcode(table, "End"));
        return new ScriptListing(new ScriptEditor(ms.ToArray(), table), FlagNames.Hgss);
    }

    [Fact]
    public void Jump_says_which_script_and_line_it_lands_on()
    {
        var lines = Listing(1).Lines;
        Assert.Equal(["SetFlag", "GoTo", "End"], lines.Select(l => l.Instruction.Name));
        Assert.Equal([1, 2, 3], lines.Select(l => l.Line));
        Assert.Equal("Script 1", lines[0].Entry);
        Assert.Equal("", lines[1].Entry);
        Assert.Contains("Script 1", lines[1].Args);
        Assert.Contains("3", lines[1].Args);
        Assert.Contains($"{lines[2].Instruction.Offset:X4}", lines[1].Args);
    }

    [Fact]
    public void Flags_show_name_and_both_numbers_and_unnamed_keep_the_numbers()
    {
        var named = FlagNames.Hgss;
        int withName = Enumerable.Range(1, named.NumFlags - 1).First(id => named.Flag(id) is not null);
        var text = Listing(withName).Lines[0].Args;
        Assert.Contains(named.Flag(withName)!, text);
        Assert.Contains(withName.ToString(), text);
        Assert.Contains($"0x{withName:X}", text);

        Assert.Equal("672 (0x2A0)", FlagNames.Label(672, null));
        Assert.Equal("FLAG_X (672, 0x2A0)", FlagNames.Label(672, "FLAG_X"));
    }

    [Fact]
    public void Var_argument_below_the_variable_range_is_just_a_number()
    {
        var listing = Listing(1);
        Assert.Null(listing.Describe("var", 5));
        Assert.Contains("0x", listing.Describe("var", FlagNames.Hgss.VarBase));
        Assert.Null(listing.Describe("species", 25));
    }

    [Theory]
    [InlineData("672", 672)]
    [InlineData("0x2A0", 672)]
    [InlineData(" 0X2a0 ", 672)]
    public void Slot_search_takes_decimal_or_hex(string text, int id) => Assert.Equal(id, FlagNames.ParseId(text));

    [Theory]
    [InlineData("rota")]
    [InlineData("0xZZ")]
    [InlineData("")]
    public void Slot_search_ignores_other_text(string text) => Assert.Null(FlagNames.ParseId(text));
}
