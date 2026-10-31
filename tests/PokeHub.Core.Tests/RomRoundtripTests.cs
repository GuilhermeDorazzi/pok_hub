using System.Text;
using PokeHub.Core.Rom;
using Xunit;

namespace PokeHub.Core.Tests;

public class RomRoundtripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pokehub-rom-{Guid.NewGuid():N}");
    private readonly Random _rng = new(1234);

    public RomRoundtripTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private byte[] Bytes(int n) { var b = new byte[n]; _rng.NextBytes(b); return b; }

    /// <summary>Monta um workspace sintético, sem fnt.bin nem manifesto, forçando a reconstrução da FNT.</summary>
    private WorkspaceLayout BuildSyntheticWorkspace(string dir)
    {
        var l = new WorkspaceLayout(dir);
        Directory.CreateDirectory(l.OverlayDir);
        Directory.CreateDirectory(Path.Combine(l.DataDir, "a", "b"));
        Directory.CreateDirectory(Path.Combine(l.DataDir, "fielddata"));
        Directory.CreateDirectory(Path.Combine(l.DataDir, "vazia"));

        var header = new byte[NdsHeader.Size];
        Encoding.ASCII.GetBytes("POKEMON PL").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("CPUE").CopyTo(header, 0x0C);
        Encoding.ASCII.GetBytes("01").CopyTo(header, 0x10);
        BitConverter.GetBytes(0x02000800u).CopyTo(header, NdsHeader.Arm9Entry);
        BitConverter.GetBytes(0x02000000u).CopyTo(header, NdsHeader.Arm9Ram);
        File.WriteAllBytes(l.HeaderBin, header);

        var arm9 = Bytes(0x5000).Concat(BitConverter.GetBytes(NdsHeader.NitroFooterMagic)).Concat(Bytes(8)).ToArray();
        File.WriteAllBytes(l.Arm9Bin, arm9);
        File.WriteAllBytes(l.Arm7Bin, Bytes(0x3000));

        // dois overlays no ARM9: ids de overlay 0 e 1, ids de arquivo 0 e 1
        var y9 = new byte[2 * NdsHeader.OverlayEntrySize];
        for (uint i = 0; i < 2; i++)
        {
            BitConverter.GetBytes(i).CopyTo(y9, (int)(i * NdsHeader.OverlayEntrySize));
            BitConverter.GetBytes(i).CopyTo(y9, (int)(i * NdsHeader.OverlayEntrySize + NdsHeader.OverlayFileIdField));
            File.WriteAllBytes(l.OverlayFile(i), Bytes(0x700 + (int)i * 100));
        }
        File.WriteAllBytes(l.Y9Bin, y9);
        File.WriteAllBytes(l.Y7Bin, []);

        var banner = new byte[0x840];
        BitConverter.GetBytes((ushort)1).CopyTo(banner, 0);
        File.WriteAllBytes(l.BannerBin, banner);

        File.WriteAllBytes(Path.Combine(l.DataDir, "root.txt"), Bytes(300));
        File.WriteAllBytes(Path.Combine(l.DataDir, "a", "x.narc"), Bytes(5000));
        File.WriteAllBytes(Path.Combine(l.DataDir, "a", "b", "deep.bin"), Bytes(1));
        File.WriteAllBytes(Path.Combine(l.DataDir, "fielddata", "map.narc"), Bytes(0x2000));
        return l;
    }

    private static Dictionary<string, byte[]> Snapshot(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllBytes);

    [Fact]
    public void Pack_then_extract_preserves_everything_and_is_idempotent()
    {
        var ws1 = BuildSyntheticWorkspace(Path.Combine(_root, "ws1"));
        var rom1 = Path.Combine(_root, "rom1.nds");
        NdsPacker.Pack(ws1.Root, rom1);

        var header = NdsRomHeader.Read(rom1);
        Assert.Equal("CPUE", header.GameCode);
        Assert.Equal(BaseGame.Platinum, header.Game);
        var romBytes = File.ReadAllBytes(rom1);
        Assert.True(header.RomSize <= romBytes.Length);
        Assert.Equal(128 * 1024 << NdsPacker.CapacityByte(header.RomSize), romBytes.Length); // preenchida até o chip
        Assert.All(romBytes.Skip((int)header.RomSize), b => Assert.Equal(0xFF, b));
        Assert.Equal(Crc16.Compute(romBytes.AsSpan(0, NdsHeader.HeaderCrc)), BitConverter.ToUInt16(romBytes, NdsHeader.HeaderCrc));
        Assert.Equal(0x4000u, BitConverter.ToUInt32(romBytes, NdsHeader.Arm9Offset));
        Assert.Equal(0x5000u, BitConverter.ToUInt32(romBytes, NdsHeader.Arm9Size)); // footer não conta

        var ws2 = Path.Combine(_root, "ws2");
        var manifest = NdsExtractor.Extract(rom1, ws2);
        Assert.True(manifest.Arm9HasNitroFooter);
        Assert.Equal(2, manifest.FirstDataFileId);
        Assert.Equal("CPUE", manifest.GameCode);

        var before = Snapshot(ws1.Root);
        var after = Snapshot(ws2);
        foreach (var (path, bytes) in before)
        {
            if (path == "header.bin") continue; // o empacotador reescreve offsets, tamanhos e CRCs do header
            Assert.True(after.ContainsKey(path), $"faltou {path}");
            Assert.True(bytes.AsSpan().SequenceEqual(after[path]), $"conteúdo diferente: {path}");
        }
        Assert.Contains("data/vazia", Directory.EnumerateDirectories(Path.Combine(ws2, "data")).Select(d => "data/" + Path.GetFileName(d)));

        // segunda passagem: árvore inalterada reaproveita a FNT e produz ROM idêntica
        var rom2 = Path.Combine(_root, "rom2.nds");
        NdsPacker.Pack(ws2, rom2);
        Assert.True(File.ReadAllBytes(rom2).AsSpan().SequenceEqual(romBytes), "segunda ROM difere da primeira");
    }

    [Fact]
    public void Adding_a_file_rebuilds_fnt_and_keeps_existing_ids()
    {
        var ws1 = BuildSyntheticWorkspace(Path.Combine(_root, "ws1"));
        var rom1 = Path.Combine(_root, "rom1.nds");
        NdsPacker.Pack(ws1.Root, rom1);
        var ws2 = Path.Combine(_root, "ws2");
        NdsExtractor.Extract(rom1, ws2);
        var (_, filesBefore, _) = NdsFileSystem.Parse(File.ReadAllBytes(Path.Combine(ws2, "fnt.bin")));

        File.WriteAllBytes(Path.Combine(ws2, "data", "fielddata", "novo.narc"), Bytes(123));
        var rom2 = Path.Combine(_root, "rom2.nds");
        NdsPacker.Pack(ws2, rom2);

        var ws3 = Path.Combine(_root, "ws3");
        NdsExtractor.Extract(rom2, ws3);
        var (_, filesAfter, _) = NdsFileSystem.Parse(File.ReadAllBytes(Path.Combine(ws3, "fnt.bin")));

        Assert.Equal(filesBefore.Count + 1, filesAfter.Count);
        Assert.True(File.Exists(Path.Combine(ws3, "data", "fielddata", "novo.narc")));
        // arquivo novo entra no fim da própria pasta; ids anteriores na mesma ordem relativa
        var orderBefore = filesBefore.OrderBy(f => f.FileId).Select(f => f.Path).ToList();
        var orderAfter = filesAfter.OrderBy(f => f.FileId).Select(f => f.Path).Where(p => p != "fielddata/novo.narc").ToList();
        Assert.Equal(orderBefore, orderAfter);
    }

    [Fact]
    public void Fnt_build_and_parse_roundtrip()
    {
        var tree = new List<FntEntry>
        {
            new() { Name = "a.bin", IsDirectory = false },
            new() { Name = "dir1", IsDirectory = true, Children =
            [
                new() { Name = "c.bin", IsDirectory = false },
                new() { Name = "sub", IsDirectory = true, Children = [ new() { Name = "d.bin", IsDirectory = false } ] },
            ]},
            new() { Name = "b.bin", IsDirectory = false },
            new() { Name = "dir2", IsDirectory = true, Children = [] },
        };
        var fnt = NdsFileSystem.Build(tree, 10);
        var (parsed, files, first) = NdsFileSystem.Parse(fnt);
        Assert.Equal(10, first);
        Assert.True(FntEntry.TreeEquals(tree, parsed));
        Assert.Equal(["a.bin", "b.bin", "dir1/c.bin", "dir1/sub/d.bin"], files.OrderBy(f => f.FileId).Select(f => f.Path));
        Assert.Equal([10, 11, 12, 13], files.OrderBy(f => f.FileId).Select(f => f.FileId));
        Assert.Equal(4, BitConverter.ToUInt16(fnt, 6)); // total de diretórios no root
    }

    /// <summary>Com POKEHUB_REAL_ROM=&lt;caminho.nds&gt;: extrai e reempacota um dump real e exige igualdade byte a byte até o tamanho usado.</summary>
    [Fact]
    public void Real_rom_roundtrip_is_byte_identical()
    {
        var romPath = Environment.GetEnvironmentVariable("POKEHUB_REAL_ROM");
        if (string.IsNullOrEmpty(romPath) || !File.Exists(romPath)) return;

        var ws = Path.Combine(_root, "real");
        var manifest = NdsExtractor.Extract(romPath, ws);
        var output = Path.Combine(_root, "real.nds");
        NdsPacker.Pack(ws, output);

        var original = File.ReadAllBytes(romPath);
        var rebuilt = File.ReadAllBytes(output);
        uint used = BitConverter.ToUInt32(original, NdsHeader.TotalUsedSize);
        int firstDiff = original.AsSpan(0, (int)used).CommonPrefixLength(rebuilt);
        Assert.True(firstDiff == used, $"primeira diferença em 0x{firstDiff:X} (manifesto: {manifest.FileOrder?.Count} arquivos ordenados)");
        // preenchida até a capacidade do chip: idêntica ao dump inteiro, inclusive o 0xFF final
        Assert.Equal(original.Length, rebuilt.Length);
        Assert.True(original.AsSpan().SequenceEqual(rebuilt), "ROM gerada difere do dump após a área usada");
    }

    [Fact]
    public void Crc16_matches_known_vector()
    {
        // CRC-16/MODBUS de "123456789" = 0x4B37
        Assert.Equal(0x4B37, Crc16.Compute(Encoding.ASCII.GetBytes("123456789")));
    }
}
