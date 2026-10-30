using System.Text.Json;
using PokeHub.Core.Hub;
using PokeHub.Core.Processes;

namespace PokeHub.Core.Rom;

/// <summary>
/// Wrapper do ds-rom (AetiasHax/ds-rom, MIT): o formato de workspace que o DSPRE 2.x usa nativamente.
/// `dsrom extract` gera config.yaml, arm9/ (descomprimido, com itcm/dtcm separados), arm9_overlays/ (descomprimidos),
/// arm7/, banner/, files/ e path_order.txt. `dsrom build` recomprime e monta a ROM.
/// </summary>
public static class DsRomTool
{
    public const string ConfigFileName = "config.yaml";
    /// <summary>Metadados do hub guardados ao lado do config.yaml do ds-rom.</summary>
    public const string HubMetaFileName = "pokehub-workspace.json";

    public sealed class HubMeta
    {
        public string Format { get; set; } = "dsrom";
        public string GameCode { get; set; } = "";
        public string SourceRomSha1 { get; set; } = "";
        public ushort OriginalSecureAreaCrc { get; set; }
        public string? SecureAreaSha1 { get; set; }
        public DateTimeOffset ExtractedAt { get; set; } = DateTimeOffset.Now;
    }

    public static bool IsDsRomWorkspace(string workspace) => File.Exists(Path.Combine(workspace, ConfigFileName));

    public static async Task<HubMeta> ExtractAsync(string dsrom, string romPath, string workspace, IProgress<string> log, CancellationToken ct = default)
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace)!);

        var code = await ProcessRunner.RunAsync(dsrom, ["extract", "-r", romPath, "-o", workspace], null, new Progress<string>(l => log.Report(TrimLog(l))), ct);
        if (code != 0) throw new InvalidOperationException($"dsrom extract falhou (código {code}). Veja o log.");
        if (!IsDsRomWorkspace(workspace)) throw new InvalidDataException("dsrom extract terminou sem gerar config.yaml.");

        var header = NdsRomHeader.Read(romPath);
        var rom = File.ReadAllBytes(romPath);
        bool hasSecure = rom.Length >= NdsHeader.SecureAreaStart + NdsHeader.SecureAreaLength;
        var meta = new HubMeta
        {
            GameCode = header.GameCode,
            SourceRomSha1 = NdsExtractor.Sha1Hex(rom),
            OriginalSecureAreaCrc = BitConverter.ToUInt16(rom, NdsHeader.SecureAreaCrc),
            SecureAreaSha1 = hasSecure ? NdsExtractor.Sha1Hex(rom.AsSpan(NdsHeader.SecureAreaStart, NdsHeader.SecureAreaLength)) : null,
        };
        SaveMeta(workspace, meta);
        return meta;
    }

    public static async Task BuildAsync(string dsrom, string workspace, string outputRom, IProgress<string> log, CancellationToken ct = default)
    {
        var config = Path.Combine(workspace, ConfigFileName);
        if (!File.Exists(config)) throw new FileNotFoundException("config.yaml não encontrado: o workspace não está no formato ds-rom.", config);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputRom))!);

        var code = await ProcessRunner.RunAsync(dsrom, ["build", "-c", config, "-o", outputRom], null, new Progress<string>(l => log.Report(TrimLog(l))), ct);
        if (code != 0 || !File.Exists(outputRom)) throw new InvalidOperationException($"dsrom build falhou (código {code}). Veja o log.");

        RestoreSecureAreaCrc(outputRom, LoadMeta(workspace), log);
    }

    /// <summary>
    /// O ds-rom zera o CRC da área segura. Se os bytes 0x4000..0x7FFF saíram iguais aos do dump original,
    /// devolve o CRC original e recalcula o CRC do header.
    /// </summary>
    public static void RestoreSecureAreaCrc(string romPath, HubMeta? meta, IProgress<string>? log)
    {
        if (meta?.SecureAreaSha1 is null) return;
        using var fs = new FileStream(romPath, FileMode.Open, FileAccess.ReadWrite);
        if (fs.Length < NdsHeader.SecureAreaStart + NdsHeader.SecureAreaLength) return;

        var secure = new byte[NdsHeader.SecureAreaLength];
        fs.Position = NdsHeader.SecureAreaStart;
        fs.ReadExactly(secure);
        if (NdsExtractor.Sha1Hex(secure) != meta.SecureAreaSha1)
        {
            log?.Report("Aviso: a área segura do arm9 mudou; CRC da área segura ficou como o ds-rom gerou.");
            return;
        }

        var header = new byte[NdsHeader.Size];
        fs.Position = 0;
        fs.ReadExactly(header);
        BitConverter.GetBytes(meta.OriginalSecureAreaCrc).CopyTo(header, NdsHeader.SecureAreaCrc);
        BitConverter.GetBytes(Crc16.Compute(header.AsSpan(0, NdsHeader.HeaderCrc))).CopyTo(header, NdsHeader.HeaderCrc);
        fs.Position = 0;
        fs.Write(header);
    }

    public static HubMeta? LoadMeta(string workspace)
    {
        var file = Path.Combine(workspace, HubMetaFileName);
        return File.Exists(file) ? JsonSerializer.Deserialize<HubMeta>(File.ReadAllText(file), HubConfig.JsonOptions) : null;
    }

    public static void SaveMeta(string workspace, HubMeta meta) =>
        File.WriteAllText(Path.Combine(workspace, HubMetaFileName), JsonSerializer.Serialize(meta, HubConfig.JsonOptions));

    /// <summary>Tira o prefixo "[data INFO  ds_rom::...]" das linhas do ds-rom.</summary>
    private static string TrimLog(string line)
    {
        int end = line.IndexOf("] ", StringComparison.Ordinal);
        return line.StartsWith('[') && end > 0 ? "  dsrom: " + line[(end + 2)..] : line;
    }
}
