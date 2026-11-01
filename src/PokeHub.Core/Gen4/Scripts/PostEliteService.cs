using System.Text;
using System.Text.Json;
using PokeHub.Core.CodeInjection;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4.Scripts;

/// <summary>O que está ligado no projeto. Fica no workspace para entrar nas versões.</summary>
public sealed class PostEliteSettings
{
    public const string FileName = "pokehub-post-elite.json";

    public bool ResetTrainers { get; set; }
    public List<int> ResetSkip { get; set; } = [];
    /// <summary>Script novo criado no mapa do Hall da Fama; zero enquanto não houver.</summary>
    public int ResetEntry { get; set; }
    /// <summary>Bytes reservados para esse script: quem couber grava por cima, sem o arquivo crescer a cada aplicação.</summary>
    public int ResetCapacity { get; set; }
    public bool OpenIlexShrine { get; set; }
    public int IlexPartyVar { get; set; }
    public int IlexResultVar { get; set; }
    /// <summary>Script novo no mapa do santuário; zero enquanto não houver. Depois de criado, só troca de conteúdo.</summary>
    public int IlexEntry { get; set; }
    public bool ScaleLevels { get; set; }
    public int LevelPercent { get; set; } = 130;
    public int LevelFloor { get; set; }
    public List<int> LevelSkip { get; set; } = [];
    public bool KantoBadges { get; set; }
    public List<int> KantoTargets { get; set; } = [.. PostEliteService.DefaultKantoTargets];
    public int KantoRouteGap { get; set; } = PostEliteService.DefaultRouteGap;
    /// <summary>Treinadores que sobem como líderes em Kanto, sem a redução das rotas: chefes de uma área, que o jogador vai procurar.</summary>
    public List<int> AreaLeaders { get; set; } = [];

    public static PostEliteSettings Load(Project p)
    {
        var file = Path.Combine(p.Paths.Workspace, FileName);
        return File.Exists(file) ? JsonSerializer.Deserialize<PostEliteSettings>(File.ReadAllText(file), HubConfig.JsonOptions) ?? new() : new();
    }

    public void Save(Project p) => File.WriteAllText(Path.Combine(p.Paths.Workspace, FileName), JsonSerializer.Serialize(this, HubConfig.JsonOptions));
}

/// <summary>
/// O que acontece ao vencer a Elite. Liberar os treinadores é script e serve em qualquer formato de HG/SS; os níveis mudam só em
/// batalha, depois do fim do jogo, por um gancho no ponto em que o jogo lê o time. Por isso pedem ds-rom e o SoulSilver US.
/// </summary>
public sealed class PostEliteService(HubPaths hub)
{
    public const string PatchName = "post-elite-levels";
    public const int MinPercent = 100, MaxPercent = 300, DefaultRouteGap = 5, KantoBadgeSteps = 9;
    /// <summary>Nível do Pokémon mais forte do líder, com 0 a 8 insígnias de Kanto. Ponto de partida, para ajustar jogando.</summary>
    public static readonly IReadOnlyList<int> DefaultKantoTargets = [45, 48, 50, 53, 55, 57, 59, 62, 65];
    // times de rebatalha do jogo: já têm o nível próprio
    private static readonly int[] RematchTeams = [.. Enumerable.Range(489, 3), .. Enumerable.Range(672, 3), .. Enumerable.Range(701, 5), .. Enumerable.Range(712, 16)];
    private const byte KantoRoute = 1, KantoLeader = 2;
    // contam como região 1, mas são o caminho até a Liga: o jogador passa por eles antes de vencer a Elite
    private static readonly string[] BeforeEliteMaps = ["T10", "D43", "D45", "R26", "R27"];
    private static readonly string[] LeagueClasses = ["Elite", "Champion"];
    private const int VarBase = 0x4000, StandardTrainer = 3000, DoubleTrainer = 5000, LastSightScript = 7000;
    private const string EliteMap = "T10R0701", SupportedGame = "IPGE";
    private const int EliteCredits = 0;
    private const string IlexMap = "D36R0101";
    private const int CelebiEvent = 3, Celebi = 251, ConditionEqual = 1;
    // endereços do SoulSilver US, conferidos no arm9 contra pokeheartgold
    private const string ReadPartyCall = "0x0207363A", ReadPartyCallBytes = "FFF7D5FF";

    public static bool CanScaleLevels(Project p) => p.Config.GameCode == SupportedGame && WorkspaceService.FormatOf(p) == WorkspaceFormat.DsRom;

    private static void RequireHgss(Project p)
    {
        if (!Gen4Game.For(p).IsHgss) throw new NotSupportedException(Loc.T("core.postElite.hgssOnly"));
    }

    /// <summary>Corpo do script: limpa a marca de vencido de cada treinador, um a um, e volta para o Hall da Fama. Sem nenhum, só volta.</summary>
    public static byte[] BuildResetScript(ScriptCommandTable table, IEnumerable<int> trainers)
    {
        var s = new ScriptBuilder(table);
        foreach (var id in trainers) s.Emit("ClearTrainerFlag", id);
        return s.Emit("Return").Build();
    }

    /// <summary>Liga ou desliga. O script novo fica no arquivo e só troca de conteúdo; assim o comando no Hall da Fama é gravado uma vez só.</summary>
    public static int ApplyReset(Project p, bool on, IReadOnlyCollection<int> skip)
    {
        RequireHgss(p);
        var settings = PostEliteSettings.Load(p);
        var game = Gen4Game.For(p);
        var header = MapHeaders.Load(game).FirstOrDefault(h => h.InternalName == EliteMap) ?? throw new InvalidDataException(Loc.T("core.postElite.noHall"));
        var ids = on ? TrainerRepository.Load(p).Trainers.Select(t => t.Id).Where(id => id > 0 && !skip.Contains(id)).ToList() : [];

        var archive = ScriptArchive.Load(p);
        var editor = archive.Open(header.ScriptsBank);
        var code = BuildResetScript(editor.Table, ids);
        ScriptInstruction Credits() => editor.Instructions().FirstOrDefault(i => i.Name == "HOFCredits" && i.Args[0].Value == EliteCredits)
            ?? throw new InvalidDataException(Loc.T("core.postElite.noHall"));
        if (settings.ResetEntry > 0 && code.Length <= settings.ResetCapacity)
            editor.Overwrite(editor.File.EntryOffsets[settings.ResetEntry - 1], code);
        else if (settings.ResetEntry > 0)
        {
            // não coube: o script vai para o fim do arquivo, e o comando do Hall da Fama precisa apontar para lá
            var credits = Credits();
            var call = editor.Instructions().Last(i => i.Offset < credits.Offset && i.Name == "Call");
            editor.ReplaceEntry(settings.ResetEntry, code);
            editor.PatchJump(call.Args[0].At, editor.File.EntryOffsets[settings.ResetEntry - 1]);
            settings.ResetCapacity = code.Length;
        }
        else if (on)
        {
            settings.ResetEntry = editor.AddEntry(code);
            settings.ResetCapacity = code.Length;
            int call = Enumerable.Range(0, editor.Table.Commands.Count).First(i => editor.Table.Commands[i].Name == "Call");
            editor.Insert(Credits().Offset, call, [editor.File.EntryOffsets[settings.ResetEntry - 1]]);
        }
        archive.Save();
        settings.ResetTrainers = on;
        settings.ResetSkip = [.. skip.Order()];
        settings.Save(p);
        return ids.Count;
    }

    /// <summary>A checagem que vale no santuário: a original, que exige a marca de distribuição, ou só a espécie.</summary>
    public static byte[] BuildIlexCheck(ScriptCommandTable table, bool open, int partyVar, int resultVar)
    {
        var s = new ScriptBuilder(table);
        if (!open) return s.Emit("FollowerPokeIsEventTrigger", CelebiEvent, partyVar, resultVar).Emit("Return").Build();
        return s.Emit("GetPartyMonSpecies", partyVar, resultVar)
            .Emit("CompareVarToValue", resultVar, Celebi).Emit("GoToIf", ConditionEqual, "yes")
            .Emit("SetVar", resultVar, 0).Emit("Return")
            .Label("yes").Emit("SetVar", resultVar, 1).Emit("Return").Build();
    }

    /// <summary>
    /// O evento do Giovanni pede um Celebi de distribuição, que não existe na ROM. Ligado, um Celebi na frente do time abre o evento,
    /// inclusive o dado por uma Missão. A primeira vez troca o comando por uma chamada; as seguintes só mudam o conteúdo do script novo.
    /// </summary>
    public static void ApplyIlex(Project p, bool on)
    {
        RequireHgss(p);
        var settings = PostEliteSettings.Load(p);
        if (!on && settings.IlexEntry == 0) return;
        var header = MapHeaders.Load(Gen4Game.For(p)).FirstOrDefault(h => h.InternalName == IlexMap) ?? throw new InvalidDataException(Loc.T("core.postElite.noShrine"));
        var archive = ScriptArchive.Load(p);
        var editor = archive.Open(header.ScriptsBank);
        if (settings.IlexEntry == 0)
        {
            var check = editor.Instructions().FirstOrDefault(i => i.Name == "FollowerPokeIsEventTrigger" && i.Args[0].Value == CelebiEvent)
                ?? throw new InvalidDataException(Loc.T("core.postElite.noShrine"));
            settings.IlexPartyVar = (int)check.Args[1].Value;
            settings.IlexResultVar = (int)check.Args[2].Value;
            settings.IlexEntry = editor.AddEntry(BuildIlexCheck(editor.Table, on, settings.IlexPartyVar, settings.IlexResultVar));
            check = editor.Instructions().First(i => i.Name == "FollowerPokeIsEventTrigger" && i.Args[0].Value == CelebiEvent && i.Offset < editor.File.EntryOffsets[settings.IlexEntry - 1]);
            int call = Enumerable.Range(0, editor.Table.Commands.Count).First(i => editor.Table.Commands[i].Name == "Call");
            int at = check.Offset;
            editor.Insert(at, call, [editor.File.EntryOffsets[settings.IlexEntry - 1]]);
            var old = editor.Instructions().First(i => i.Name == "FollowerPokeIsEventTrigger" && i.Args[0].Value == CelebiEvent && i.Offset > at && i.Offset < editor.File.EntryOffsets[settings.IlexEntry - 1]);
            editor.Remove(old.Offset);
        }
        else
            editor.Overwrite(editor.File.EntryOffsets[settings.IlexEntry - 1], BuildIlexCheck(editor.Table, on, settings.IlexPartyVar, settings.IlexResultVar));
        archive.Save();
        settings.OpenIlexShrine = on;
        settings.Save(p);
    }

    /// <summary>
    /// Quem luta em Kanto, pelos mapas de região 1: treinadores com visão nos objetos e batalhas nos scripts. Líder é quem tem classe de líder.
    /// Devolve um valor por treinador: 0 fora de Kanto, 1 rota, 2 líder.
    /// </summary>
    public static byte[] KantoTrainers(Project p)
    {
        var game = Gen4Game.For(p);
        var repo = TrainerRepository.Load(p);
        var events = Narc.Read(File.ReadAllBytes(game.EventsPath));
        var archive = ScriptArchive.Load(p);
        var found = new HashSet<int>();
        var files = new HashSet<int>();
        foreach (var header in MapHeaders.Load(game).Where(h => h.Region == 1 && !BeforeEliteMaps.Any(h.InternalName.StartsWith)))
        {
            if (header.EventsBank >= 0 && header.EventsBank < events.Files.Count)
                foreach (var o in new EventFile(events.Files[header.EventsBank]).Objects)
                {
                    if (o.ScriptId >= DoubleTrainer) found.Add(o.ScriptId - DoubleTrainer + 1);
                    else if (o.ScriptId >= StandardTrainer && o.ScriptId < LastSightScript) found.Add(o.ScriptId - StandardTrainer + 1);
                }
            if (header.ScriptsBank >= 0) files.Add(header.ScriptsBank);
        }
        foreach (var file in files)
            foreach (var i in archive.Open(file).Instructions())
                if (i.Name == "TrainerBattle" && i.Args[0].Value is > 0 and < VarBase) found.Add((int)i.Args[0].Value);

        var kinds = new byte[repo.Trainers.Count];
        foreach (var id in found.Where(id => id > 0 && id < kinds.Length).Except(RematchTeams).Where(id => !LeagueClasses.Any(repo.ClassOf(repo.Trainers[id]).Contains)))
            kinds[id] = repo.ClassOf(repo.Trainers[id]).Contains("Leader", StringComparison.OrdinalIgnoreCase) ? KantoLeader : KantoRoute;
        foreach (var id in PostEliteSettings.Load(p).AreaLeaders)
            if (id > 0 && id < kinds.Length && kinds[id] != 0) kinds[id] = KantoLeader;
        return kinds;
    }

    /// <summary>
    /// O código do gancho. Treinador de Kanto recebe o nível pelas insígnias, mantendo a diferença interna do time; os outros, o multiplicador
    /// depois do fim do jogo. Kanto só se alcança depois da Elite, então os dois juntos acumulariam. Sem biblioteca C, a divisão por 100 é uma multiplicação: 5243 / 2^19.
    /// </summary>
    public static string BuildLevelSource(int percent, int floor, IReadOnlyCollection<int> skip, byte[]? kanto = null, IReadOnlyList<int>? targets = null, int routeGap = DefaultRouteGap)
    {
        var list = string.Join(", ", skip.Order().Append(0xFFFF));
        var kinds = kanto is { Length: > 0 } ? string.Join(",", kanto) : "0";
        var levels = string.Join(", ", targets ?? DefaultKantoTargets);
        return new StringBuilder()
            .AppendLine("// Gerado pelo hub (Depois da Elite). Depois do fim do jogo, os times saem da leitura com outro nível.")
            .AppendLine("#define NARC_TRAINER_PARTY 56")
            .AppendLine("#define TRAINER_DATA_SIZE 32")
            .AppendLine($"#define PERCENT {percent}u")
            .AppendLine($"#define FLOOR {floor}u")
            .AppendLine("#define MAX_LEVEL 100u")
            .AppendLine($"#define ROUTE_GAP {routeGap}")
            .AppendLine($"#define KANTO_COUNT {(kanto is { Length: > 0 } ? kanto.Length : 0)}u")
            .AppendLine("#define PROFILE_KANTO_BADGES 31")
            .AppendLine()
            .AppendLine("extern void ReadWholeNarcMemberByIdPair(void *dest, int narc, int member);")
            .AppendLine("extern void TrainerData_ReadTrData(unsigned id, void *dest);")
            .AppendLine("extern void *SaveData_Get(void);")
            .AppendLine("extern void *Save_VarsFlags_Get(void *save);")
            .AppendLine("extern int CheckGameClearFlag(void *flags);")
            .AppendLine("extern unsigned char *Save_PlayerData_GetProfile(void *save);")
            .AppendLine()
            .AppendLine($"static const unsigned short skip[] = {{ {list} }};")
            .AppendLine($"static const unsigned char kanto[] = {{ {kinds} }};")
            .AppendLine($"static const unsigned char targets[] = {{ {levels} }};")
            .AppendLine()
            .AppendLine("void PostElite_ReadParty(unsigned id, unsigned char *party)")
            .AppendLine("{")
            .AppendLine("    ReadWholeNarcMemberByIdPair(party, NARC_TRAINER_PARTY, id);")
            .AppendLine("    for (int i = 0; skip[i] != 0xFFFF; i++)")
            .AppendLine("        if (skip[i] == id) return;")
            .AppendLine("    void *save = SaveData_Get();")
            .AppendLine("    unsigned char data[TRAINER_DATA_SIZE];")
            .AppendLine("    TrainerData_ReadTrData(id, data);")
            .AppendLine("    // nível, espécie e o resto: 8 bytes, mais golpes e item se o tipo do treinador tiver")
            .AppendLine("    unsigned size = 8u + ((data[0] & 1) ? 8u : 0u) + ((data[0] & 2) ? 2u : 0u);")
            .AppendLine("    if (id < KANTO_COUNT && kanto[id])")
            .AppendLine("    {")
            .AppendLine("        unsigned bits = Save_PlayerData_GetProfile(save)[PROFILE_KANTO_BADGES], badges = 0, top = 0;")
            .AppendLine("        for (; bits; bits >>= 1) badges += bits & 1u;")
            .AppendLine("        for (unsigned k = 0; k < data[3]; k++)")
            .AppendLine("        {")
            .AppendLine("            unsigned level = party[k * size + 2];")
            .AppendLine("            if (level > top) top = level;")
            .AppendLine("        }")
            .AppendLine("        int target = targets[badges] - (kanto[id] == 1 ? ROUTE_GAP : 0);")
            .AppendLine("        for (unsigned k = 0; k < data[3]; k++)")
            .AppendLine("        {")
            .AppendLine("            int level = (int)party[k * size + 2] + target - (int)top;")
            .AppendLine("            if (level < 1) level = 1;")
            .AppendLine("            if (level > (int)MAX_LEVEL) level = MAX_LEVEL;")
            .AppendLine("            party[k * size + 2] = (unsigned char)level;")
            .AppendLine("            party[k * size + 3] = 0;")
            .AppendLine("        }")
            .AppendLine("        return;")
            .AppendLine("    }")
            .AppendLine("    if (PERCENT == 100u && FLOOR == 0u) return;")
            .AppendLine("    if (!CheckGameClearFlag(Save_VarsFlags_Get(save))) return;")
            .AppendLine("    for (unsigned k = 0; k < data[3]; k++)")
            .AppendLine("    {")
            .AppendLine("        unsigned char *mon = party + k * size;")
            .AppendLine("        unsigned level = mon[2] | ((unsigned)mon[3] << 8);")
            .AppendLine("        level = (level * PERCENT * 5243u + (1u << 18)) >> 19;")
            .AppendLine("        if (level < FLOOR) level = FLOOR;")
            .AppendLine("        if (level > MAX_LEVEL) level = MAX_LEVEL;")
            .AppendLine("        mon[2] = (unsigned char)level;")
            .AppendLine("        mon[3] = 0;")
            .AppendLine("    }")
            .AppendLine("}")
            .ToString();
    }

    public static PatchManifest BuildLevelManifest() => new()
    {
        Description = Loc.T("core.postElite.patchDescription"),
        Enabled = true,
        GameCodes = [SupportedGame],
        Symbols = new()
        {
            ["ReadWholeNarcMemberByIdPair"] = "0x02007509",
            ["TrainerData_ReadTrData"] = "0x020735D9",
            ["SaveData_Get"] = "0x020272B1",
            ["Save_VarsFlags_Get"] = "0x020503D1",
            ["CheckGameClearFlag"] = "0x02066615",
            ["Save_PlayerData_GetProfile"] = "0x02028E9D",
        },
        Hooks = [new PatchHook { Target = "arm9", Address = ReadPartyCall, Type = "call", Symbol = "PostElite_ReadParty", Expect = ReadPartyCallBytes }],
    };

    /// <summary>Grava o patch e compila. Desligar mantém a pasta inativa e tira o gancho do jogo.</summary>
    public async Task<PostEliteSettings> ApplyLevelsAsync(Project p, bool on, int percent, int floor, IReadOnlyCollection<int> skip, IProgress<string> log, CancellationToken ct = default, bool kantoBadges = false, IReadOnlyList<int>? targets = null, int routeGap = DefaultRouteGap)
    {
        RequireHgss(p);
        var settings = PostEliteSettings.Load(p);
        var dir = Path.Combine(CodePatchService.PatchesDir(p), PatchName);
        bool scale = on;
        on = on || kantoBadges;
        if (on)
        {
            if (!CanScaleLevels(p)) throw new NotSupportedException(Loc.T("core.postElite.levelsNeed"));
            if (percent is < MinPercent or > MaxPercent) throw new ArgumentException(Loc.T("core.postElite.badPercent", MinPercent, MaxPercent));
            if (floor is < 0 or > 100) throw new ArgumentException(Loc.T("core.postElite.badFloor"));
            var levels = targets ?? DefaultKantoTargets;
            if (levels.Count != KantoBadgeSteps || levels.Any(l => l is < 1 or > 100)) throw new ArgumentException(Loc.T("core.postElite.badTargets", KantoBadgeSteps));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "main.c"), BuildLevelSource(scale ? percent : 100, scale ? floor : 0, skip, kantoBadges ? KantoTrainers(p) : null, levels, routeGap));
            new CodePatch { Name = PatchName, Directory = dir, Manifest = BuildLevelManifest() }.Save();
        }
        else if (Directory.Exists(dir) && CodePatch.Load(dir) is { Manifest.Enabled: true } patch)
        {
            patch.Manifest.Enabled = false;
            patch.Save();
        }
        else
        {
            // já está desligado: sem nada a compilar, não pode exigir o compilador
            settings.ScaleLevels = false;
            settings.KantoBadges = false;
            settings.Save(p);
            return settings;
        }
        await new CodePatchService(hub).ApplyAsync(p, log, ct);
        settings.ScaleLevels = scale;
        settings.KantoBadges = kantoBadges;
        settings.KantoTargets = [.. targets ?? DefaultKantoTargets];
        settings.KantoRouteGap = routeGap;
        settings.LevelPercent = percent;
        settings.LevelFloor = floor;
        settings.LevelSkip = [.. skip.Order()];
        settings.Save(p);
        return settings;
    }

    /// <summary>Lista de números separados por vírgula ou espaço, como vem da tela. Faixas com hífen valem, como 701-705.</summary>
    public static List<int> ParseIds(string text)
    {
        var ids = new SortedSet<int>();
        foreach (var part in text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var range = part.Split('-');
            if (range.Length == 2 && int.TryParse(range[0], out var from) && int.TryParse(range[1], out var to) && from <= to) { for (int i = from; i <= to; i++) ids.Add(i); }
            else if (int.TryParse(part, out var id)) ids.Add(id);
            else throw new ArgumentException(Loc.T("core.postElite.badIds", part));
        }
        return [.. ids];
    }

    /// <summary>Os níveis-alvo como vêm da tela, separados por vírgula ou espaço.</summary>
    public static List<int> ParseTargets(string text)
    {
        var levels = text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(t => int.TryParse(t, out var n) ? n : -1).ToList();
        if (levels.Count != KantoBadgeSteps || levels.Any(l => l is < 1 or > 100)) throw new ArgumentException(Loc.T("core.postElite.badTargets", KantoBadgeSteps));
        return levels;
    }
}
