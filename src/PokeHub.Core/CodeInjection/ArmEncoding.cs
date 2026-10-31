using PokeHub.Core.Localization;

namespace PokeHub.Core.CodeInjection;

/// <summary>Instruções que os ganchos gravam no código do jogo (ARMv5TE do ARM946E-S, em ARM ou Thumb).</summary>
public static class ArmEncoding
{
    private const long ThumbRange = 0x400000, ArmRange = 0x2000000;

    /// <summary>Chamada de 4 bytes a partir de <paramref name="site"/>. O bit 0 do alvo diz se ele é Thumb; o modo muda com BLX quando precisa.</summary>
    public static byte[] Call(uint site, uint target, bool siteThumb)
    {
        bool targetThumb = (target & 1) != 0;
        if (siteThumb)
        {
            RequireAligned(site, 2);
            long off = targetThumb
                ? (long)(target & ~1u) - (site + 4)
                : (long)(target & ~3u) - ((site + 4) & ~3u); // BLX usa o PC alinhado a 4 e o alvo é ARM
            RequireRange(off, ThumbRange, site, target);
            uint hi = 0xF000u | (uint)((off >> 12) & 0x7FF);
            uint lo = (targetThumb ? 0xF800u : 0xE800u) | (uint)((off >> 1) & 0x7FF);
            return [(byte)hi, (byte)(hi >> 8), (byte)lo, (byte)(lo >> 8)];
        }
        RequireAligned(site, 4);
        long armOff = (long)(target & ~1u) - (site + 8);
        RequireRange(armOff, ArmRange, site, target);
        uint word = targetThumb
            ? 0xFA000000u | (uint)((armOff >> 1) & 1) << 24 | (uint)((armOff >> 2) & 0xFFFFFF)
            : 0xEB000000u | (uint)((armOff >> 2) & 0xFFFFFF);
        return BitConverter.GetBytes(word);
    }

    /// <summary>
    /// Salto para qualquer endereço sem sujar registradores nem o lr, para substituir uma função inteira.
    /// ARM: ldr pc,[pc,#-4] + endereço (8 bytes). Thumb: bx pc; nop; ldr pc,[pc,#-4] + endereço (12 bytes, ou 14 fora do alinhamento de 4).
    /// </summary>
    public static byte[] Jump(uint site, uint target, bool siteThumb)
    {
        byte[] ldrPc = [0x04, 0xF0, 0x1F, 0xE5];
        var address = BitConverter.GetBytes(target);
        if (!siteThumb)
        {
            RequireAligned(site, 4);
            return [.. ldrPc, .. address];
        }
        RequireAligned(site, 2);
        byte[] bxPcNop = [0x78, 0x47, 0xC0, 0x46];
        return site % 4 == 0
            ? [.. bxPcNop, .. ldrPc, .. address]
            : [0xC0, 0x46, .. bxPcNop, .. ldrPc, .. address]; // bx pc só cai no modo ARM certo de um endereço múltiplo de 4
    }

    private static void RequireAligned(uint site, uint alignment)
    {
        if (site % alignment != 0) throw new InvalidDataException(Loc.T("core.inject.misaligned", $"0x{site:X8}", alignment));
    }

    private static void RequireRange(long offset, long range, uint site, uint target)
    {
        if (offset < -range || offset >= range) throw new InvalidDataException(Loc.T("core.inject.outOfRange", $"0x{site:X8}", $"0x{target:X8}"));
    }
}
