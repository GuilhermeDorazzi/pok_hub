namespace PokeHub.Core.Gen4;

/// <summary>Um slot de encontro: espécie e faixa de nível (igual quando o jogo guarda um único nível).</summary>
public sealed class EncounterSlot
{
    public ushort Species { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
}

/// <summary>Probabilidades fixas dos slots do jogo (Gen 4).</summary>
public static class EncounterOdds
{
    public static readonly int[] Walking = [20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1];
    public static readonly int[] Surf = [60, 30, 5, 4, 1];
}

/// <summary>
/// Arquivo de encontros de HGSS (196 bytes): taxas, 12 níveis da grama com espécies por período (manhã/dia/noite),
/// 2 espécies por som do PokéGear (Hoenn, Sinnoh), surf (5), rock smash (2), varas (5 cada) e enxame (4).
/// </summary>
public sealed class HgssEncounters
{
    public const int Size = 196;
    public byte WalkingRate, SurfRate, RockSmashRate, OldRodRate, GoodRodRate, SuperRodRate;
    public byte[] WalkingLevels = new byte[12];
    public ushort[] Morning = new ushort[12];
    public ushort[] Day = new ushort[12];
    public ushort[] Night = new ushort[12];
    public ushort[] HoennSound = new ushort[2];
    public ushort[] SinnohSound = new ushort[2];
    public EncounterSlot[] Surf = NewSlots(5);
    public EncounterSlot[] RockSmash = NewSlots(2);
    public EncounterSlot[] OldRod = NewSlots(5);
    public EncounterSlot[] GoodRod = NewSlots(5);
    public EncounterSlot[] SuperRod = NewSlots(5);
    /// <summary>Enxame: grama, surf, vara boa, super vara.</summary>
    public ushort[] Swarm = new ushort[4];

    private static EncounterSlot[] NewSlots(int n) => Enumerable.Range(0, n).Select(_ => new EncounterSlot()).ToArray();

    public static HgssEncounters Parse(byte[] d)
    {
        if (d.Length < Size) throw new InvalidDataException($"HGSS encounter file: {d.Length} bytes, expected {Size}.");
        var e = new HgssEncounters
        {
            WalkingRate = d[0], SurfRate = d[1], RockSmashRate = d[2], OldRodRate = d[3], GoodRodRate = d[4], SuperRodRate = d[5],
        };
        Array.Copy(d, 8, e.WalkingLevels, 0, 12);
        int o = 20;
        for (int i = 0; i < 12; i++, o += 2) e.Morning[i] = BitConverter.ToUInt16(d, o);
        for (int i = 0; i < 12; i++, o += 2) e.Day[i] = BitConverter.ToUInt16(d, o);
        for (int i = 0; i < 12; i++, o += 2) e.Night[i] = BitConverter.ToUInt16(d, o);
        for (int i = 0; i < 2; i++, o += 2) e.HoennSound[i] = BitConverter.ToUInt16(d, o);
        for (int i = 0; i < 2; i++, o += 2) e.SinnohSound[i] = BitConverter.ToUInt16(d, o);
        o = ReadSlots(d, o, e.Surf);
        o = ReadSlots(d, o, e.RockSmash);
        o = ReadSlots(d, o, e.OldRod);
        o = ReadSlots(d, o, e.GoodRod);
        o = ReadSlots(d, o, e.SuperRod);
        for (int i = 0; i < 4; i++, o += 2) e.Swarm[i] = BitConverter.ToUInt16(d, o);
        return e;
    }

    private static int ReadSlots(byte[] d, int o, EncounterSlot[] slots)
    {
        foreach (var s in slots) { s.MinLevel = d[o]; s.MaxLevel = d[o + 1]; s.Species = BitConverter.ToUInt16(d, o + 2); o += 4; }
        return o;
    }

    public byte[] Serialize()
    {
        var d = new byte[Size];
        d[0] = WalkingRate; d[1] = SurfRate; d[2] = RockSmashRate; d[3] = OldRodRate; d[4] = GoodRodRate; d[5] = SuperRodRate;
        Array.Copy(WalkingLevels, 0, d, 8, 12);
        int o = 20;
        foreach (var s in Morning) { BitConverter.GetBytes(s).CopyTo(d, o); o += 2; }
        foreach (var s in Day) { BitConverter.GetBytes(s).CopyTo(d, o); o += 2; }
        foreach (var s in Night) { BitConverter.GetBytes(s).CopyTo(d, o); o += 2; }
        foreach (var s in HoennSound) { BitConverter.GetBytes(s).CopyTo(d, o); o += 2; }
        foreach (var s in SinnohSound) { BitConverter.GetBytes(s).CopyTo(d, o); o += 2; }
        foreach (var group in new[] { Surf, RockSmash, OldRod, GoodRod, SuperRod })
            foreach (var s in group) { d[o] = s.MinLevel; d[o + 1] = s.MaxLevel; BitConverter.GetBytes(s.Species).CopyTo(d, o + 2); o += 4; }
        foreach (var s in Swarm) { BitConverter.GetBytes(s).CopyTo(d, o); o += 2; }
        return d;
    }
}

/// <summary>
/// Arquivo de encontros de DP/Pt (424 bytes, campos de 32 bits): grama (12 níveis+espécies), enxame (2), dia (2), noite (2),
/// radar (4), formas (5), desconhecido (1), dual-slot GBA (5 jogos × 2), surf e três varas (taxa + 5 slots com níveis max/min).
/// Fonte: EncounterFileDPPt do DSPRE.
/// </summary>
public sealed class DpptEncounters
{
    public const int Size = 424;
    public uint WalkingRate, SurfRate, OldRodRate, GoodRodRate, SuperRodRate;
    public EncounterSlot[] Walking = NewSlots(12);
    public ushort[] Swarm = new ushort[2];
    public ushort[] Day = new ushort[2];
    public ushort[] Night = new ushort[2];
    public ushort[] Radar = new ushort[4];
    public uint[] FormData = new uint[5];
    public uint Unknown;
    public ushort[] Ruby = new ushort[2], Sapphire = new ushort[2], Emerald = new ushort[2], FireRed = new ushort[2], LeafGreen = new ushort[2];
    public EncounterSlot[] Surf = NewSlots(5);
    public EncounterSlot[] OldRod = NewSlots(5);
    public EncounterSlot[] GoodRod = NewSlots(5);
    public EncounterSlot[] SuperRod = NewSlots(5);

    private static EncounterSlot[] NewSlots(int n) => Enumerable.Range(0, n).Select(_ => new EncounterSlot()).ToArray();

    public static DpptEncounters Parse(byte[] d)
    {
        if (d.Length < Size) throw new InvalidDataException($"DPPt encounter file: {d.Length} bytes, expected {Size}.");
        var e = new DpptEncounters { WalkingRate = BitConverter.ToUInt32(d, 0) };
        int o = 4;
        foreach (var s in e.Walking) { s.MinLevel = s.MaxLevel = (byte)BitConverter.ToUInt32(d, o); s.Species = (ushort)BitConverter.ToUInt32(d, o + 4); o += 8; }
        foreach (var arr in new[] { e.Swarm, e.Day, e.Night, e.Radar })
            for (int i = 0; i < arr.Length; i++, o += 4) arr[i] = (ushort)BitConverter.ToUInt32(d, o);
        for (int i = 0; i < 5; i++, o += 4) e.FormData[i] = BitConverter.ToUInt32(d, o);
        e.Unknown = BitConverter.ToUInt32(d, o); o += 4;
        foreach (var arr in new[] { e.Ruby, e.Sapphire, e.Emerald, e.FireRed, e.LeafGreen })
            for (int i = 0; i < 2; i++, o += 4) arr[i] = (ushort)BitConverter.ToUInt32(d, o);
        e.SurfRate = BitConverter.ToUInt32(d, o); o += 4;
        o = ReadWater(d, o, e.Surf);
        o = 0x124;
        e.OldRodRate = BitConverter.ToUInt32(d, o); o += 4; o = ReadWater(d, o, e.OldRod);
        e.GoodRodRate = BitConverter.ToUInt32(d, o); o += 4; o = ReadWater(d, o, e.GoodRod);
        e.SuperRodRate = BitConverter.ToUInt32(d, o); o += 4; ReadWater(d, o, e.SuperRod);
        return e;
    }

    private static int ReadWater(byte[] d, int o, EncounterSlot[] slots)
    {
        foreach (var s in slots) { s.MaxLevel = d[o]; s.MinLevel = d[o + 1]; s.Species = (ushort)BitConverter.ToUInt32(d, o + 4); o += 8; }
        return o;
    }

    public byte[] Serialize()
    {
        var d = new byte[Size];
        BitConverter.GetBytes(WalkingRate).CopyTo(d, 0);
        int o = 4;
        foreach (var s in Walking) { BitConverter.GetBytes((uint)s.MinLevel).CopyTo(d, o); BitConverter.GetBytes((uint)s.Species).CopyTo(d, o + 4); o += 8; }
        foreach (var arr in new[] { Swarm, Day, Night, Radar })
            foreach (var v in arr) { BitConverter.GetBytes((uint)v).CopyTo(d, o); o += 4; }
        foreach (var v in FormData) { BitConverter.GetBytes(v).CopyTo(d, o); o += 4; }
        BitConverter.GetBytes(Unknown).CopyTo(d, o); o += 4;
        foreach (var arr in new[] { Ruby, Sapphire, Emerald, FireRed, LeafGreen })
            foreach (var v in arr) { BitConverter.GetBytes((uint)v).CopyTo(d, o); o += 4; }
        BitConverter.GetBytes(SurfRate).CopyTo(d, o); o += 4;
        o = WriteWater(d, o, Surf);
        o = 0x124;
        BitConverter.GetBytes(OldRodRate).CopyTo(d, o); o += 4; o = WriteWater(d, o, OldRod);
        BitConverter.GetBytes(GoodRodRate).CopyTo(d, o); o += 4; o = WriteWater(d, o, GoodRod);
        BitConverter.GetBytes(SuperRodRate).CopyTo(d, o); o += 4; WriteWater(d, o, SuperRod);
        return d;
    }

    private static int WriteWater(byte[] d, int o, EncounterSlot[] slots)
    {
        foreach (var s in slots) { d[o] = s.MaxLevel; d[o + 1] = s.MinLevel; BitConverter.GetBytes((uint)s.Species).CopyTo(d, o + 4); o += 8; }
        return o;
    }
}
