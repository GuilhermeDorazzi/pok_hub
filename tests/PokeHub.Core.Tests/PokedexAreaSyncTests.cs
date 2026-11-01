using PokeHub.Core.Gen4;
using Xunit;

namespace PokeHub.Core.Tests;

public class PokedexAreaSyncTests
{
    [Theory]
    [InlineData("R29", "Route 29", 0x02u, false)]
    [InlineData("T31", "Mt. Silver", 0x1Cu, false)]
    [InlineData("D41R0105", "Mt. Silver Cave", 0x10u, true)]
    [InlineData("D26R0102", "SLOWPOKE Well", 0x04u, true)]
    [InlineData("D01R0101", "DIGLETT’s Cave", 0x17u, true)]
    [InlineData("D22R0101", "National Park", 0x06u, true)]
    [InlineData("R02R0101", "Route 2", 0x2Cu, false)]
    public void Places_map_to_the_right_pokedex_id(string map, string name, uint id, bool special)
    {
        var place = PokedexAreaSync.LocationOf(map, name);
        Assert.NotNull(place);
        Assert.Equal(id, place.Value.Id);
        Assert.Equal(special, place.Value.Special);
    }

    [Fact]
    public void Places_the_pokedex_does_not_have_are_left_out() =>
        Assert.Null(PokedexAreaSync.LocationOf("D47R0102", "Safari Zone"));

    [Fact]
    public void Merge_follows_known_places_and_leaves_the_rest()
    {
        HashSet<uint> known = [1, 2, 3, 4];
        HashSet<uint> expected = [2, 3];
        HashSet<uint> keep = [4];
        // 1 saiu dos encontros, 3 é novo, 4 fica por enxame ou som, e 64 não tem par nos encontros
        var result = PokedexAreaSync.Merge([1, 2, 4, 64], known, expected, keep);
        Assert.Equal(new uint[] { 2, 3, 4, 64 }, result.OrderBy(x => x));
    }
}
