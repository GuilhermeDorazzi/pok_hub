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
        if (!layout.IsExtracted) throw new InvalidOperationException("workspace/ não contém uma ROM extraída.");
        var manifest = WorkspaceManifest.Load(layout.ManifestFile);

        var header = File.ReadAllBytes(layout.HeaderBin);
        if (header.Length < NdsHeader.Size) throw new InvalidDataException("header.bin inválido.");
        header = header[..NdsHeader.Size];

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
            log?.Report("Árvore de data/ inalterada: FNT original reaproveitada.");
        }
        else
        {
            fnt = NdsFileSystem.Build(tree, firstFileId);
            log?.Report("Árvore de data/ alterada: FNT reconstruída (ids de arquivos novos no fim de cada pasta).");
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

        // arquivos de data/ em ordem de id
        int n = 0;
        foreach (var f in files.OrderBy(f => f.FileId))
        {
            var path = Path.Combine(layout.DataDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
            var data = File.ReadAllBytes(path);
            uint start = Place(data);
            fat[f.FileId] = (start, start + (uint)data.Length);
            if (++n % 500 == 0) log?.Report($"  {n}/{files.Count} arquivos");
        }

        uint total = (uint)rom.Position;
        rom.Position = fatOffset;
        foreach (var (s, e) in fat) { rom.Write(BitConverter.GetBytes(s)); rom.Write(BitConverter.GetBytes(e)); }

        // campos finais do header
        U32(NdsHeader.TotalUsedSize, total);
        U32(NdsHeader.HeaderSize, NdsHeader.SecureAreaStart);
        header[NdsHeader.Capacity] = CapacityByte(total);
        var buffer = rom.GetBuffer();
        ushort secureCrc = total >= NdsHeader.SecureAreaStart + NdsHeader.SecureAreaLength
            ? Crc16.Compute(buffer.AsSpan(NdsHeader.SecureAreaStart, NdsHeader.SecureAreaLength))
            : (ushort)0xFFFF;
        BitConverter.GetBytes(secureCrc).CopyTo(header, NdsHeader.SecureAreaCrc);
        BitConverter.GetBytes(Crc16.Compute(header.AsSpan(0, NdsHeader.HeaderCrc))).CopyTo(header, NdsHeader.HeaderCrc);
        rom.Position = 0;
        rom.Write(header);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputRom))!);
        File.WriteAllBytes(outputRom, buffer.AsSpan(0, (int)total).ToArray());
        log?.Report($"ROM gerada: {outputRom} ({total / 1024 / 1024} MB, {files.Count} arquivos, {overlayEntries.Count} overlays)");

        void WriteOverlays(byte[] table)
        {
            for (int i = 0; i + NdsHeader.OverlayEntrySize <= table.Length; i += NdsHeader.OverlayEntrySize)
            {
                uint overlayId = BitConverter.ToUInt32(table, i);
                uint fileId = BitConverter.ToUInt32(table, i + NdsHeader.OverlayFileIdField);
                var file = layout.OverlayFile(overlayId);
                if (!File.Exists(file)) throw new FileNotFoundException($"Overlay {overlayId} referenciado na tabela mas ausente.", file);
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
