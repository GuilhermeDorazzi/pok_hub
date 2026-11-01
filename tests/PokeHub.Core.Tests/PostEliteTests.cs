using PokeHub.Core.CodeInjection;
using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Hub;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

[Collection("Eventos no workspace real")] // gravam no mesmo workspace: em paralelo, um apaga o que o outro criou
public class PostEliteTests
{
    private static Project? Scratch()
    {
        var ws = Environment.GetEnvironmentVariable("POKEHUB_TABLES_SCRATCH");
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return null;
        var dir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        return new Project { Paths = new ProjectPaths(dir), Config = ProjectConfig.Load(Path.Combine(dir, "project.json")) };
    }

    [Fact]
    public void Ids_accept_lists_and_ranges()
    {
        Assert.Equal([1, 701, 702, 703], PostEliteService.ParseIds("703, 1 701-702"));
        Assert.Empty(PostEliteService.ParseIds(" "));
        Assert.Throws<ArgumentException>(() => PostEliteService.ParseIds("abc"));
    }

    [Fact]
    public void Reset_script_clears_each_trainer_and_returns()
    {
        var code = PostEliteService.BuildResetScript(ScriptCommandTable.Hgss, [5, 9]);
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        var ins = new ScriptFile(file, ScriptCommandTable.Hgss).Routine(1)!.Instructions.OrderBy(i => i.Offset).ToList();
        Assert.Equal(["ClearTrainerFlag", "ClearTrainerFlag", "Return"], ins.Select(i => i.Name));
        Assert.Equal(9, ins[1].Args[0].Value);
    }

    [Fact]
    public void Level_source_keeps_the_rules_in_one_place()
    {
        var source = PostEliteService.BuildLevelSource(130, 40, [705, 701]);
        Assert.Contains("#define PERCENT 130u", source);
        Assert.Contains("#define FLOOR 40u", source);
        Assert.Contains("701, 705, 65535", source);
        // a conta do gancho, em números: 15 × 1,3 ≈ 20; 88 × 1,3 ≈ 114, e o limite fica no gancho
        Assert.Equal(20u, (15u * 130u * 5243u + (1u << 18)) >> 19);
        Assert.Equal(114u, (88u * 130u * 5243u + (1u << 18)) >> 19);
        Assert.Equal(50u, (50u * 100u * 5243u + (1u << 18)) >> 19);
    }

    [Fact]
    public void Real_reset_is_written_once_and_turns_off_without_leaving_flags_behind()
    {
        if (Scratch() is not { } project) return;
        var game = Gen4Game.For(project);
        var settingsFile = Path.Combine(project.Paths.Workspace, PostEliteSettings.FileName);
        var saved = File.ReadAllBytes(game.ScriptsPath);
        var savedSettings = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        try
        {
            int total = TrainerRepository.Load(project).Trainers.Count;
            int count = PostEliteService.ApplyReset(project, true, [1, 2]);
            Assert.Equal(total - 3, count); // o registro 0 e os dois de fora

            var bank = MapHeaders.Load(game).First(h => h.InternalName == "T10R0701").ScriptsBank;
            var entry = PostEliteSettings.Load(project).ResetEntry;
            Assert.True(entry > 0);

            List<ScriptInstruction> Read() => ScriptArchive.Load(project).Open(bank).Instructions();
            int Calls(List<ScriptInstruction> list)
            {
                int at = list.FindIndex(i => i.Name == "HOFCredits");
                return list[at - 1].Name == "Call" ? 1 : 0;
            }
            var ins = Read();
            Assert.Equal(1, Calls(ins));
            Assert.Equal(count, ins.Count(i => i.Name == "ClearTrainerFlag"));
            Assert.DoesNotContain(ins, i => i.Name == "ClearTrainerFlag" && i.Args[0].Value == 1);

            // aplicar de novo não acrescenta outro comando; o script fica o mais novo, mesmo se crescer
            PostEliteService.ApplyReset(project, true, [1]);
            Assert.Equal(entry, PostEliteSettings.Load(project).ResetEntry);
            ins = Read();
            Assert.Equal(count + 1, ins.Count(i => i.Name == "ClearTrainerFlag"));
            int size = File.ReadAllBytes(game.ScriptsPath).Length;
            PostEliteService.ApplyReset(project, true, [1, 2, 3]);
            Assert.Equal(size, File.ReadAllBytes(game.ScriptsPath).Length);
            Assert.Equal(count - 1, Read().Count(i => i.Name == "ClearTrainerFlag"));
            PostEliteService.ApplyReset(project, true, [1]);
            ins = Read();
            Assert.Equal(1, ins.Count(i => i.Name == "Call" && ins[ins.IndexOf(i) + 1].Name == "HOFCredits"));

            PostEliteService.ApplyReset(project, false, []);
            Assert.DoesNotContain(Read(), i => i.Name == "ClearTrainerFlag");
            Assert.False(PostEliteSettings.Load(project).ResetTrainers);
        }
        finally
        {
            File.WriteAllBytes(game.ScriptsPath, saved);
            if (savedSettings is null) File.Delete(settingsFile);
            else File.WriteAllText(settingsFile, savedSettings);
        }
    }

    [Fact]
    public void Real_ilex_shrine_checks_the_species_and_can_go_back()
    {
        if (Scratch() is not { } project) return;
        var game = Gen4Game.For(project);
        var settingsFile = Path.Combine(project.Paths.Workspace, PostEliteSettings.FileName);
        var saved = File.ReadAllBytes(game.ScriptsPath);
        var savedSettings = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        try
        {
            var bank = MapHeaders.Load(game).First(h => h.InternalName == "D36R0101").ScriptsBank;
            List<ScriptInstruction> Read() => ScriptArchive.Load(project).Open(bank).Instructions();
            var before = Read();
            int at = before.FindIndex(i => i.Name == "FollowerPokeIsEventTrigger" && i.Args[0].Value == 3);
            var after = before.Skip(at + 1).Take(3).Select(i => i.Name).ToList();
            int resultVar = (int)before[at].Args[2].Value;

            PostEliteService.ApplyIlex(project, true);
            var open = Read();
            Assert.DoesNotContain(open, i => i.Name == "FollowerPokeIsEventTrigger" && i.Args[0].Value == 3);
            // os outros eventos do mapa continuam iguais
            Assert.Contains(open, i => i.Name == "FollowerPokeIsEventTrigger" && i.Args[0].Value == 0);
            var species = open.Single(i => i.Name == "GetPartyMonSpecies" && i.Args[1].Value == resultVar);
            var call = open.First(i => i.Name == "Call" && i.Args[0].Value == PostEliteRoutine(project, bank));
            Assert.Equal(after, open.SkipWhile(i => i != call).Skip(1).Take(3).Select(i => i.Name));
            Assert.Equal(251, open[open.IndexOf(species) + 1].Args[1].Value);

            // desligar devolve a checagem original, dentro do script novo
            int size = File.ReadAllBytes(game.ScriptsPath).Length;
            PostEliteService.ApplyIlex(project, false);
            Assert.Equal(size, File.ReadAllBytes(game.ScriptsPath).Length);
            Assert.Contains(Read(), i => i.Name == "FollowerPokeIsEventTrigger" && i.Args[0].Value == 3);
            Assert.False(PostEliteSettings.Load(project).OpenIlexShrine);
            PostEliteService.ApplyIlex(project, true);
            Assert.DoesNotContain(Read(), i => i.Name == "FollowerPokeIsEventTrigger" && i.Args[0].Value == 3);
        }
        finally
        {
            File.WriteAllBytes(game.ScriptsPath, saved);
            if (savedSettings is null) File.Delete(settingsFile);
            else File.WriteAllText(settingsFile, savedSettings);
        }
    }

    private static long PostEliteRoutine(Project project, int bank) =>
        ScriptArchive.Load(project).Open(bank).File.EntryOffsets[PostEliteSettings.Load(project).IlexEntry - 1];

    [Fact]
    public async Task Levels_already_off_ask_for_no_compiler()
    {
        if (Scratch() is not { } project) return;
        var settingsFile = Path.Combine(project.Paths.Workspace, PostEliteSettings.FileName);
        var savedSettings = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        var empty = Path.Combine(Path.GetTempPath(), "pokehub-sem-msys2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var settings = await new PostEliteService(new HubPaths(empty)).ApplyLevelsAsync(project, false, 130, 0, [], new Progress<string>(_ => { }));
            Assert.False(settings.ScaleLevels);
        }
        finally
        {
            Directory.Delete(empty, true);
            if (savedSettings is null) File.Delete(settingsFile);
            else File.WriteAllText(settingsFile, savedSettings);
        }
    }

    /// <summary>Compila de verdade e confere o gancho. Opcional: POKEHUB_MSYS2_HUB_ROOT aponta para o hub com o MSYS2 instalado.</summary>
    [Fact]
    public async Task Real_level_patch_compiles_and_lands_on_the_party_read()
    {
        var hubRoot = Environment.GetEnvironmentVariable("POKEHUB_MSYS2_HUB_ROOT");
        if (hubRoot is null || Scratch() is not { } project || !PostEliteService.CanScaleLevels(project)) return;
        var service = new PostEliteService(new HubPaths(hubRoot));
        var hook = PostEliteService.BuildLevelManifest().Hooks[0];
        var original = CodePatchService.OriginalBytes(project, hook);
        Assert.Equal(CodePatch.ParseHex(hook.Expect!), original);
        var messages = new List<string>();
        var log = new Progress<string>(messages.Add);
        var arm9 = Path.Combine(project.Paths.Workspace, "arm9", "arm9.bin");
        try
        {
            await service.ApplyLevelsAsync(project, true, 130, 0, [701], log);
            var written = File.ReadAllBytes(arm9).AsSpan(0x7363A, 4).ToArray();
            Assert.NotEqual(original, written);
            Assert.Contains(CodePatchService.LoadLedger(project).Regions, r => r.Patch == PostEliteService.PatchName);
        }
        finally
        {
            await service.ApplyLevelsAsync(project, false, 130, 0, [], log);
        }
        Assert.Equal(original, File.ReadAllBytes(arm9).AsSpan(0x7363A, 4).ToArray());
    }
}
