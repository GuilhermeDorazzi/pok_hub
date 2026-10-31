using System.Text;

namespace PokeHub.Core.Gen4;

/// <summary>Uma mensagem de um banco de texto de Gen 4: códigos u16 já decifrados e descomprimidos.</summary>
public sealed class Gen4Message
{
    /// <summary>Códigos sem o terminador. Para nomes de treinador comprimidos, já expandidos em 9 bits.</summary>
    public List<ushort> Codes { get; set; } = [];
    /// <summary>true = guardado comprimido (marcador 0xF100, 9 bits por caractere), como os nomes de treinador.</summary>
    public bool IsCompressed { get; set; }
    /// <summary>Palavras originais decifradas (com terminador). Reutilizadas na gravação enquanto a mensagem não for alterada,
    /// porque o encoder da Nintendo deixa bits de preenchimento diferentes dos nossos.</summary>
    public ushort[]? Raw { get; set; }

    public void SetCodes(List<ushort> codes) { Codes = codes; Raw = null; }
}

/// <summary>
/// Banco de texto de Gen 4 (um arquivo do msg.narc): cabeçalho (quantidade, chave), tabela de offsets cifrada e
/// strings cifradas. Algoritmo conforme o msgenc do pokeplatinum.
/// </summary>
public sealed class Gen4TextArchive
{
    public ushort Key { get; set; }
    public List<Gen4Message> Messages { get; } = [];

    public static Gen4TextArchive Read(byte[] data)
    {
        var archive = new Gen4TextArchive();
        int count = BitConverter.ToUInt16(data, 0);
        archive.Key = BitConverter.ToUInt16(data, 2);
        for (int i = 0; i < count; i++)
        {
            ushort entryKey = (ushort)(((archive.Key * 0x2FD) & 0xFFFF) * (i + 1));
            uint key32 = (uint)(entryKey | (entryKey << 16));
            uint offset = BitConverter.ToUInt32(data, 4 + i * 8) ^ key32;
            uint length = BitConverter.ToUInt32(data, 8 + i * 8) ^ key32;
            if (offset + length * 2 > data.Length) throw new InvalidDataException($"Mensagem {i} fora do arquivo.");

            var codes = new List<ushort>((int)length);
            ushort k = (ushort)((0x91BD3 * (i + 1)) & 0xFFFF);
            for (uint j = 0; j < length; j++)
            {
                codes.Add((ushort)(BitConverter.ToUInt16(data, (int)(offset + j * 2)) ^ k));
                k = (ushort)(k + 0x493D);
            }

            var msg = new Gen4Message { Raw = codes.ToArray() };
            if (codes.Count > 0 && codes[0] == CharMap.CompressedMarker)
            {
                msg.IsCompressed = true;
                msg.Codes = Decompress(codes);
            }
            else
            {
                int end = codes.IndexOf(CharMap.Terminator);
                msg.Codes = end >= 0 ? codes[..end] : codes;
            }
            archive.Messages.Add(msg);
        }
        return archive;
    }

    public byte[] Write()
    {
        var encoded = new List<ushort[]>();
        foreach (var m in Messages)
        {
            if (m.Raw is { } raw) { encoded.Add(raw); continue; }
            var codes = m.IsCompressed ? Compress(m.Codes) : new List<ushort>(m.Codes);
            codes.Add(CharMap.Terminator);
            encoded.Add(codes.ToArray());
        }

        using var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((ushort)Messages.Count));
        ms.Write(BitConverter.GetBytes(Key));
        uint offset = (uint)(4 + Messages.Count * 8);
        for (int i = 0; i < encoded.Count; i++)
        {
            ushort entryKey = (ushort)(((Key * 0x2FD) & 0xFFFF) * (i + 1));
            uint key32 = (uint)(entryKey | (entryKey << 16));
            ms.Write(BitConverter.GetBytes(offset ^ key32));
            ms.Write(BitConverter.GetBytes((uint)encoded[i].Length ^ key32));
            offset += (uint)encoded[i].Length * 2;
        }
        for (int i = 0; i < encoded.Count; i++)
        {
            ushort k = (ushort)((0x91BD3 * (i + 1)) & 0xFFFF);
            foreach (var c in encoded[i])
            {
                ms.Write(BitConverter.GetBytes((ushort)(c ^ k)));
                k = (ushort)(k + 0x493D);
            }
        }
        return ms.ToArray();
    }

    /// <summary>Expande palavras de 15 bits em caracteres de 9 bits (após o marcador 0xF100), até 0x1FF.</summary>
    private static List<ushort> Decompress(List<ushort> codes)
    {
        var result = new List<ushort>();
        int bit = 0, p = 1;
        while (p < codes.Count)
        {
            ushort cur = (ushort)((codes[p] >> bit) & 0x1FF);
            bit += 9;
            if (bit >= 15)
            {
                p++;
                bit -= 15;
                if (bit != 0 && p < codes.Count) cur |= (ushort)((codes[p] << (9 - bit)) & 0x1FF);
            }
            if (cur == 0x1FF) break;
            result.Add(cur);
        }
        return result;
    }

    /// <summary>Empacota caracteres de 9 bits em palavras de 15 bits precedidas de 0xF100 (espelho do msgenc).</summary>
    private static List<ushort> Compress(List<ushort> codes)
    {
        var result = new List<ushort> { CharMap.CompressedMarker };
        uint buf = 0; int bit = 0;
        foreach (var c in codes)
        {
            if ((c & ~0x1FF) != 0) throw new InvalidDataException($"Caractere 0x{c:X4} não cabe em 9 bits (nome de treinador).");
            buf |= (uint)c << bit;
            bit += 9;
            if (bit >= 15) { bit -= 15; result.Add((ushort)(buf & 0x7FFF)); buf >>= 15; }
        }
        if (bit > 1)
        {
            buf |= 0xFFFFu << bit;
            result.Add((ushort)(buf & 0x7FFF));
        }
        return result;
    }

    // ----- texto -----

    /// <summary>Decodifica para texto legível; comandos viram {NOME ...} e códigos desconhecidos viram \xHHHH.</summary>
    public static string Decode(Gen4Message m, CharMap? map = null)
    {
        map ??= CharMap.Default;
        var sb = new StringBuilder();
        var codes = m.Codes;
        for (int j = 0; j < codes.Count; j++)
        {
            ushort c = codes[j];
            if (c == CharMap.CommandMarker && !m.IsCompressed && j + 2 < codes.Count + 1)
            {
                ushort cmd = codes[++j];
                int nargs = j + 1 < codes.Count ? codes[++j] : 0;
                var name = map.CommandOf(cmd) ?? $"CMD_{cmd:X4}";
                sb.Append('{').Append(name);
                for (int a = 0; a < nargs && j + 1 < codes.Count; a++) sb.Append(a == 0 ? ' ' : ',').Append(codes[++j]);
                sb.Append('}');
            }
            else sb.Append(map.CharOf(c) ?? $"\\x{c:X4}");
        }
        return sb.ToString();
    }

    /// <summary>Codifica texto legível de volta para códigos (casamento mais longo primeiro).</summary>
    public static List<ushort> Encode(string text, CharMap? map = null)
    {
        map ??= CharMap.Default;
        var codes = new List<ushort>();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\\' && i + 5 < text.Length + 1 && i + 1 < text.Length && text[i + 1] == 'x' && i + 6 <= text.Length)
            {
                codes.Add(Convert.ToUInt16(text.Substring(i + 2, 4), 16));
                i += 6;
                continue;
            }
            if (text[i] == '{')
            {
                int end = text.IndexOf('}', i);
                if (end < 0) throw new InvalidDataException("Comando sem '}' no texto.");
                var inner = text[(i + 1)..end];
                var parts = inner.Split(' ', 2);
                if (!map.TryCommandCode(parts[0], out var cmd))
                {
                    if (parts[0].StartsWith("CMD_")) cmd = Convert.ToUInt16(parts[0][4..], 16);
                    else throw new InvalidDataException($"Comando desconhecido: {parts[0]}");
                }
                var args = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => ushort.Parse(a.Trim())).ToList() : [];
                codes.Add(CharMap.CommandMarker);
                codes.Add(cmd);
                codes.Add((ushort)args.Count);
                codes.AddRange(args);
                i = end + 1;
                continue;
            }
            bool matched = false;
            for (int len = Math.Min(map.MaxTextLength, text.Length - i); len >= 1; len--)
            {
                if (map.TryCode(text.Substring(i, len), out var code)) { codes.Add(code); i += len; matched = true; break; }
            }
            if (!matched) throw new InvalidDataException($"Caractere sem código no charmap: '{text[i]}'");
        }
        return codes;
    }
}
