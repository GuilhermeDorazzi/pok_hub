using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public class TextRepositoryTests
{
    /// <summary>Com POKEHUB_WORKSPACE: todo texto do jogo decodifica e recodifica para os mesmos códigos; editar e salvar funciona.</summary>
    [Fact]
    public void Real_workspace_every_message_roundtrips_and_edit_saves()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_WORKSPACE");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return;
        var projectDir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        var project = new Project { Paths = new ProjectPaths(projectDir), Config = ProjectConfig.Load(Path.Combine(projectDir, "project.json")) };

        var repo = TextRepository.Load(project);
        Assert.True(repo.BankCount > 500);
        Assert.Equal("PIDGEY", repo.Bank(repo.Game.SpeciesBank).Texts[16]);
        Assert.NotNull(repo.LabelOf(repo.Game.SpeciesBank));

        int messages = 0; var failures = new List<string>();
        for (int b = 0; b < repo.BankCount; b++)
        {
            var bank = repo.Bank(b);
            for (int i = 0; i < bank.Texts.Count; i++)
            {
                messages++;
                var m = bank.Archive.Messages[i];
                try
                {
                    var codes = Gen4TextArchive.Encode(bank.Texts[i]);
                    if (!codes.SequenceEqual(m.Codes)) failures.Add($"{b}:{i} códigos diferentes");
                }
                catch (Exception ex) { failures.Add($"{b}:{i} {ex.Message}"); }
            }
        }
        Assert.True(messages > 10000);
        Assert.True(failures.Count == 0, $"{failures.Count} de {messages} mensagens não fazem ida e volta: {string.Join(" | ", failures.Take(8))}");

        Assert.Contains(repo.Search("Falkner"), h => h.Bank == repo.Game.TrainerNamesBank);

        var path = repo.Game.TextArchivePath;
        var before = File.ReadAllBytes(path);
        try
        {
            Assert.False(repo.SetText(repo.Game.SpeciesBank, 16, "PIDGEY"));
            Assert.True(repo.SetText(repo.Game.SpeciesBank, 16, "POMBO"));
            Assert.True(repo.SetText(repo.Game.TrainerNamesBank, 20, "Falcao"));
            Assert.True(repo.HasChanges);
            repo.Save();
            var again = TextRepository.Load(project);
            Assert.Equal("POMBO", again.Bank(repo.Game.SpeciesBank).Texts[16]);
            Assert.Equal("Falcao", again.Bank(repo.Game.TrainerNamesBank).Texts[20]);
            Assert.True(again.IsCompressed(repo.Game.TrainerNamesBank, 20));
            Assert.Throws<InvalidDataException>(() => again.SetText(repo.Game.TrainerNamesBank, 20, "Falcão{STRVAR_1 0}"));
        }
        finally { File.WriteAllBytes(path, before); }
    }
}
