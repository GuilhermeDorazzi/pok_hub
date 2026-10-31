using PokeHub.Core.Localization;

namespace PokeHub.Core.Rom;

/// <summary>
/// Empacota a pasta workspace/ de volta numa ROM .nds.
/// Reaproveita a FNT original quando a árvore de data/ não mudou (ids idênticos aos da ROM original);
/// caso contrário reconstrói a FNT preservando a ordem conhecida e acrescentando arquivos novos no fim de cada pasta.
/// </summary>
public static class NdsPacker
{
    private const int Align = 0x200;
    private const byte Fill = 0xFF;

    public static void Pack(string workspaceDir, string outputRom, IProgress<string>? log = null)
    {
        var layout = new WorkspaceLayout(workspaceDir);
        if (!layout.IsExtracted) throw new InvalidOperationException(Loc.T("core.ws.notExtracted"));
        var manifest = WorkspaceManifest.Load(layout.ManifestFile);

        var headerFile = File.ReadAllBytes(layout.HeaderBin);
        if (headerFile.Length < NdsHeader.Size) throw new InvalidDataException(Loc.T("core.nds.headerInvalid"));
        // região 0..0x4000 inteira (header + reservado), zeros se o header.bin só tem os 0x200 bytes do ndstool
        var headerFull = new byte[NdsHeader.FullHeaderSize];
        headerFile.AsSpan(0, Math.Min(headerFile.Length, NdsHeader.FullHeaderSize)).CopyTo(headerFull);
        var header = headerFull[..NdsHeader.Size];

        var arm9 = File.ReadAllBytes(layout.Arm9Bin);
        bool footer = arm9.Length >= 12 && BitConverter.ToUInt32(arm9, arm9.Length - 12) == NdsHeader.NitroFooterMagic;
        uint arm9Size = (uint)(footer ? arm9.Length - 12 : arm9.Length);
        var arm7 = File.ReadAllBytes(layout.Arm7Bin);
        var y9 = File.Exists(layout.Y9Bin) ? File.ReadAllBytes(layout.Y9Bin) : [];
        var y7 = File.Exists(layout.Y7Bin) ? File.ReadAllBytes(layout.Y7Bin) : [];
        var banner = File.Exists(layout.BannerBin) ? File.ReadAllBytes(layout.BannerBin) : [];

        // FNT: original se a árvore não mudou, senão reconstruída
        var tree = NdsFileSystem.FromDirectory(layout.DataDir, manifest?.Tree);
        int overlayCount = (y9.Length + y7.Length) / NdsHeader.OverlayEntrySize;
        int firstFileId = manifest?.FirstDataFileId ?? overlayCount;
        byte[] fnt;
        if (manifest is not null && File.Exists(layout.FntBin) && FntEntry.TreeEquals(tree, manifest.Tree))
        {
            fnt = File.ReadAllBytes(layout.FntBin);
            log?.Report(Loc.T("core.nds.fntReused"));
        }
        else
        {
            fnt = NdsFileSystem.Build(tree, firstFileId);
            log?.Report(Loc.T("core.nds.fntRebuilt"));
        }
        var (_, files, _) = NdsFileSystem.Parse(fnt);

        var overlayEntries = new List<(uint OverlayId, uint FileId)>();
        foreach (var table in new[] { y9, y7 })
            for (int i = 0; i + NdsHeader.OverlayEntrySize <= table.Length; i += NdsHeader.OverlayEntrySize)
                overlayEntries.Add((BitConverter.ToUInt32(table, i), BitConverter.ToUInt32(table, i + NdsHeader.OverlayFileIdField)));

        int fatCount = Math.Max(files.Count > 0 ? files.Max(f => f.FileId) + 1 : 0,
                                overlayEntries.Count > 0 ? (int)overlayEntries.Max(o => o.FileId) + 1 : 0);
        var fat = new (uint Start, uint End)[fatCount];

        using var rom = new MemoryStream();
        rom.SetLength(NdsHeader.SecureAreaStart); // header + reserva até 0x4000 (zeros)
        rom.Position = NdsHeader.SecureAreaStart;

        void U32(int off, uint v) => BitConverter.GetBytes(v).CopyTo(header, off);
        uint Place(byte[] data)
        {
            Pad(rom);
            uint start = (uint)rom.Position;
            rom.Write(data);
            return start;
        }

        // ARM9 (+ footer) sempre em 0x4000
        rom.Write(arm9);
        U32(NdsHeader.Arm9Offset, NdsHeader.SecureAreaStart);
        U32(NdsHeader.Arm9Size, arm9Size);

        // tabela de overlays do ARM9 e seus arquivos
        U32(NdsHeader.Arm9OvtOffset, y9.Length > 0 ? Place(y9) : 0);
        U32(NdsHeader.Arm9OvtSize, (uint)y9.Length);
        WriteOverlays(y9);

        // ARM7, tabela e overlays
        U32(NdsHeader.Arm7Offset, Place(arm7));
        U32(NdsHeader.Arm7Size, (uint)arm7.Length);
        U32(NdsHeader.Arm7OvtOffset, y7.Length > 0 ? Place(y7) : 0);
        U32(NdsHeader.Arm7OvtSize, (uint)y7.Length);
        WriteOverlays(y7);

        // FNT, FAT (reservada), banner
        U32(NdsHeader.FntOffset, Place(fnt));
        U32(NdsHeader.FntSize, (uint)fnt.Length);
        Pad(rom);
        uint fatOffset = (uint)rom.Position;
        rom.SetLength(rom.Length + fatCount * 8);
        rom.Position = rom.Length;
        U32(NdsHeader.FatOffset, fatOffset);
        U32(NdsHeader.FatSize, (uint)(fatCount * 8));
        U32(NdsHeader.BannerOffset, banner.Length > 0 ? Place(banner) : 0);

        // arquivos de data/ na ordem física original (arquivos novos no fim, por id)
        var position = new Dictionary<int, int>();
        if (manifest?.FileOrder is { } order) for (int i = 0; i < order.Count; i++) position[order[i]] = i;
        var ordered = files.OrderBy(f => position.TryGetValue(f.FileId, out var i) ? i : int.MaxValue).ThenBy(f => f.FileId);
        int n = 0;
        foreach (var f in ordered)
        {
            var path = Path.Combine(layout.DataDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            var data = File.ReadAllBytes(path);
            uint start = Place(data);
            fat[f.FileId] = (start, start + (uint)data.Length);
            if (++n % 500 == 0) log?.Report(Loc.T("core.nds.progress", n, files.Count));
        }

        uint total = (uint)rom.Position;
        rom.Position = fatOffset;
        foreach (var (s, e) in fat) { rom.Write(BitConverter.GetBytes(s)); rom.Write(BitConverter.GetBytes(e)); }

        // campos finais do header
        U32(NdsHeader.TotalUsedSize, total);
        U32(NdsHeader.HeaderSize, NdsHeader.SecureAreaStart);
        header[NdsHeader.Capacity] = CapacityByte(total);
        var buffer = rom.GetBuffer();
        bool hasSecureArea = total >= NdsHeader.SecureAreaStart + NdsHeader.SecureAreaLength;
        ushort secureCrc;
        if (hasSecureArea && manifest?.SecureAreaSha1 is { } sha
            && NdsExtractor.Sha1Hex(buffer.AsSpan(NdsHeader.SecureAreaStart, NdsHeader.SecureAreaLength)) == sha)
        {
            // área segura intacta: o CRC original (calculado pela Nintendo sobre a área cifrada íntegra) continua válido
            secureCrc = manifest.OriginalSecureAreaCrc;
        }
        else
        {
            secureCrc = hasSecureArea ? Crc16.Compute(buffer.AsSpan(NdsHeader.SecureAreaStart, NdsHeader.SecureAreaLength)) : (ushort)0xFFFF;
            if (hasSecureArea && manifest?.SecureAreaSha1 is not null)
                log?.Report(Loc.T("core.nds.secureChanged"));
        }
        BitConverter.GetBytes(secureCrc).CopyTo(header, NdsHeader.SecureAreaCrc);
        BitConverter.GetBytes(Crc16.Compute(header.AsSpan(0, NdsHeader.HeaderCrc))).CopyTo(header, NdsHeader.HeaderCrc);
        header.CopyTo(headerFull, 0);
        rom.Position = 0;
        rom.Write(headerFull);

        // preenche com 0xFF até o tamanho do chip (128 KB << capacidade), como o cartucho original
        uint chipSize = 128u * 1024u << header[NdsHeader.Capacity];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputRom))!);
        using (var fs = File.Create(outputRom))
        {
            fs.Write(buffer, 0, (int)total);
            var fill = new byte[1 << 16];
            Array.Fill(fill, Fill);
            long remaining = chipSize - total;
            while (remaining > 0) { int chunk = (int)Math.Min(remaining, fill.Length); fs.Write(fill, 0, chunk); remaining -= chunk; }
        }
        log?.Report(Loc.T("core.nds.romGenerated", outputRom, total / 1024 / 1024, chipSize / 1024 / 1024, files.Count, overlayEntries.Count));

        void WriteOverlays(byte[] table)
        {
            for (int i = 0; i + NdsHeader.OverlayEntrySize <= table.Length; i += NdsHeader.OverlayEntrySize)
            {
                uint overlayId = BitConverter.ToUInt32(table, i);
                uint fileId = BitConverter.ToUInt32(table, i + NdsHeader.OverlayFileIdField);
                var file = layout.OverlayFile(overlayId);
                if (!File.Exists(file)) throw new FileNotFoundException(Loc.T("core.nds.overlayMissing", overlayId), file);
                var data = File.ReadAllBytes(file);
                uint start = Place(data);
                fat[fileId] = (start, start + (uint)data.Length);
            }
        }
    }

    private static void Pad(MemoryStream s)
    {
        long rem = s.Position % Align;
        if (rem == 0) return;
        for (long i = rem; i < Align; i++) s.WriteByte(Fill);
    }

    /// <summary>Byte 0x14 do header: tamanho do chip = 128 KB &lt;&lt; n.</summary>
    public static byte CapacityByte(uint totalSize)
    {
        byte n = 0;
        while (n < 0x0F && (128u * 1024u << n) < totalSize) n++;
        return n;
    }
}
