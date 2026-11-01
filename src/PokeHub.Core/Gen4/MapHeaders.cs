using PokeHub.Core.Rom;

namespace PokeHub.Core.Gen4;

/// <summary>Resumo de um cabeçalho de mapa: o que liga um mapa ao seu arquivo de encontros e ao nome do local.</summary>
public sealed record MapHeaderInfo(int Id, string InternalName, int? EncounterFile, int LocationNameIndex)
{
    /// <summary>HGSS e Pt: arquivo de scripts, cabeçalho de level scripts (mesmo NARC), banco de texto do mapa e arquivo de eventos.</summary>
    public int ScriptsBank { get; init; } = -1;
    public int ScriptHeaderBank { get; init; } = -1;
    public int MsgBank { get; init; } = -1;
    public int EventsBank { get; init; } = -1;
    /// <summary>HGSS e Pt: matriz do mapa.</summary>
    public int MatrixId { get; init; } = -1;
    /// <summary>HGSS e Pt: área, que diz quais texturas o mapa usa.</summary>
    public int AreaId { get; init; } = -1;
    /// <summary>HGSS: 0 Johto, 1 Kanto (regionNo do cabeçalho). Nos outros jogos, sempre 0.</summary>
    public int Region { get; init; }
}

/// <summary>
/// Tabela de cabeçalhos de mapa (24 bytes cada) dentro do arm9. HGSS: byte 0 = arquivo de encontros (0xFF = nenhum),
/// byte 0x12 = índice do nome do local. DP/Pt: u16 em 0x0E = arquivo de encontros (0xFFFF = nenhum); nome do local em 0x12 (u16 em DP, byte em Pt).
/// Os nomes internos vêm de fielddata/maptable/mapname.bin (16 bytes por mapa), cujo tamanho dá a quantidade de cabeçalhos.
/// </summary>
public static class MapHeaders
{
    public const int Length = 24;

    public static IReadOnlyList<MapHeaderInfo> Load(Gen4Game game)
    {
        if (game.HeaderTableOffset is not { } offset || !File.Exists(game.MapNamesPath) || !File.Exists(game.Arm9Path)) return [];
        var names = File.ReadAllBytes(game.MapNamesPath);
        int count = names.Length / 16;
        var arm9 = Blz.DecompressArm9(File.ReadAllBytes(game.Arm9Path));
        if (offset + (long)count * Length > arm9.Length) return [];

        var list = new List<MapHeaderInfo>(count);
        for (int i = 0; i < count; i++)
        {
            int o = (int)offset + i * Length;
            string name = System.Text.Encoding.ASCII.GetString(names, i * 16, 16).TrimEnd('\0');
            int? enc; int loc;
            if (game.IsHgss)
            {
                enc = arm9[o] == 0xFF ? null : arm9[o];
                loc = arm9[o + 0x12];
                list.Add(new MapHeaderInfo(i, name, enc, loc)
                {
                    ScriptsBank = BitConverter.ToUInt16(arm9, o + 6),
                    ScriptHeaderBank = BitConverter.ToUInt16(arm9, o + 8),
                    MsgBank = BitConverter.ToUInt16(arm9, o + 10),
                    EventsBank = BitConverter.ToUInt16(arm9, o + 16),
                    MatrixId = BitConverter.ToUInt16(arm9, o + 4),
                    AreaId = arm9[o + 1],
                    Region = arm9[o + 0x14] & 1,
                });
                continue;
            }
            else
            {
                ushort e = BitConverter.ToUInt16(arm9, o + 0x0E);
                enc = e == 0xFFFF ? null : e;
                loc = game.IsDp ? BitConverter.ToUInt16(arm9, o + 0x12) : arm9[o + 0x12];
            }
            // Pt: matriz em 2, scripts em 4, level scripts em 6, texto em 8, eventos em 16 (MapHeader da pokeplatinum). DP sem ROM para conferir.
            list.Add(game.IsPlatinum
                ? new MapHeaderInfo(i, name, enc, loc)
                {
                    MatrixId = BitConverter.ToUInt16(arm9, o + 2),
                    AreaId = arm9[o],
                    ScriptsBank = BitConverter.ToUInt16(arm9, o + 4),
                    ScriptHeaderBank = BitConverter.ToUInt16(arm9, o + 6),
                    MsgBank = BitConverter.ToUInt16(arm9, o + 8),
                    EventsBank = BitConverter.ToUInt16(arm9, o + 16),
                }
                : new MapHeaderInfo(i, name, enc, loc));
        }
        return list;
    }
}
