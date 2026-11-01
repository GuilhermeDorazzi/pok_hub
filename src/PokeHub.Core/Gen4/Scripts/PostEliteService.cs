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
    public const int MinPercent = 100, MaxPercent = 300;
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

    /// <summary>O código do gancho. Sem biblioteca C, a divisão por 100 é uma multiplicação: 5243 / 2^19.</summary>
    public static string BuildLevelSource(int percent, int floor, IReadOnlyCollection<int> skip)
    {
        var list = string.Join(", ", skip.Order().Append(0xFFFF));
        return new StringBuilder()
            .AppendLine("// Gerado pelo hub (Depois da Elite). Depois do fim do jogo, os times saem da leitura com outro nível.")
            .AppendLine("#define NARC_TRAINER_PARTY 56")
            .AppendLine("#define TRAINER_DATA_SIZE 32")
            .AppendLine($"#define PERCENT {percent}u")
            .AppendLine($"#define FLOOR {floor}u")
            .AppendLine("#define MAX_LEVEL 100u")
            .AppendLine()
            .AppendLine("extern void ReadWholeNarcMemberByIdPair(void *dest, int narc, int member);")
            .AppendLine("extern void TrainerData_ReadTrData(unsigned id, void *dest);")
            .AppendLine("extern void *SaveData_Get(void);")
            .AppendLine("extern void *Save_VarsFlags_Get(void *save);")
            .AppendLine("extern int CheckGameClearFlag(void *flags);")
            .AppendLine()
            .AppendLine($"static const unsigned short skip[] = {{ {list} }};")
            .AppendLine()
            .AppendLine("void PostElite_ReadParty(unsigned id, unsigned char *party)")
            .AppendLine("{")
            .AppendLine("    ReadWholeNarcMemberByIdPair(party, NARC_TRAINER_PARTY, id);")
            .AppendLine("    if (!CheckGameClearFlag(Save_VarsFlags_Get(SaveData_Get()))) return;")
            .AppendLine("    for (int i = 0; skip[i] != 0xFFFF; i++)")
            .AppendLine("        if (skip[i] == id) return;")
            .AppendLine("    unsigned char data[TRAINER_DATA_SIZE];")
            .AppendLine("    TrainerData_ReadTrData(id, data);")
            .AppendLine("    // nível, espécie e o resto: 8 bytes, mais golpes e item se o tipo do treinador tiver")
            .AppendLine("    unsigned size = 8u + ((data[0] & 1) ? 8u : 0u) + ((data[0] & 2) ? 2u : 0u);")
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
        },
        Hooks = [new PatchHook { Target = "arm9", Address = ReadPartyCall, Type = "call", Symbol = "PostElite_ReadParty", Expect = ReadPartyCallBytes }],
    };

    /// <summary>Grava o patch e compila. Desligar mantém a pasta inativa e tira o gancho do jogo.</summary>
    public async Task<PostEliteSettings> ApplyLevelsAsync(Project p, bool on, int percent, int floor, IReadOnlyCollection<int> skip, IProgress<string> log, CancellationToken ct = default)
    {
        RequireHgss(p);
        var settings = PostEliteSettings.Load(p);
        var dir = Path.Combine(CodePatchService.PatchesDir(p), PatchName);
        if (on)
        {
            if (!CanScaleLevels(p)) throw new NotSupportedException(Loc.T("core.postElite.levelsNeed"));
            if (percent is < MinPercent or > MaxPercent) throw new ArgumentException(Loc.T("core.postElite.badPercent", MinPercent, MaxPercent));
            if (floor is < 0 or > 100) throw new ArgumentException(Loc.T("core.postElite.badFloor"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "main.c"), BuildLevelSource(percent, floor, skip));
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
            settings.Save(p);
            return settings;
        }
        await new CodePatchService(hub).ApplyAsync(p, log, ct);
        settings.ScaleLevels = on;
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
}
