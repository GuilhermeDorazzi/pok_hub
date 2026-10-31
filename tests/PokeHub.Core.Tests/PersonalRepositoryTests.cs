using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class PersonalRepositoryTests
{
    [Fact]
    public void Fields_map_to_the_right_bytes_and_keep_the_rest()
    {
        var raw = Enumerable.Range(0, PersonalData.Size).Select(i => (byte)i).ToArray();
        var d = new PersonalData(raw);
        Assert.Equal(0, d.Hp);
        Assert.Equal(3, d.Speed);
        Assert.Equal(4, d.SpAttack);
        Assert.Equal(22, d.Ability1);
        Assert.Equal(BitConverter.ToUInt16(raw, 12), d.Item1);
        d.Attack = 200;
        Assert.Equal(200, raw[1]);
        Assert.Equal(28, raw[28]); // TMs não mudam
    }

    [Fact]
    public void Ev_yield_uses_two_bits_each()
    {
        var d = new PersonalData(new byte[PersonalData.Size]);
        d.SetEvYield(3, 2);
        d.SetEvYield(5, 3);
        Assert.Equal(2, d.EvYield(3));
        Assert.Equal(3, d.EvYield(5));
        Assert.Equal(0, d.EvYield(0));
        d.SetEvYield(3, 9); // limite do campo
        Assert.Equal(3, d.EvYield(3));
        Assert.Equal(3, d.EvYield(5));
    }

    /// <summary>Stats conhecidos num workspace real. Só lê: as mudanças ficam na memória.</summary>
    [Theory]
    [InlineData("POKEHUB_WORKSPACE")]
    [InlineData("POKEHUB_PLATINUM_WORKSPACE")]
    public void Real_workspace_known_stats(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };
        var repo = PersonalRepository.Load(project);
        var pikachu = repo.Get(25)!;
        Assert.Equal("PIKACHU", repo.NameOf(25));
        Assert.Equal((byte)35, pikachu.Hp);
        Assert.Equal((byte)55, pikachu.Attack);
        Assert.Equal((byte)90, pikachu.Speed);
        Assert.Equal("Electric", PersonalRepository.TypeNames[pikachu.Type1]);
        Assert.Equal(534, repo.Get(6)!.Total); // Charizard
        Assert.Equal("Static", repo.Abilities[pikachu.Ability1], ignoreCase: true);
        Assert.False(repo.HasChanges);
        pikachu.Hp = 36;
        Assert.True(repo.HasChanges);
        pikachu.Hp = 35;
        Assert.False(repo.HasChanges);
    }
}
