using System.Text.Json;
using PokeHub.Core.Gen4;
using PokeHub.Core.Hub;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.CodeInjection;

/// <summary>O que o hub gravou no workspace, para desfazer antes de aplicar de novo.</summary>
public sealed class PatchLedger
{
    public sealed class Write
    {
        public string Patch { get; set; } = "";
        public string File { get; set; } = "";
        public int Offset { get; set; }
        public string Original { get; set; } = "";
        public string Written { get; set; } = "";
    }

    public sealed class Region
    {
        public string Patch { get; set; } = "";
        public uint Address { get; set; }
        public int Size { get; set; }
    }

    public List<Write> Writes { get; set; } = [];
    /// <summary>Trechos ocupados no overlay sintético.</summary>
    public List<Region> Regions { get; set; } = [];
}

public sealed record AppliedPatchInfo(string Name, uint Address, int Size, int Hooks, IReadOnlyDictionary<string, uint> Symbols);

public sealed record PatchApplyResult(IReadOnlyList<AppliedPatchInfo> Patches, bool ExpansionAdded, int FreeBytes);

/// <summary>
/// Compila os patches de código do projeto e os grava no workspace: as imagens no overlay sintético e os ganchos no arm9 e nos overlays.
/// Tudo acontece em memória primeiro; se um patch falha na compilação ou na checagem, o workspace fica como estava.
/// Aplicar é idempotente: o registro em applied.json desfaz a aplicação anterior antes da nova.
/// </summary>
public sealed class CodePatchService(HubPaths hub)
{
    public const string PatchesFolder = "code-patches";
    public const string LedgerFileName = "applied.json";

    public static string PatchesDir(Project p) => Path.Combine(p.Paths.Workspace, PatchesFolder);
    private static string LedgerPath(Project p) => Path.Combine(PatchesDir(p), LedgerFileName);
    /// <summary>Saída da compilação, fora do workspace para não pesar nas versões.</summary>
    private static string BuildDir(Project p, string patch) => Path.Combine(p.Paths.Dir, "code-patches-build", patch);

    public static IReadOnlyList<CodePatch> List(Project p)
    {
        var dir = PatchesDir(p);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir)
            .Where(d => File.Exists(Path.Combine(d, CodePatch.ManifestFileName)))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .Select(CodePatch.Load).ToList();
    }

    public static PatchLedger LoadLedger(Project p)
    {
        var file = LedgerPath(p);
        return File.Exists(file) ? JsonSerializer.Deserialize<PatchLedger>(File.ReadAllText(file), HubConfig.JsonOptions) ?? new PatchLedger() : new PatchLedger();
    }

    /// <summary>Cria a pasta de um patch novo com um modelo comentado.</summary>
    public static CodePatch Create(Project p, string name)
    {
        RequireDsRom(p);
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(Loc.T("core.inject.invalidName"), nameof(name));
        var dir = Path.Combine(PatchesDir(p), name);
        if (Directory.Exists(dir)) throw new InvalidOperationException(Loc.T("core.inject.exists", name));
        Directory.CreateDirectory(dir);
        var patch = new CodePatch
        {
            Name = name,
            Directory = dir,
            Manifest = new PatchManifest { Description = Loc.T("core.inject.templateDescription"), Enabled = false, GameCodes = [p.Config.GameCode] },
        };
        patch.Save();
        File.WriteAllText(Path.Combine(dir, "main.c"), Loc.T("core.inject.templateSource"));
        return patch;
    }

    private static void RequireDsRom(Project p)
    {
        if (WorkspaceService.FormatOf(p) != WorkspaceFormat.DsRom) throw new InvalidOperationException(Loc.T("core.inject.needDsRom"));
    }

    /// <summary>Tira do workspace tudo o que foi aplicado. A expansão do ARM9 fica: patches do DSPRE podem depender dela e ela não muda o jogo sozinha.</summary>
    public Task<PatchApplyResult> RevertAsync(Project p, IProgress<string> log, CancellationToken ct = default) => RunAsync(p, [], log, ct);

    /// <summary>Compila e aplica os patches ativos, substituindo a aplicação anterior.</summary>
    public Task<PatchApplyResult> ApplyAsync(Project p, IProgress<string> log, CancellationToken ct = default) =>
        RunAsync(p, List(p).Where(x => x.Manifest.Enabled).ToList(), log, ct);

    private async Task<PatchApplyResult> RunAsync(Project p, IReadOnlyList<CodePatch> patches, IProgress<string> log, CancellationToken ct)
    {
        RequireDsRom(p);
        var game = Gen4Game.For(p);
        var expansion = Arm9Expansion.For(game) ?? throw new NotSupportedException(Loc.T("core.inject.gameUnsupported", p.Config.GameCode));
        foreach (var patch in patches)
            if (patch.Manifest.GameCodes.Count > 0 && !patch.Manifest.GameCodes.Contains(p.Config.GameCode, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(Loc.T("core.inject.wrongGame", patch.Name, string.Join(", ", patch.Manifest.GameCodes), p.Config.GameCode));

        // 1. compila antes de tocar em qualquer coisa
        var built = new List<(CodePatch Patch, BuiltPatch Build)>();
        uint next = Arm9Expansion.LoadAddress + Arm9Expansion.WindowStart;
        uint limit = Arm9Expansion.LoadAddress + Arm9Expansion.WindowEnd;
        if (patches.Any(x => ArmToolchain.SourcesOf(x.Directory).Count > 0))
        {
            var toolchain = await ArmToolchain.FindAsync(hub.Runtimes) ?? throw new InvalidOperationException(Loc.T("core.inject.noToolchain"));
            foreach (var patch in patches)
            {
                log.Report(Loc.T("core.inject.compiling", patch.Name));
                var build = await toolchain.BuildAsync(patch, next, BuildDir(p, patch.Name), log, ct);
                if (build.Address + build.Image.Length > limit)
                    throw new InvalidOperationException(Loc.T("core.inject.tooBig", patch.Name, Arm9Expansion.WindowEnd - Arm9Expansion.WindowStart));
                built.Add((patch, build));
                next = (build.Address + (uint)build.Image.Length + 3) & ~3u;
            }
        }
        else built.AddRange(patches.Select(x => (x, new BuiltPatch(next, [], new Dictionary<string, uint>()))));

        var ws = new PatchedWorkspace(p.Paths.Workspace, game, expansion);
        var previous = LoadLedger(p);
        ws.Undo(previous);

        // 2. monta o estado novo em memória
        var binaries = new RomBinaries(p.Paths.Workspace);
        var ledger = new PatchLedger();
        bool expansionAdded = built.Any(b => b.Build.Image.Length > 0) && expansion.EnsureApplied(ws.Bytes(RomBinaries.Arm9File), binaries.Arm9Base);
        if (expansionAdded) { ws.MarkChanged(RomBinaries.Arm9File); log.Report(Loc.T("core.inject.expansionApplied")); }

        var infos = new List<AppliedPatchInfo>();
        var taken = new List<(string File, int Start, int End, string Patch)>();
        // os pontos da expansão também são proibidos para ganchos
        taken.Add((RomBinaries.Arm9File, (int)(expansion.BranchAddress - binaries.Arm9Base), (int)(expansion.BranchAddress - binaries.Arm9Base) + expansion.BranchCode.Length, "ARM9"));
        taken.Add((RomBinaries.Arm9File, (int)(expansion.InitAddress - binaries.Arm9Base), (int)(expansion.InitAddress - binaries.Arm9Base) + expansion.InitCode.Length, "ARM9"));
        foreach (var (patch, build) in built)
        {
            foreach (var hook in patch.Manifest.Hooks)
            {
                uint address = CodePatch.ParseNumber(hook.Address) & ~1u;
                var (file, offset) = binaries.Locate(hook.Target, address);
                var data = EncodeHook(hook, address, patch, build);
                var bytes = ws.Bytes(file);
                if (offset + data.Length > bytes.Length) throw new InvalidDataException(Loc.T("core.inject.outsideFile", $"0x{address:X8}", hook.Target));
                var clash = taken.FirstOrDefault(t => t.File == file && offset < t.End && t.Start < offset + data.Length);
                if (clash.File is not null) throw new InvalidOperationException(Loc.T("core.inject.overlap", $"0x{address:X8}", patch.Name, clash.Patch));
                taken.Add((file, offset, offset + data.Length, patch.Name));

                var original = bytes.AsSpan(offset, data.Length).ToArray();
                if (hook.Expect is { } expect)
                {
                    var expected = CodePatch.ParseHex(expect);
                    if (offset + expected.Length > bytes.Length || !bytes.AsSpan(offset, expected.Length).SequenceEqual(expected))
                        throw new InvalidOperationException(Loc.T("core.inject.expectFailed", patch.Name, $"0x{address:X8}", Convert.ToHexString(bytes.AsSpan(offset, Math.Min(expected.Length, bytes.Length - offset))), Convert.ToHexString(expected)));
                }
                else log.Report(Loc.T("core.inject.noExpect", patch.Name, $"0x{address:X8}"));

                data.CopyTo(bytes, offset);
                ws.MarkChanged(file);
                ledger.Writes.Add(new PatchLedger.Write { Patch = patch.Name, File = file, Offset = offset, Original = Convert.ToHexString(original), Written = Convert.ToHexString(data) });
                log.Report(Loc.T("core.inject.hooked", patch.Name, hook.Type, $"0x{address:X8}", hook.Target, data.Length));
            }

            if (build.Image.Length > 0)
            {
                ws.WriteSynthetic(build.Address, build.Image, patch.Name);
                ledger.Regions.Add(new PatchLedger.Region { Patch = patch.Name, Address = build.Address, Size = build.Image.Length });
            }
            infos.Add(new AppliedPatchInfo(patch.Name, build.Address, build.Image.Length, patch.Manifest.Hooks.Count, build.Symbols));
        }

        // 3. grava
        ws.Save();
        Directory.CreateDirectory(PatchesDir(p));
        File.WriteAllText(LedgerPath(p), JsonSerializer.Serialize(ledger, HubConfig.JsonOptions));
        return new PatchApplyResult(infos, expansionAdded, (int)(limit - next));
    }

    private static byte[] EncodeHook(PatchHook hook, uint address, CodePatch patch, BuiltPatch build)
    {
        if (hook.Type == "bytes") return CodePatch.ParseHex(hook.Bytes!);
        uint target = ResolveSymbol(hook.Symbol!, patch, build);
        bool thumb = hook.Mode == "thumb";
        return hook.Type switch
        {
            "call" => ArmEncoding.Call(address, target, thumb),
            "jump" => ArmEncoding.Jump(address, target, thumb),
            _ => BitConverter.GetBytes(target),
        };
    }

    private static uint ResolveSymbol(string symbol, CodePatch patch, BuiltPatch build)
    {
        if (CodePatch.IsNumber(symbol)) return CodePatch.ParseNumber(symbol);
        if (patch.Manifest.Symbols.TryGetValue(symbol, out var value)) return CodePatch.ParseNumber(value);
        return build.Symbols.TryGetValue(symbol, out var address) ? address : throw new InvalidDataException(Loc.T("core.inject.noSymbol", patch.Name, symbol));
    }

    /// <summary>Arquivos do workspace carregados e alterados em memória.</summary>
    private sealed class PatchedWorkspace(string root, Gen4Game game, Arm9Expansion expansion)
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
        private Narc? _narc;

        private string SyntheticPath => Path.Combine(game.FilesRoot, expansion.SyntheticNarc);

        public byte[] Bytes(string file)
        {
            if (!_files.TryGetValue(file, out var data)) _files[file] = data = File.ReadAllBytes(Path.Combine(root, file));
            return data;
        }

        public void MarkChanged(string file) => _changed.Add(file);

        /// <summary>Volta os bytes originais. Se algo foi alterado por fora, para: gravar por cima corromperia a edição.</summary>
        public void Undo(PatchLedger ledger)
        {
            foreach (var w in ledger.Writes)
            {
                var bytes = Bytes(w.File);
                var written = Convert.FromHexString(w.Written);
                if (w.Offset + written.Length > bytes.Length || !bytes.AsSpan(w.Offset, written.Length).SequenceEqual(written))
                    throw new InvalidOperationException(Loc.T("core.inject.ledgerMismatch", w.Patch, w.File, $"0x{w.Offset:X}"));
                Convert.FromHexString(w.Original).CopyTo(bytes, w.Offset);
                MarkChanged(w.File);
            }
            foreach (var r in ledger.Regions)
                Array.Clear(Synthetic(), (int)(r.Address - Arm9Expansion.LoadAddress), r.Size);
        }

        /// <summary>O arquivo da expansão. Se ainda não tem o tamanho reservado, é recriado zerado, como o DSPRE faz.</summary>
        private byte[] Synthetic()
        {
            if (_narc is null)
            {
                if (!File.Exists(SyntheticPath)) throw new FileNotFoundException(Loc.T("core.inject.syntheticMissing"), SyntheticPath);
                _narc = Narc.Read(File.ReadAllBytes(SyntheticPath));
                if (expansion.SyntheticFile >= _narc.Files.Count) throw new InvalidDataException(Loc.T("core.inject.syntheticMissing"));
                if (_narc.Files[expansion.SyntheticFile].Length < Arm9Expansion.FileSize)
                    _narc.Files[expansion.SyntheticFile] = new byte[Arm9Expansion.FileSize];
            }
            return _narc.Files[expansion.SyntheticFile];
        }

        public void WriteSynthetic(uint address, byte[] image, string patch)
        {
            var file = Synthetic();
            int offset = (int)(address - Arm9Expansion.LoadAddress);
            int used = file.AsSpan(offset, image.Length).IndexOfAnyExcept((byte)0);
            if (used >= 0) throw new InvalidOperationException(Loc.T("core.inject.regionUsed", patch, $"0x{offset + used:X}"));
            image.CopyTo(file, offset);
        }

        public void Save()
        {
            foreach (var file in _changed) WriteAtomic(Path.Combine(root, file), _files[file]);
            if (_narc is not null) WriteAtomic(SyntheticPath, _narc.Write());
        }

        private static void WriteAtomic(string path, byte[] data)
        {
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, path, overwrite: true);
        }
    }
}
