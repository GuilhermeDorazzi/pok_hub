using PokeHub.Core.Gen4;
using PokeHub.Core.Hub;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;
using Xunit;

namespace PokeHub.Core.Tests;

public class Gen4Tests
{
    [Fact]
    public void Narc_roundtrip_synthetic()
    {
        var n = new Narc();
        n.Files.Add([1, 2, 3]);
        n.Files.Add([]);
        n.Files.Add(new byte[9]);
        var bytes = n.Write();
        var back = Narc.Read(bytes);
        Assert.Equal(3, back.Files.Count);
        Assert.Equal([1, 2, 3], back.Files[0]);
        Assert.Empty(back.Files[1]);
        Assert.Equal(9, back.Files[2].Length);
        Assert.Equal(bytes, back.Write());
    }

    [Fact]
    public void Charmap_is_embedded_and_maps_latin()
    {
        var map = CharMap.Default;
        Assert.Equal("A", map.CharOf(0x012B));
        Assert.True(map.TryCode("a", out var a));
        Assert.Equal(0x0145, a);
        Assert.Equal("STRVAR_1", map.CommandOf(0x0100));
    }

    [Fact]
    public void Text_archive_roundtrip_plain_and_compressed()
    {
        var archive = new Gen4TextArchive { Key = 0x1234 };
        archive.Messages.Add(new Gen4Message { Codes = Gen4TextArchive.Encode("Hello{STRVAR_1 0}!") });
        archive.Messages.Add(new Gen4Message { Codes = Gen4TextArchive.Encode("Falkner"), IsCompressed = true });
        archive.Messages.Add(new Gen4Message { Codes = Gen4TextArchive.Encode("Ab"), IsCompressed = true });
        var bytes = archive.Write();
        var back = Gen4TextArchive.Read(bytes);
        Assert.Equal(0x1234, back.Key);
        Assert.Equal("Hello{STRVAR_1 0}!", Gen4TextArchive.Decode(back.Messages[0]));
        Assert.True(back.Messages[1].IsCompressed);
        Assert.Equal("Falkner", Gen4TextArchive.Decode(back.Messages[1]));
        Assert.Equal("Ab", Gen4TextArchive.Decode(back.Messages[2]));
        Assert.Equal(bytes, back.Write());
    }

    [Fact]
    public void Trainer_roundtrip_synthetic()
    {
        var t = new Trainer { Id = 7, Class = 3, HasItems = true, HasMoves = true, AiFlags = 0x7, DoubleBattle = true, Items = [1, 0, 17, 0] };
        t.Party.Add(new PartyMember { Species = 152, Level = 5, Difficulty = 50, Item = 4, Moves = [33, 45, 0, 0], BallSeal = 0 });
        t.Party.Add(new PartyMember { Species = 25, Form = 1, Level = 10, GenderAbility = GenderAbilityFlags.ForceFemale | GenderAbilityFlags.AbilitySlot2 });
        var data = t.SerializeData();
        var party = t.SerializeParty(hasBallSeal: true);
        Assert.Equal(20, data.Length);
        Assert.Equal(2 * 18, party.Length);
        var back = Trainer.Parse(7, data, party, hasBallSeal: true);
        Assert.Equal(2, back.Party.Count);
        Assert.True(back.HasItems && back.HasMoves && back.DoubleBattle);
        Assert.Equal(152, back.Party[0].Species);
        Assert.Equal(1, back.Party[1].Form);
        Assert.Equal(GenderAbilityFlags.ForceFemale | GenderAbilityFlags.AbilitySlot2, back.Party[1].GenderAbility);
        Assert.Equal([33, 45, 0, 0], back.Party[0].Moves);
        Assert.Equal(data, back.SerializeData());
        Assert.Equal(party, back.SerializeParty(true));

        // DP: sem ball seal, 16 bytes por Pokémon com itens e golpes
        Assert.Equal(2 * 16, t.SerializeParty(hasBallSeal: false).Length);
    }

    /// <summary>Com POKEHUB_WORKSPACE apontando para um workspace Gen 4 extraído: carrega tudo e exige que regravar não altere os NARCs.</summary>
    [Fact]
    public void Real_workspace_loads_and_rewrites_identically()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_WORKSPACE");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;

        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var config = ProjectConfig.Load(Path.Combine(projectDir, "project.json"));
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = config };

        var repo = TrainerRepository.Load(project);
        Assert.True(repo.Trainers.Count > 100);
        Assert.True(repo.Species.Count > 400);
        Assert.Contains("Falkner", repo.Names);
        var falkner = repo.Trainers[repo.Names.IndexOf("Falkner")];
        Assert.Equal("Leader", repo.ClassOf(falkner));
        Assert.Equal("PIDGEY", repo.Species[falkner.Party[0].Species]);

        var game = repo.Game;
        var before = (File.ReadAllBytes(game.TrainerDataPath), File.ReadAllBytes(game.TrainerPartyPath), File.ReadAllBytes(game.TextArchivePath));
        try
        {
            repo.Save();
            Assert.Equal(before.Item1, File.ReadAllBytes(game.TrainerDataPath));
            Assert.Equal(before.Item2, File.ReadAllBytes(game.TrainerPartyPath));
            Assert.Equal(before.Item3, File.ReadAllBytes(game.TextArchivePath));

            // banco de nomes: reler e reescrever sem mudanças deve ser idêntico
            var text = Narc.Read(before.Item3);
            var names = Gen4TextArchive.Read(text.Files[game.TrainerNamesBank]);
            Assert.Equal(text.Files[game.TrainerNamesBank], names.Write());
        }
        finally
        {
            File.WriteAllBytes(game.TrainerDataPath, before.Item1);
            File.WriteAllBytes(game.TrainerPartyPath, before.Item2);
            File.WriteAllBytes(game.TextArchivePath, before.Item3);
        }
    }
}
