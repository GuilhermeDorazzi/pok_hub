using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

public sealed class SaveCarryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("pokehub-save").FullName;
    private readonly List<string> _log = [];
    private IProgress<string> Log => new SyncLog(_log);

    private sealed class SyncLog(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Save(string name, string content, int minutesAgo)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return path;
    }

    [Fact]
    public void New_rom_gets_the_most_recently_played_save()
    {
        Save("a_1.sav", "antigo", 60);
        Save("a_2.sav", "recente", 5);
        Save("a_3.sav", "o mais antigo", 600);
        WorkspaceService.CarrySave(Path.Combine(_dir, "a_4.nds"), Log);
        Assert.Equal("recente", File.ReadAllText(Path.Combine(_dir, "a_4.sav")));
        Assert.Single(_log);
        Assert.Contains("a_2.sav", _log[0]);
        Assert.Equal("recente", File.ReadAllText(Path.Combine(_dir, "a_2.sav")));
    }

    [Fact]
    public void Existing_save_is_never_replaced()
    {
        Save("a_1.sav", "recente", 1);
        Save("a_2.sav", "meu", 60);
        WorkspaceService.CarrySave(Path.Combine(_dir, "a_2.nds"), Log);
        Assert.Equal("meu", File.ReadAllText(Path.Combine(_dir, "a_2.sav")));
        Assert.Empty(_log);
    }

    [Fact]
    public void Without_a_save_nothing_is_created()
    {
        Save("vazio.sav", "", 1);
        WorkspaceService.CarrySave(Path.Combine(_dir, "a_1.nds"), Log);
        Assert.False(File.Exists(Path.Combine(_dir, "a_1.sav")));
        Assert.Empty(_log);
    }

    [Fact]
    public void A_locked_save_only_warns()
    {
        var path = Save("a_1.sav", "aberto", 1);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            WorkspaceService.CarrySave(Path.Combine(_dir, "a_2.nds"), Log);
        Assert.False(File.Exists(Path.Combine(_dir, "a_2.sav")));
        Assert.Single(_log);
        Assert.Contains("a_1.sav", _log[0]);
    }
}
