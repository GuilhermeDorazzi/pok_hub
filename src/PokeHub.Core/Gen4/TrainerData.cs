namespace PokeHub.Core.Gen4;

[Flags]
public enum GenderAbilityFlags : byte
{
    None = 0,
    ForceMale = 0x1,
    ForceFemale = 0x2,
    AbilitySlot1 = 0x10,
    AbilitySlot2 = 0x20,
}

/// <summary>Um Pokémon do time de um treinador (entrada do trpoke).</summary>
public sealed class PartyMember
{
    /// <summary>0..255; os IVs são difficulty * 31 / 255.</summary>
    public byte Difficulty { get; set; }
    public GenderAbilityFlags GenderAbility { get; set; }
    public ushort Level { get; set; } = 5;
    public ushort Species { get; set; } = 1;
    public ushort Form { get; set; }
    public ushort Item { get; set; }
    public ushort[] Moves { get; set; } = new ushort[4];
    public ushort BallSeal { get; set; }

    public PartyMember Clone()
    {
        var c = (PartyMember)MemberwiseClone();
        c.Moves = (ushort[])Moves.Clone();
        return c;
    }
}

/// <summary>Treinador: registro de 20 bytes do trdata + time no trpoke.</summary>
public sealed class Trainer
{
    public const int MaxParty = 6;
    public const int AiFlagCount = 11;

    public int Id { get; set; }
    public bool HasMoves { get; set; }
    public bool HasItems { get; set; }
    public byte Class { get; set; }
    public byte Unknown { get; set; }
    public ushort[] Items { get; set; } = new ushort[4];
    public uint AiFlags { get; set; }
    /// <summary>0 = simples, 2 = dupla.</summary>
    public uint BattleType { get; set; }
    public List<PartyMember> Party { get; set; } = [];
    public bool DoubleBattle { get => BattleType == 2; set => BattleType = value ? 2u : 0u; }

    /// <summary>Bytes originais lidos do NARC, para regravar sem alteração (inclusive lixo após o time) o que não foi editado.</summary>
    public byte[]? OriginalData { get; set; }
    public byte[]? OriginalParty { get; set; }

    public int PartyEntrySize(bool hasBallSeal) => 6 + (HasItems ? 2 : 0) + (HasMoves ? 8 : 0) + (hasBallSeal ? 2 : 0);

    public static Trainer Parse(int id, byte[] data, byte[] party, bool hasBallSeal)
    {
        if (data.Length < 20) throw new InvalidDataException($"Treinador {id}: registro com {data.Length} bytes (esperado 20).");
        var t = new Trainer
        {
            Id = id,
            HasMoves = (data[0] & 1) != 0,
            HasItems = (data[0] & 2) != 0,
            Class = data[1],
            Unknown = data[2],
            AiFlags = BitConverter.ToUInt32(data, 12),
            BattleType = BitConverter.ToUInt32(data, 16),
            OriginalData = data,
            OriginalParty = party,
        };
        for (int i = 0; i < 4; i++) t.Items[i] = BitConverter.ToUInt16(data, 4 + i * 2);

        int count = data[3];
        int size = t.PartyEntrySize(hasBallSeal);
        for (int n = 0; n < count && (n + 1) * size <= party.Length; n++)
        {
            int o = n * size;
            var m = new PartyMember
            {
                Difficulty = party[o],
                GenderAbility = (GenderAbilityFlags)party[o + 1],
                Level = BitConverter.ToUInt16(party, o + 2),
            };
            ushort full = BitConverter.ToUInt16(party, o + 4);
            m.Species = (ushort)(full & 0x3FF);
            m.Form = (ushort)(full >> 10);
            o += 6;
            if (t.HasItems) { m.Item = BitConverter.ToUInt16(party, o); o += 2; }
            if (t.HasMoves)
            {
                for (int k = 0; k < 4; k++) { var v = BitConverter.ToUInt16(party, o); m.Moves[k] = v == 0xFFFF ? (ushort)0 : v; o += 2; }
            }
            if (hasBallSeal) m.BallSeal = BitConverter.ToUInt16(party, o);
            t.Party.Add(m);
        }
        return t;
    }

    public byte[] SerializeData()
    {
        var d = new byte[20];
        d[0] = (byte)((HasMoves ? 1 : 0) | (HasItems ? 2 : 0));
        d[1] = Class;
        d[2] = Unknown;
        d[3] = (byte)Math.Min(Party.Count, MaxParty);
        for (int i = 0; i < 4; i++) BitConverter.GetBytes(Items[i]).CopyTo(d, 4 + i * 2);
        BitConverter.GetBytes(AiFlags).CopyTo(d, 12);
        BitConverter.GetBytes(BattleType).CopyTo(d, 16);
        return d;
    }

    /// <summary>Bytes a gravar: os originais se o treinador está igual ao lido (preserva preenchimento), senão os serializados.</summary>
    public (byte[] Data, byte[] Party) BytesToWrite(bool hasBallSeal)
    {
        var data = SerializeData();
        var party = SerializeParty(hasBallSeal);
        if (OriginalData is { } od && OriginalParty is { } op
            && od.AsSpan().SequenceEqual(data) && op.Length >= party.Length && op.AsSpan(0, party.Length).SequenceEqual(party))
            return (od, op);
        return (data, party);
    }

    public byte[] SerializeParty(bool hasBallSeal)
    {
        using var ms = new MemoryStream();
        foreach (var m in Party.Take(MaxParty))
        {
            ms.WriteByte(m.Difficulty);
            ms.WriteByte((byte)m.GenderAbility);
            ms.Write(BitConverter.GetBytes(m.Level));
            ms.Write(BitConverter.GetBytes((ushort)((m.Species & 0x3FF) | (m.Form << 10))));
            if (HasItems) ms.Write(BitConverter.GetBytes(m.Item));
            if (HasMoves) foreach (var mv in m.Moves) ms.Write(BitConverter.GetBytes(mv));
            if (hasBallSeal) ms.Write(BitConverter.GetBytes(m.BallSeal));
        }
        // convenção do jogo: time vazio ocupa 8 bytes e todo arquivo é preenchido com zeros até múltiplo de 4
        if (ms.Length == 0) ms.Write(new byte[8]);
        while (ms.Length % 4 != 0) ms.WriteByte(0);
        return ms.ToArray();
    }
}
