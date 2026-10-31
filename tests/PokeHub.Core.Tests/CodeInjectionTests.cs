using PokeHub.Core.CodeInjection;
using PokeHub.Core.Gen4;
using PokeHub.Core.Hub;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;
using Xunit;
using Xunit.Abstractions;

namespace PokeHub.Core.Tests;

public class CodeInjectionTests(ITestOutputHelper output) : IDisposable
{
    private const uint Arm9Base = 0x02000000;
    private const uint Ov5Base = 0x021E0000;
    private readonly string _root = Directory.CreateTempSubdirectory("pokehub-inject").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Workspace ds-rom mínimo de SoulSilver US: arm9 com o ponto de entrada e a área livre como no jogo limpo.</summary>
    private Project FakeProject()
    {
        var paths = new ProjectPaths(Path.Combine(_root, "proj"));
        var ws = paths.Workspace;
        Directory.CreateDirectory(Path.Combine(ws, "arm9"));
        Directory.CreateDirectory(Path.Combine(ws, "arm9_overlays"));
        Directory.CreateDirectory(Path.Combine(ws, "files", "a", "0", "2"));
        File.WriteAllText(Path.Combine(ws, "config.yaml"), "arm9_bin: arm9/arm9.bin\n");
        File.WriteAllText(Path.Combine(ws, "arm9", "arm9.yaml"), $"base_address: {Arm9Base}\nentry_function: 0\n");
        File.WriteAllText(Path.Combine(ws, "arm9_overlays", "overlays.yaml"),
            $"table_signed: false\noverlays:\n  - id: 5\n    base_address: {Ov5Base}\n    code_size: 256\n    file_name: ov005.bin\n");
        var arm9 = new byte[0x110400];
        for (int i = 0; i < arm9.Length; i++) arm9[i] = (byte)(i * 31 + 7);
        new byte[] { 0x00, 0x20, 0x03, 0x21 }.CopyTo(arm9, 0xCD0);
        Array.Fill(arm9, (byte)0xFF, 0x110334, 0x24);
        File.WriteAllBytes(Path.Combine(ws, "arm9", "arm9.bin"), arm9);
        File.WriteAllBytes(Path.Combine(ws, "arm9_overlays", "ov005.bin"), new byte[256]);
        var narc = new Narc();
        narc.Files.Add(new byte[0x228]);
        narc.Files.Add([1, 2, 3, 4]);
        File.WriteAllBytes(Path.Combine(ws, "files", "a", "0", "2", "8"), narc.Write());
        return new Project { Paths = paths, Config = new ProjectConfig { Name = "t", BaseGame = BaseGame.SoulSilver, GameCode = "IPGE", Generation = 4 } };
    }

    private static CodePatch WritePatch(Project p, string name, PatchManifest manifest, string? source = null)
    {
        var dir = Path.Combine(CodePatchService.PatchesDir(p), name);
        Directory.CreateDirectory(dir);
        var patch = new CodePatch { Name = name, Directory = dir, Manifest = manifest };
        patch.Save();
        if (source is not null) File.WriteAllText(Path.Combine(dir, "main.c"), source);
        return patch;
    }

    private CodePatchService Service() => new(new HubPaths(Path.Combine(_root, "hub")));
    private static byte[] Arm9(Project p) => File.ReadAllBytes(Path.Combine(p.Paths.Workspace, "arm9", "arm9.bin"));
    private static readonly Progress<string> NoLog = new();

    [Theory]
    [InlineData(0x02000CD0u, 0x02110335u, "0FF130FB")] // a chamada real do patch do DSPRE no HGSS US
    [InlineData(0x02110340u, 0x02000CD5u, "F0F6C8FC")] // para trás
    public void Thumb_call_encodes_bl(uint site, uint target, string expected) =>
        Assert.Equal(expected, Convert.ToHexString(ArmEncoding.Call(site, target, siteThumb: true)));

    [Fact]
    public void Call_switches_mode_with_blx_and_arm_bl()
    {
        Assert.Equal("00F000E8", Convert.ToHexString(ArmEncoding.Call(0x02000000, 0x02000004, siteThumb: true))); // Thumb -> ARM
        Assert.Equal("000000EB", Convert.ToHexString(ArmEncoding.Call(0x02000000, 0x02000008, siteThumb: false))); // ARM -> ARM
        Assert.Equal("000000FB", Convert.ToHexString(ArmEncoding.Call(0x02000000, 0x0200000B, siteThumb: false))); // ARM -> Thumb, H=1
    }

    [Fact]
    public void Expansion_accepts_padding_or_the_assert_text_and_refuses_anything_else()
    {
        var e = Arm9Expansion.For(new Gen4Game { Game = BaseGame.Platinum, Language = Gen4Language.English, FilesRoot = "", Arm9Path = "" })!;
        int init = (int)(e.InitAddress - Arm9Base), site = (int)(e.BranchAddress - Arm9Base);
        byte[] Arm9With(byte[] area)
        {
            var arm9 = new byte[init + 64];
            new byte[] { 0x00, 0x20, 0x03, 0x21 }.CopyTo(arm9, site);
            area.CopyTo(arm9, init);
            return arm9;
        }
        var text = System.Text.Encoding.ASCII.GetBytes("Assertion (%s) failed in \"%s\"");
        Assert.True(e.EnsureApplied(Arm9With(text), Arm9Base));
        Assert.True(e.EnsureApplied(Arm9With(Enumerable.Repeat((byte)0xFF, 32).ToArray()), Arm9Base));
        var code = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        Assert.Throws<InvalidDataException>(() => e.EnsureApplied(Arm9With(code), Arm9Base));
    }

    [Fact]
    public void Thumb_call_rejects_far_targets_and_odd_sites()
    {
        Assert.Throws<InvalidDataException>(() => ArmEncoding.Call(0x02000000, 0x02800001, siteThumb: true));
        Assert.Throws<InvalidDataException>(() => ArmEncoding.Call(0x02000001, 0x02000101, siteThumb: true));
    }

    [Fact]
    public void Jump_keeps_registers_and_handles_alignment()
    {
        Assert.Equal("7847C04604F01FE5" + "01A03C02", Convert.ToHexString(ArmEncoding.Jump(0x02000000, 0x023CA001, siteThumb: true)));
        Assert.Equal("C0467847C04604F01FE5" + "01A03C02", Convert.ToHexString(ArmEncoding.Jump(0x02000002, 0x023CA001, siteThumb: true)));
        Assert.Equal("04F01FE5" + "00A03C02", Convert.ToHexString(ArmEncoding.Jump(0x02000000, 0x023CA000, siteThumb: false)));
    }

    [Fact]
    public void Expansion_tables_cover_gen4_except_unknown_languages()
    {
        foreach (var game in new[] { BaseGame.Diamond, BaseGame.Platinum, BaseGame.HeartGold })
            foreach (var lang in new[] { Gen4Language.English, Gen4Language.Spanish, Gen4Language.Italian, Gen4Language.French, Gen4Language.German })
                Assert.NotNull(Arm9Expansion.For(new Gen4Game { Game = game, Language = lang, FilesRoot = "", Arm9Path = "" }));
        Assert.Null(Arm9Expansion.For(new Gen4Game { Game = BaseGame.HeartGold, Language = Gen4Language.Korean, FilesRoot = "", Arm9Path = "" }));
    }

    /// <summary>A rotina de carga chama a mesma função do jogo a partir de endereços diferentes: uma tabela errada aparece aqui.</summary>
    [Theory]
    [InlineData(BaseGame.SoulSilver, Gen4Language.Italian, 0x02007508u)]
    [InlineData(BaseGame.SoulSilver, Gen4Language.French, 0x02007508u)]
    [InlineData(BaseGame.SoulSilver, Gen4Language.German, 0x02007508u)]
    [InlineData(BaseGame.Diamond, Gen4Language.English, 0x020064F0u)]
    [InlineData(BaseGame.Diamond, Gen4Language.German, 0x020064F0u)]
    public void Init_code_calls_the_narc_loader_from_its_address(BaseGame game, Gen4Language language, uint loader)
    {
        var e = Arm9Expansion.For(new Gen4Game { Game = game, Language = language, FilesRoot = "", Arm9Path = "" })!;
        Assert.Equal(Convert.ToHexString(ArmEncoding.Call(e.InitAddress + 8, loader | 1, siteThumb: true)), Convert.ToHexString(e.InitCode.AsSpan(8, 4)));
        Assert.Equal(Convert.ToHexString(ArmEncoding.Call(e.BranchAddress, e.InitAddress | 1, siteThumb: true)), Convert.ToHexString(e.BranchCode));
    }

    [Fact]
    public void Symbols_keep_the_thumb_bit_from_readelf()
    {
        var symbols = ArmToolchain.ParseSymbols("""
            Symbol table '.symtab' contains 3 entries:
               Num:    Value  Size Type    Bind   Vis      Ndx Name
                 0: 00000000     0 NOTYPE  LOCAL  DEFAULT  UND
                 1: 023ca001    24 FUNC    GLOBAL DEFAULT    1 MyHook
                 2: 02004209     0 NOTYPE  GLOBAL DEFAULT  ABS Game_Func
                 3: 00000000     0 NOTYPE  GLOBAL DEFAULT  UND Missing
            """);
        Assert.Equal(0x023CA001u, symbols["MyHook"]);
        Assert.Equal(0x02004209u, symbols["Game_Func"]);
        Assert.False(symbols.ContainsKey("Missing"));
    }

    [Fact]
    public void Invalid_manifest_is_refused_with_its_name()
    {
        var p = FakeProject();
        WritePatch(p, "ruim", new PatchManifest { Hooks = [new PatchHook { Address = "0x02000000", Type = "voar", Symbol = "F" }] });
        var ex = Assert.Throws<InvalidDataException>(() => CodePatchService.List(p));
        Assert.Contains("ruim", ex.Message);
        // nome de símbolo vai para o script do ligador: não pode carregar texto arbitrário
        var dir = Path.Combine(CodePatchService.PatchesDir(p), "ruim");
        File.WriteAllText(Path.Combine(dir, "patch.json"), """{"symbols":{"a = 1; /DISCARD/":"0x02000000"}}""");
        Assert.Throws<InvalidDataException>(() => CodePatch.Load(dir));
    }

    [Fact]
    public async Task Bytes_patch_applies_reapplies_and_reverts_without_touching_the_expansion()
    {
        var p = FakeProject();
        var clean = Arm9(p);
        var expect = Convert.ToHexString(clean.AsSpan(0x1000, 2));
        WritePatch(p, "a", new PatchManifest
        {
            GameCodes = ["IPGE"],
            Hooks =
            [
                new PatchHook { Address = "0x02001000", Type = "bytes", Bytes = "AA BB", Expect = expect },
                new PatchHook { Target = "overlay:5", Address = "0x021E0010", Type = "call", Symbol = "0x021E0101" },
            ],
        });

        var result = await Service().ApplyAsync(p, NoLog);
        Assert.False(result.ExpansionAdded); // sem código novo, sem expansão
        Assert.Equal("AABB", Convert.ToHexString(Arm9(p).AsSpan(0x1000, 2)));
        var ov = File.ReadAllBytes(Path.Combine(p.Paths.Workspace, "arm9_overlays", "ov005.bin"));
        Assert.Equal(Convert.ToHexString(ArmEncoding.Call(0x021E0010, 0x021E0101, true)), Convert.ToHexString(ov.AsSpan(0x10, 4)));

        // reaplicar não vê os próprios bytes como conflito com o expect
        await Service().ApplyAsync(p, NoLog);
        Assert.Equal("AABB", Convert.ToHexString(Arm9(p).AsSpan(0x1000, 2)));

        await Service().RevertAsync(p, NoLog);
        Assert.Equal(clean, Arm9(p));
        Assert.Empty(CodePatchService.LoadLedger(p).Writes);
    }

    [Fact]
    public async Task Wrong_expect_or_game_or_overlap_write_nothing()
    {
        var p = FakeProject();
        var clean = Arm9(p);
        var wrong = WritePatch(p, "a", new PatchManifest { Hooks = [new PatchHook { Address = "0x02001000", Type = "bytes", Bytes = "AABB", Expect = "0000" }] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApplyAsync(p, NoLog));
        Assert.Equal(clean, Arm9(p));

        wrong.Manifest.Hooks[0].Expect = null;
        wrong.Manifest.GameCodes = ["CPUE"];
        wrong.Save();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApplyAsync(p, NoLog));
        Assert.Equal(clean, Arm9(p));

        wrong.Manifest.GameCodes = [];
        wrong.Save();
        WritePatch(p, "b", new PatchManifest { Hooks = [new PatchHook { Address = "0x02001001", Type = "bytes", Bytes = "CC" }] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApplyAsync(p, NoLog));
        Assert.Equal(clean, Arm9(p));
    }

    [Fact]
    public async Task Hook_over_the_expansion_entry_point_is_refused()
    {
        var p = FakeProject();
        WritePatch(p, "a", new PatchManifest { Hooks = [new PatchHook { Address = "0x02000CD0", Type = "bytes", Bytes = "0000" }] });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ApplyAsync(p, NoLog));
    }

    [Fact]
    public async Task Bytes_changed_outside_the_hub_stop_the_reapply()
    {
        var p = FakeProject();
        WritePatch(p, "a", new PatchManifest { Hooks = [new PatchHook { Address = "0x02001000", Type = "bytes", Bytes = "AABB" }] });
        await Service().ApplyAsync(p, NoLog);

        var arm9 = Arm9(p);
        arm9[0x1000] = 0x11;
        File.WriteAllBytes(Path.Combine(p.Paths.Workspace, "arm9", "arm9.bin"), arm9);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RevertAsync(p, NoLog));
        Assert.Equal(0x11, Arm9(p)[0x1000]);
    }

    [Fact]
    public void Ndstool_workspace_gets_a_clear_message()
    {
        var p = FakeProject();
        File.Delete(Path.Combine(p.Paths.Workspace, "config.yaml"));
        var ex = Assert.Throws<InvalidOperationException>(() => CodePatchService.Create(p, "x"));
        Assert.Contains("ds-rom", ex.Message);
    }

    [Fact]
    public void Template_is_created_inactive_and_valid()
    {
        var p = FakeProject();
        var created = CodePatchService.Create(p, "novo");
        Assert.False(created.Manifest.Enabled);
        var listed = Assert.Single(CodePatchService.List(p));
        Assert.Equal("novo", listed.Name);
        Assert.Equal(["IPGE"], listed.Manifest.GameCodes);
        Assert.True(File.Exists(Path.Combine(listed.Directory, "main.c")));
    }

    /// <summary>
    /// Gera uma ROM de SoulSilver US para conferir no emulador: gancho no laço principal que clareia a tela de cima e escurece a de baixo a cada quadro.
    /// Opcional: POKEHUB_INJECT_WORKSPACE (cópia descartável de um workspace ds-rom, alterada pelo teste), POKEHUB_DSROM, POKEHUB_MSYS2_HUB_ROOT e POKEHUB_INJECT_OUTPUT (.nds).
    /// POKEHUB_INJECT_HOOK troca o jogo: "ponto,funcao,codigo"; Platinum US: 0x02000D84,0x02000F31,CPUE.
    /// </summary>
    [Fact]
    public async Task Real_rom_with_a_visible_hook()
    {
        var workspace = Environment.GetEnvironmentVariable("POKEHUB_INJECT_WORKSPACE");
        var dsrom = Environment.GetEnvironmentVariable("POKEHUB_DSROM");
        var hubRoot = Environment.GetEnvironmentVariable("POKEHUB_MSYS2_HUB_ROOT");
        var outputRom = Environment.GetEnvironmentVariable("POKEHUB_INJECT_OUTPUT");
        if (workspace is null || dsrom is null || hubRoot is null || outputRom is null) { output.WriteLine("Variáveis não definidas; teste ignorado."); return; }

        var paths = new ProjectPaths(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(workspace))!);
        Assert.Equal(Path.GetFullPath(workspace), Path.GetFullPath(paths.Workspace)); // a pasta precisa se chamar workspace, como num projeto
        var hook = (Environment.GetEnvironmentVariable("POKEHUB_INJECT_HOOK") ?? "0x02000E3C,0x02004209,IPGE").Split(',');
        uint site = CodePatch.ParseNumber(hook[0]), frame = CodePatch.ParseNumber(hook[1]);
        var p = new Project { Paths = paths, Config = new ProjectConfig { Name = "real", BaseGame = hook[2].StartsWith("CPU") ? BaseGame.Platinum : BaseGame.SoulSilver, GameCode = hook[2], Generation = 4 } };

        WritePatch(p, "brilho", new PatchManifest
        {
            GameCodes = [hook[2]],
            Symbols = new() { ["Game_Frame"] = hook[1] },
            Hooks = [new PatchHook { Address = hook[0], Type = "call", Symbol = "FrameHook", Expect = Convert.ToHexString(ArmEncoding.Call(site, frame, true)) }],
        }, """
            extern void Game_Frame(void);
            void FrameHook(void)
            {
                Game_Frame();
                *(volatile unsigned short *)0x0400006C = 0x4008;
                *(volatile unsigned short *)0x0400106C = 0x8008;
            }
            """);

        var progress = new Progress<string>(output.WriteLine);
        var result = await new CodePatchService(new HubPaths(hubRoot)).ApplyAsync(p, progress);
        Assert.True(Assert.Single(result.Patches).Size > 0);
        await DsRomTool.BuildAsync(dsrom, workspace, outputRom, progress);
        Assert.True(File.Exists(outputRom));
    }

    /// <summary>Compilação de verdade. Opcional: POKEHUB_MSYS2_HUB_ROOT aponta para o hub com o MSYS2 instalado.</summary>
    [Fact]
    public async Task Real_toolchain_builds_links_and_hooks()
    {
        var hubRoot = Environment.GetEnvironmentVariable("POKEHUB_MSYS2_HUB_ROOT");
        if (hubRoot is null) { output.WriteLine("POKEHUB_MSYS2_HUB_ROOT não definido; teste ignorado."); return; }

        var p = FakeProject();
        WritePatch(p, "real", new PatchManifest
        {
            Symbols = new() { ["Game_Func"] = "0x02004209" },
            Hooks =
            [
                new PatchHook { Address = "0x02000E3C", Type = "call", Symbol = "MyHook" },
                new PatchHook { Address = "0x02001000", Type = "jump", Symbol = "MyHook" },
                new PatchHook { Address = "0x02001100", Type = "pointer", Symbol = "counter" },
            ],
        }, """
            extern void Game_Func(void);
            int counter;
            int table[3] = { 1, 2, 3 };
            void Unused(void) { for (;;); }
            int MyHook(int a, int b) { Game_Func(); counter += table[a % 3] / b; return counter; }
            """);

        // modelo ativado sem ganchos: nada alcança o código, e isso não é erro
        var template = CodePatchService.Create(p, "modelo");
        template.Manifest.Enabled = true;
        template.Save();
        // gancho para função que não existe: o erro precisa dizer qual, não falar do objcopy
        var missing = WritePatch(p, "faltando", new PatchManifest { Hooks = [new PatchHook { Address = "0x02001200", Type = "call", Symbol = "NaoExiste" }] }, "void Outra(void) { }\n");
        var missingError = await Assert.ThrowsAsync<InvalidDataException>(() => new CodePatchService(new HubPaths(hubRoot)).ApplyAsync(p, new Progress<string>()));
        Assert.Contains("NaoExiste", missingError.Message);
        Directory.Delete(missing.Directory, recursive: true);

        var log = new List<string>();
        var service = new CodePatchService(new HubPaths(hubRoot));
        var progress = new Progress<string>(l => { lock (log) log.Add(l); });
        var result = await service.ApplyAsync(p, progress);
        lock (log) foreach (var l in log) output.WriteLine(l);

        Assert.True(result.ExpansionAdded);
        Assert.Equal(0, result.Patches.Single(x => x.Name == "modelo").Size);
        var info = result.Patches.Single(x => x.Name == "real");
        Assert.Equal(Arm9Expansion.LoadAddress + Arm9Expansion.WindowStart, info.Address);
        uint hook = info.Symbols["MyHook"];
        Assert.Equal(1u, hook & 1); // função Thumb
        Assert.False(info.Symbols.ContainsKey("Unused")); // o ligador descartou o que nada alcança
        // função Thumb do jogo chamada direto; um veneer aqui saltaria em modo ARM e travaria o jogo
        Assert.DoesNotContain(info.Symbols.Keys, s => s.Contains("Game_Func_from_thumb"));
        Assert.Equal(0x02004209u, info.Symbols["Game_Func"]);

        var arm9 = Arm9(p);
        Assert.Equal(Convert.ToHexString(ArmEncoding.Call(0x02000E3C, hook, true)), Convert.ToHexString(arm9.AsSpan(0xE3C, 4)));
        Assert.Equal(Convert.ToHexString(ArmEncoding.Jump(0x02001000, hook, true)), Convert.ToHexString(arm9.AsSpan(0x1000, 12)));
        Assert.Equal(info.Symbols["counter"], BitConverter.ToUInt32(arm9, 0x1100));
        Assert.Equal("0FF130FB", Convert.ToHexString(arm9.AsSpan(0xCD0, 4)));

        var narc = Narc.Read(File.ReadAllBytes(Path.Combine(p.Paths.Workspace, "files", "a", "0", "2", "8")));
        Assert.Equal(Arm9Expansion.FileSize, narc.Files[0].Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, narc.Files[1]);
        var counter = info.Symbols["counter"];
        Assert.InRange(counter, info.Address, info.Address + (uint)info.Size - 4); // .bss dentro do espaço reservado

        // aplicar de novo limpa a área e grava de novo
        await service.ApplyAsync(p, progress);
        await service.RevertAsync(p, progress);
        narc = Narc.Read(File.ReadAllBytes(Path.Combine(p.Paths.Workspace, "files", "a", "0", "2", "8")));
        Assert.Equal(-1, narc.Files[0].AsSpan().IndexOfAnyExcept((byte)0));
    }
}
