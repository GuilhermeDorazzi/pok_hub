using System.Text.Json;
using PokeHub.Core.Hub;

namespace PokeHub.Core.Rom;

/// <summary>Offsets do header NDS usados pelo extrator e pelo empacotador.</summary>
public static class NdsHeader
{
    public const int Size = 0x200;
    public const int Arm9Offset = 0x20, Arm9Entry = 0x24, Arm9Ram = 0x28, Arm9Size = 0x2C;
    public const int Arm7Offset = 0x30, Arm7Entry = 0x34, Arm7Ram = 0x38, Arm7Size = 0x3C;
    public const int FntOffset = 0x40, FntSize = 0x44, FatOffset = 0x48, FatSize = 0x4C;
    public const int Arm9OvtOffset = 0x50, Arm9OvtSize = 0x54, Arm7OvtOffset = 0x58, Arm7OvtSize = 0x5C;
    public const int SecureAreaCrc = 0x6C, BannerOffset = 0x68, Capacity = 0x14;
    public const int TotalUsedSize = 0x80, HeaderSize = 0x84, HeaderCrc = 0x15E;
    public const int OverlayEntrySize = 32, OverlayFileIdField = 0x18;
    public const uint NitroFooterMagic = 0xDEC00621;
    public const int SecureAreaStart = 0x4000, SecureAreaLength = 0x4000;
    /// <summary>header.bin guarda os 0x4000 bytes iniciais (header + região reservada), não só os 0x200 do ndstool.</summary>
    public const int FullHeaderSize = 0x4000;
}

/// <summary>Nomes de arquivos dentro de workspace/ (mesma convenção do ndstool, que o DSPRE também usa).</summary>
public sealed class WorkspaceLayout(string workspace)
{
    public string Root { get; } = workspace;
    public string HeaderBin => Path.Combine(Root, "header.bin");
    public string Arm9Bin => Path.Combine(Root, "arm9.bin");
    public string Arm7Bin => Path.Combine(Root, "arm7.bin");
    public string Y9Bin => Path.Combine(Root, "y9.bin");
    public string Y7Bin => Path.Combine(Root, "y7.bin");
    public string BannerBin => Path.Combine(Root, "banner.bin");
    public string FntBin => Path.Combine(Root, "fnt.bin");
    public string OverlayDir => Path.Combine(Root, "overlay");
    public string DataDir => Path.Combine(Root, "data");
    public string ManifestFile => Path.Combine(Root, "nds-layout.json");
    public string OverlayFile(uint overlayId) => Path.Combine(OverlayDir, $"overlay_{overlayId:D4}.bin");

    public bool IsExtracted => File.Exists(HeaderBin) && File.Exists(Arm9Bin) && Directory.Exists(DataDir);
}

/// <summary>nds-layout.json: o que o extrator sabia da ROM original, para o empacotador reproduzir ids e ordem.</summary>
public sealed class WorkspaceManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string GameCode { get; set; } = "";
    public DateTimeOffset ExtractedAt { get; set; } = DateTimeOffset.Now;
    public string SourceRomSha1 { get; set; } = "";
    public int FirstDataFileId { get; set; }
    public bool Arm9HasNitroFooter { get; set; }
    /// <summary>CRC da área segura como veio no header original. Dumps de cartucho têm os 8 primeiros bytes destruídos, então recalcular nunca bate.</summary>
    public ushort OriginalSecureAreaCrc { get; set; }
    /// <summary>SHA-1 dos bytes 0x4000..0x7FFF originais; se não mudaram, o CRC original é mantido.</summary>
    public string? SecureAreaSha1 { get; set; }
    /// <summary>Ids dos arquivos de data/ na ordem física da ROM original, para reproduzir o layout.</summary>
    public List<int>? FileOrder { get; set; }
    public List<FntEntry> Tree { get; set; } = [];

    public static WorkspaceManifest? Load(string file) =>
        File.Exists(file) ? JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(file), HubConfig.JsonOptions) : null;

    public void Save(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, HubConfig.JsonOptions));
}
