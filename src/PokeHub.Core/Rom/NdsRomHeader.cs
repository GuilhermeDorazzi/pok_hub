using System.Text;

namespace PokeHub.Core.Rom;

/// <summary>Leitura mínima do header de uma ROM .nds (primeiros 0x200 bytes).</summary>
public sealed record NdsRomHeader(string Title, string GameCode, string MakerCode, uint RomSize)
{
    public BaseGame Game => GameInfo.FromGameCode(GameCode);
    public int Generation => GameInfo.Generation(Game);
    public string Region => GameInfo.Region(GameCode);

    public static NdsRomHeader Read(string romPath)
    {
        using var fs = File.OpenRead(romPath);
        var h = new byte[0x200];
        if (fs.Read(h, 0, h.Length) < h.Length) throw new InvalidDataException("Arquivo menor que um header NDS.");
        static string Ascii(ReadOnlySpan<byte> s) => Encoding.ASCII.GetString(s).TrimEnd('\0', ' ');
        return new NdsRomHeader(
            Title: Ascii(h.AsSpan(0, 12)),
            GameCode: Ascii(h.AsSpan(0x0C, 4)),
            MakerCode: Ascii(h.AsSpan(0x10, 2)),
            RomSize: BitConverter.ToUInt32(h, 0x80));
    }
}
