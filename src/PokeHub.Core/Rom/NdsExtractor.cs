using PokeHub.Core.Localization;
using System.Security.Cryptography;

namespace PokeHub.Core.Rom;

/// <summary>
/// Extrai uma ROM .nds para a pasta workspace/ no layout do ndstool:
/// header.bin, arm9.bin (com nitro footer, se houver), arm7.bin, y9.bin, y7.bin, overlay/, banner.bin, data/.
/// A área segura (secure area) do arm9 é copiada sem decifrar: fica válida enquanto não for alterada.
/// </summary>
public static class NdsExtractor
{
    public static WorkspaceManifest Extract(string romPath, string workspaceDir, IProgress<string>? log = null, bool overwrite = false)
    {
        var layout = new WorkspaceLayout(workspaceDir);
        if (layout.IsExtracted && !overwrite)
            throw new InvalidOperationException(Loc.T("core.ws.alreadyExtracted"));

        log?.Report(Loc.T("core.nds.reading", Path.GetFileName(romPath)));
        var rom = File.ReadAllBytes(romPath);
        if (rom.Length < NdsHeader.Size) throw new InvalidDataException(Loc.T("core.nds.tooSmall"));
        var h = rom[..NdsHeader.Size];
        uint U32(int off) => BitConverter.ToUInt32(h, off);

        if (Directory.Exists(workspaceDir)) Directory.Delete(workspaceDir, recursive: true);
        Directory.CreateDirectory(workspaceDir);
        Directory.CreateDirectory(layout.OverlayDir);
        Directory.CreateDirectory(layout.DataDir);

        File.WriteAllBytes(layout.HeaderBin, rom[..Math.Min(rom.Length, NdsHeader.FullHeaderSize)]);

        // ARM9 (+ nitro footer de 12 bytes logo após, se presente) e ARM7
        var arm9Off = U32(NdsHeader.Arm9Offset); var arm9Size = U32(NdsHeader.Arm9Size);
        bool footer = arm9Off + arm9Size + 12 <= (uint)rom.Length
                      && BitConverter.ToUInt32(rom, (int)(arm9Off + arm9Size)) == NdsHeader.NitroFooterMagic;
        File.WriteAllBytes(layout.Arm9Bin, Slice(rom, arm9Off, arm9Size + (footer ? 12u : 0u), "arm9"));
        File.WriteAllBytes(layout.Arm7Bin, Slice(rom, U32(NdsHeader.Arm7Offset), U32(NdsHeader.Arm7Size), "arm7"));
        log?.Report(Loc.T("core.nds.arm", arm9Size, footer ? " + footer" : "", U32(NdsHeader.Arm7Size)));

        // Tabelas de overlay
        var y9 = Slice(rom, U32(NdsHeader.Arm9OvtOffset), U32(NdsHeader.Arm9OvtSize), "y9");
        var y7 = Slice(rom, U32(NdsHeader.Arm7OvtOffset), U32(NdsHeader.Arm7OvtSize), "y7");
        File.WriteAllBytes(layout.Y9Bin, y9);
        File.WriteAllBytes(layout.Y7Bin, y7);

        // FAT
        var fat = ReadFat(rom, U32(NdsHeader.FatOffset), U32(NdsHeader.FatSize));

        int overlays = 0;
        foreach (var table in new[] { y9, y7 })
            for (int i = 0; i + NdsHeader.OverlayEntrySize <= table.Length; i += NdsHeader.OverlayEntrySize)
            {
                uint overlayId = BitConverter.ToUInt32(table, i);
                uint fileId = BitConverter.ToUInt32(table, i + NdsHeader.OverlayFileIdField);
                var (start, end) = FatEntry(fat, fileId);
                File.WriteAllBytes(layout.OverlayFile(overlayId), Slice(rom, start, end - start, $"overlay {overlayId}"));
                overlays++;
            }
        log?.Report(Loc.T("core.nds.overlays", overlays));

        // Banner (tamanho depende da versão)
        var bannerOff = U32(NdsHeader.BannerOffset);
        if (bannerOff != 0 && bannerOff < rom.Length)
        {
            ushort version = BitConverter.ToUInt16(rom, (int)bannerOff);
            uint size = BannerSize(version);
            File.WriteAllBytes(layout.BannerBin, Slice(rom, bannerOff, Math.Min(size, (uint)rom.Length - bannerOff), "banner"));
        }

        // FNT e arquivos
        var fnt = Slice(rom, U32(NdsHeader.FntOffset), U32(NdsHeader.FntSize), "fnt");
        File.WriteAllBytes(layout.FntBin, fnt);
        var (tree, files, firstFileId) = NdsFileSystem.Parse(fnt);
        CreateDirectories(tree, layout.DataDir); // inclui pastas vazias, para a FNT reconstruída ficar igual
        int n = 0;
        foreach (var f in files)
        {
            var (start, end) = FatEntry(fat, (uint)f.FileId);
            var dest = Path.Combine(layout.DataDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, Slice(rom, start, end - start, f.Path));
            if (++n % 500 == 0) log?.Report(Loc.T("core.nds.progress", n, files.Count));
        }
        log?.Report(Loc.T("core.nds.filesInData", files.Count));

        bool hasSecureArea = rom.Length >= NdsHeader.SecureAreaStart + NdsHeader.SecureAreaLength;
        var manifest = new WorkspaceManifest
        {
            GameCode = NdsRomHeader.Read(romPath).GameCode,
            SourceRomSha1 = Convert.ToHexString(SHA1.HashData(rom)).ToLowerInvariant(),
            FirstDataFileId = firstFileId,
            Arm9HasNitroFooter = footer,
            OriginalSecureAreaCrc = BitConverter.ToUInt16(h, NdsHeader.SecureAreaCrc),
            SecureAreaSha1 = hasSecureArea ? Sha1Hex(rom.AsSpan(NdsHeader.SecureAreaStart, NdsHeader.SecureAreaLength)) : null,
            FileOrder = files.OrderBy(f => fat[f.FileId].Start).Select(f => f.FileId).ToList(),
            Tree = tree,
        };
        manifest.Save(layout.ManifestFile);
        return manifest;
    }

    public static string Sha1Hex(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    private static void CreateDirectories(IEnumerable<FntEntry> entries, string dir)
    {
        foreach (var e in entries.Where(e => e.IsDirectory))
        {
            var sub = Path.Combine(dir, e.Name);
            Directory.CreateDirectory(sub);
            CreateDirectories(e.Children ?? [], sub);
        }
    }

    public static uint BannerSize(ushort version) => version switch
    {
        0x0002 => 0x940,
        0x0003 => 0xA40,
        0x0103 => 0x23C0,
        _ => 0x840,
    };

    private static byte[] Slice(byte[] rom, uint off, uint size, string what)
    {
        if (size == 0) return [];
        if (off > rom.Length || off + size > (uint)rom.Length)
            throw new InvalidDataException($"{what}: região fora da ROM (offset 0x{off:X}, tamanho 0x{size:X}).");
        return rom.AsSpan((int)off, (int)size).ToArray();
    }

    private static (uint Start, uint End)[] ReadFat(byte[] rom, uint off, uint size)
    {
        var fat = new (uint, uint)[size / 8];
        for (int i = 0; i < fat.Length; i++)
            fat[i] = (BitConverter.ToUInt32(rom, (int)off + i * 8), BitConverter.ToUInt32(rom, (int)off + i * 8 + 4));
        return fat;
    }

    private static (uint Start, uint End) FatEntry((uint Start, uint End)[] fat, uint fileId)
    {
        if (fileId >= fat.Length) throw new InvalidDataException($"File id {fileId} fora da FAT ({fat.Length} entradas).");
        var e = fat[fileId];
        if (e.End < e.Start) throw new InvalidDataException($"Entrada FAT {fileId} inválida.");
        return e;
    }
}
