using System.Diagnostics;
using PokeHub.Core.Hub;
using PokeHub.Core.Projects;
using PokeHub.Core.Runtimes;
using Xunit;

namespace PokeHub.Core.Tests;

/// <summary>Projeto cuja pasta já é um repositório: as versões do hub entram nele, sem criar outro em workspace/.</summary>
public sealed class WorkspaceGitProjectRepoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pokehub-repo-" + Guid.NewGuid().ToString("N"));

    private static string RunGit(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    private static WorkspaceGit Git(string root) => new(new RuntimeManager(new HubPaths(root), new HubConfig()));

    [Fact]
    public async Task Versions_go_to_the_project_repo_and_stay_inside_workspace()
    {
        var dir = Path.Combine(_root, "projeto");
        var paths = new ProjectPaths(dir);
        Directory.CreateDirectory(paths.Workspace);
        RunGit(dir, "init", "-q", "-b", "main");
        RunGit(dir, "config", "user.name", "Teste");
        RunGit(dir, "config", "user.email", "teste@localhost");
        File.WriteAllText(Path.Combine(dir, "fora.txt"), "fora");
        RunGit(dir, "add", "-A");
        RunGit(dir, "commit", "-q", "-m", "primeiro");
        var p = new Project { Paths = paths, Config = new ProjectConfig { Name = "projeto" } };
        var git = Git(_root);
        if (!git.IsAvailable) return;
        var log = new Progress<string>(_ => { });

        File.WriteAllText(Path.Combine(paths.Workspace, "a.bin"), "1");
        await git.EnsureRepoAsync(p, log);
        Assert.True(WorkspaceGit.UsesProjectRepo(p));
        Assert.False(Directory.Exists(Path.Combine(paths.Workspace, ".git")));
        Assert.Contains("workspace/a.bin", RunGit(dir, "ls-files"));

        // o que está fora de workspace/ não entra nas versões do hub
        File.WriteAllText(Path.Combine(dir, "fora.txt"), "mudou");
        File.WriteAllText(Path.Combine(paths.Workspace, "a.bin"), "2");
        Assert.True(await git.SnapshotAsync(p, "dois", log));
        Assert.Contains("fora.txt", RunGit(dir, "status", "--short"));

        var history = await git.HistoryAsync(p);
        Assert.DoesNotContain(history, h => h.Message == "primeiro");
        Assert.Equal(2, history.Count);
        var first = history[1].Hash;

        Assert.Contains("a.bin", await git.ChangedFilesAsync(p, first));
        Assert.Equal("1"u8.ToArray(), await git.ReadFileAtAsync(p, first, "a.bin"));

        await git.RestoreAsync(p, first, log);
        Assert.Equal("1", File.ReadAllText(Path.Combine(paths.Workspace, "a.bin")));
        Assert.Equal("mudou", File.ReadAllText(Path.Combine(dir, "fora.txt")));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
