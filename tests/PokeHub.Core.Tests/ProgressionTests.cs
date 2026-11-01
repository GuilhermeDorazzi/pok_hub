using PokeHub.Core.Batch;
using PokeHub.Core.CodeInjection;
using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Hub;
using PokeHub.Core.Projects;
using Xunit;

namespace PokeHub.Core.Tests;

[Collection("Eventos no workspace real")] // gravam no mesmo workspace: em paralelo, um apaga o que o outro criou
public class ProgressionTests
{
    private static readonly ScriptCommandTable Table = ScriptCommandTable.Hgss;

    private static Project? From(string variable)
    {
        var ws = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrEmpty(ws) || !Directory.Exists(ws)) return null;
        var dir = Path.GetDirectoryName(ws.TrimEnd('\\', '/'))!;
        return new Project { Paths = new ProjectPaths(dir), Config = ProjectConfig.Load(Path.Combine(dir, "project.json")) };
    }

    private static List<ScriptInstruction> Decode(byte[] code)
    {
        var file = new byte[6 + code.Length];
        BitConverter.TryWriteBytes(file.AsSpan(0), 2);
        BitConverter.TryWriteBytes(file.AsSpan(4), (ushort)0xFD13);
        code.CopyTo(file, 6);
        return new ScriptFile(file, Table).Routine(1)!.Instructions.OrderBy(i => i.Offset).ToList();
    }

    [Theory]
    [InlineData(0, 12, 2)]
    [InlineData(11, 12, 60)]
    [InlineData(1, 12, 7)]
    [InlineData(4, 5, 60)]
    public void Rarity_range_goes_from_common_to_rare(int slot, int slots, int expected)
    {
        var o = new EncounterBatchOptions { RarityFloor = 2, RarityCeiling = 60 };
        Assert.Equal(expected, o.LevelOf(30, slot, slots));
    }

    [Fact]
    public void Gift_quest_has_no_check_and_keeps_the_rest()
    {
        var gift = new QuestBehavior(null, 1, false, null, null, 1, 1, 5, 0, "a", "", "c", "d", "e");
        var names = Decode(NewEventService.BuildScript(Table, gift, [0, 1, 2, 3, 4], 0x900)).Select(i => i.Name).ToList();
        Assert.DoesNotContain("HasItem", names);
        Assert.DoesNotContain("PlayerHasSpecies", names);
        Assert.Contains("GiveMon", names);
        Assert.True(names.IndexOf("GetPartyCount") < names.IndexOf("GiveMon"));
    }

    [Fact]
    public void Level_limit_locks_when_any_pokemon_is_above_it()
    {
        var code = NewEventService.BuildScript(Table, new TalkBehavior("x"), [0, 1], null, condition: new NpcCondition("y", MaxPartyLevel: 30));
        var ins = Decode(code);
        var count = ins.Single(i => i.Name == "PartyCountMonsAtOrBelowLevel");
        Assert.Equal(30, count.Args[1].Value);
        var compare = ins[ins.IndexOf(count) + 1];
        Assert.Equal("CompareVarToVar", compare.Name);
        Assert.Equal(0, ins[ins.IndexOf(compare) + 1].Args[0].Value); // menor que o time inteiro: trancado
    }

    [Fact]
    public void Level_source_has_kanto_table_and_skips_the_multiplier_for_it()
    {
        var source = PostEliteService.BuildLevelSource(130, 0, [], [0, 1, 2], PostEliteService.DefaultKantoTargets, 5);
        Assert.Contains("kanto[] = { 0,1,2 }", source);
        Assert.Contains("targets[] = { 45, 48, 50, 53, 55, 57, 59, 62, 65 }", source);
        Assert.True(source.IndexOf("return;\n", source.IndexOf("kanto[id] == 1"), StringComparison.Ordinal) is var at && (at < 0 ? source.IndexOf("return;\r\n", source.IndexOf("kanto[id] == 1"), StringComparison.Ordinal) : at) < source.IndexOf("CheckGameClearFlag(Save_VarsFlags_Get(save))", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => PostEliteService.ParseTargets("45,48"));
        Assert.Equal(9, PostEliteService.ParseTargets("1 2 3 4 5 6 7 8 9").Count);
    }

    [Fact]
    public void Real_kanto_trainers_are_found_and_johto_stays_out()
    {
        if (From("POKEHUB_TABLES_SS") is not { } project) return;
        var kinds = PostEliteService.KantoTrainers(project);
        var repo = TrainerRepository.Load(project);
        var leaders = Enumerable.Range(0, kinds.Length).Where(i => kinds[i] == 2).Select(i => repo.NameOf(repo.Trainers[i])).ToList();
        foreach (var name in new[] { "Brock", "Misty", "Erika", "Sabrina" }) Assert.Contains(name, leaders);
        Assert.DoesNotContain("Falkner", leaders);
        Assert.DoesNotContain("Whitney", leaders);
        Assert.True(kinds.Count(k => k == 1) > 50, $"rota: {kinds.Count(k => k == 1)}");
        foreach (var rematch in new[] { 489, 672, 701, 720 }) Assert.Equal(0, kinds[rematch]);
        Assert.Equal(0, kinds[0]);
    }

    [Fact]
    public void Real_batches_preview_and_apply_the_same()
    {
        if (From("POKEHUB_TABLES_SS") is not { } project) return;
        var trainers = TrainerRepository.Load(project);
        var options = new TrainerBatchOptions { NameFilter = "Falkner", Difficulty = 200 };
        var preview = BatchEditor.PreviewTrainers(trainers, options);
        Assert.NotEmpty(preview);
        Assert.Equal(preview, BatchEditor.ApplyTrainers(trainers, options));
        Assert.All(trainers.Trainers.Where(t => trainers.NameOf(t) == "Falkner").SelectMany(t => t.Party), m => Assert.Equal(200, m.Difficulty));

        var personal = PersonalRepository.Load(project);
        int pidgey = personal.Get(16)!.ExpYield;
        var exp = new SpeciesBatchOptions { ExpPercent = 70 };
        var lines = BatchEditor.PreviewSpecies(personal, exp);
        Assert.Equal(pidgey, personal.Get(16)!.ExpYield);
        Assert.Equal(lines, BatchEditor.ApplySpecies(personal, exp));
        Assert.Equal((int)Math.Round(pidgey * 0.7, MidpointRounding.AwayFromZero), personal.Get(16)!.ExpYield);

        var encounters = EncounterRepository.Load(project);
        var wild = new EncounterBatchOptions { PlaceFilter = "R01", RarityFloor = 2, RarityCeiling = 45 };
        Assert.Equal(BatchEditor.PreviewEncounters(encounters, wild), BatchEditor.ApplyEncounters(encounters, wild));
        var route = encounters.Entries.First(e => e.Maps.Any(m => m.InternalName == "R01")).Hgss!;
        Assert.Equal(2, route.WalkingLevels[0]);
        Assert.Equal(45, route.WalkingLevels[11]);
        var surfBefore = route.Surf.Select(s => s.MaxLevel).ToList();
        BatchEditor.ApplyEncounters(encounters, new EncounterBatchOptions { PlaceFilter = "R01", RarityFloor = 30, RarityCeiling = 40, GrassOnly = true });
        Assert.Equal(30, route.WalkingLevels[0]);
        Assert.Equal(surfBefore, route.Surf.Select(s => s.MaxLevel));
        // nada foi gravado: os repositórios ficam só na memória
    }

    /// <summary>Compila de verdade com a tabela de Kanto. Opcional: POKEHUB_MSYS2_HUB_ROOT aponta para o hub com o MSYS2 instalado.</summary>
    [Fact]
    public async Task Real_kanto_badges_patch_compiles()
    {
        var hubRoot = Environment.GetEnvironmentVariable("POKEHUB_MSYS2_HUB_ROOT");
        if (hubRoot is null || From("POKEHUB_TABLES_SCRATCH") is not { } project || !PostEliteService.CanScaleLevels(project)) return;
        var service = new PostEliteService(new HubPaths(hubRoot));
        var log = new Progress<string>(_ => { });
        var settingsFile = Path.Combine(project.Paths.Workspace, PostEliteSettings.FileName);
        var saved = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
        try
        {
            var settings = await service.ApplyLevelsAsync(project, true, 130, 0, [], log, default, true);
            Assert.True(settings.KantoBadges);
            Assert.Contains(CodePatchService.LoadLedger(project).Regions, r => r.Patch == PostEliteService.PatchName);
        }
        finally
        {
            await service.ApplyLevelsAsync(project, false, 130, 0, [], log);
            if (saved is null) File.Delete(settingsFile);
            else File.WriteAllText(settingsFile, saved);
        }
    }
}
