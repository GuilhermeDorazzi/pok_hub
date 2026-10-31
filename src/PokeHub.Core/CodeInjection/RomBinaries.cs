using System.Globalization;
using PokeHub.Core.Localization;

namespace PokeHub.Core.CodeInjection;

/// <summary>Traduz endereço de memória em arquivo e posição no workspace ds-rom, onde o arm9 e os overlays já estão descomprimidos.</summary>
public sealed class RomBinaries
{
    private readonly record struct Overlay(uint BaseAddress, string FileName);

    private readonly Dictionary<int, Overlay> _overlays = [];

    public string Workspace { get; }
    public uint Arm9Base { get; }
    public const string Arm9File = @"arm9\arm9.bin";

    public RomBinaries(string workspace)
    {
        Workspace = workspace;
        Arm9Base = ReadBaseAddress(Path.Combine(workspace, "arm9", "arm9.yaml"));
        var overlays = Path.Combine(workspace, "arm9_overlays", "overlays.yaml");
        if (File.Exists(overlays)) ParseOverlays(File.ReadLines(overlays));
    }

    /// <summary>Arquivo relativo ao workspace e posição nele. O tamanho do arquivo é conferido por quem grava.</summary>
    public (string File, int Offset) Locate(string target, uint address)
    {
        uint origin;
        string file;
        if (CodePatch.OverlayId(target) is { } id)
        {
            if (!_overlays.TryGetValue(id, out var o)) throw new InvalidDataException(Loc.T("core.inject.noOverlay", id));
            origin = o.BaseAddress;
            file = Path.Combine("arm9_overlays", o.FileName);
        }
        else
        {
            origin = Arm9Base;
            file = Arm9File;
        }
        if (address < origin) throw new InvalidDataException(Loc.T("core.inject.outsideFile", $"0x{address:X8}", target));
        return (file, (int)(address - origin));
    }

    private static uint ReadBaseAddress(string yaml)
    {
        if (!File.Exists(yaml)) throw new FileNotFoundException(Loc.T("core.inject.needDsRom"), yaml);
        foreach (var line in File.ReadLines(yaml))
            if (line.StartsWith("base_address:", StringComparison.Ordinal))
                return uint.Parse(line.AsSpan(13).Trim(), CultureInfo.InvariantCulture);
        throw new InvalidDataException(Loc.T("core.inject.needDsRom"));
    }

    /// <summary>Só os campos usados do overlays.yaml gerado pelo ds-rom: lista de "- id:", com base_address e file_name no mesmo item.</summary>
    private void ParseOverlays(IEnumerable<string> lines)
    {
        int? id = null;
        uint address = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("- id:", StringComparison.Ordinal)) id = int.Parse(line.AsSpan(5).Trim(), CultureInfo.InvariantCulture);
            else if (line.StartsWith("base_address:", StringComparison.Ordinal)) address = uint.Parse(line.AsSpan(13).Trim(), CultureInfo.InvariantCulture);
            else if (line.StartsWith("file_name:", StringComparison.Ordinal) && id is { } current) _overlays[current] = new Overlay(address, line[10..].Trim());
        }
    }
}
