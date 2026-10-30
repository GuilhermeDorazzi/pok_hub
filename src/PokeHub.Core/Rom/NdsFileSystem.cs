using System.Text;

namespace PokeHub.Core.Rom;

/// <summary>Entrada de um diretório na FNT, na ordem original (arquivos e subpastas intercalados).</summary>
public sealed class FntEntry
{
    public required string Name { get; set; }
    public bool IsDirectory { get; set; }
    /// <summary>Preenchido só para diretórios.</summary>
    public List<FntEntry>? Children { get; set; }

    public FntEntry Clone() => new()
    {
        Name = Name,
        IsDirectory = IsDirectory,
        Children = Children?.Select(c => c.Clone()).ToList(),
    };

    public static bool TreeEquals(IReadOnlyList<FntEntry> a, IReadOnlyList<FntEntry> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var x = a[i]; var y = b[i];
            if (x.Name != y.Name || x.IsDirectory != y.IsDirectory) return false;
            if (x.IsDirectory && !TreeEquals(x.Children ?? [], y.Children ?? [])) return false;
        }
        return true;
    }
}

/// <summary>Arquivo resolvido: id na FAT e caminho relativo dentro de data/.</summary>
public sealed record NdsFileRef(int FileId, string Path);

/// <summary>
/// Leitura e escrita da File Name Table (FNT) do formato Nitro.
/// Tabela principal: 8 bytes por diretório (offset da subtabela, primeiro file id, pai ou total de diretórios no root).
/// Subtabela: [len|0x80 se dir][nome][dirId u16 se dir] ... 0.
/// </summary>
public static class NdsFileSystem
{
    public const ushort RootDirId = 0xF000;

    /// <summary>Lê a FNT e devolve a árvore ordenada e a lista de arquivos com seus ids.</summary>
    public static (List<FntEntry> Tree, List<NdsFileRef> Files, int FirstFileId) Parse(ReadOnlySpan<byte> fnt)
    {
        int dirCount = BitConverter.ToUInt16(fnt[6..8]);
        if (dirCount == 0 || dirCount * 8 > fnt.Length) throw new InvalidDataException("FNT inválida.");
        var files = new List<NdsFileRef>();
        var tree = ParseDir(fnt, RootDirId, "", files);
        int firstFileId = BitConverter.ToUInt16(fnt[4..6]);
        return (tree, files, firstFileId);
    }

    private static List<FntEntry> ParseDir(ReadOnlySpan<byte> fnt, ushort dirId, string prefix, List<NdsFileRef> files)
    {
        int main = (dirId & 0x0FFF) * 8;
        int sub = (int)BitConverter.ToUInt32(fnt[main..(main + 4)]);
        int fileId = BitConverter.ToUInt16(fnt[(main + 4)..(main + 6)]);
        var entries = new List<FntEntry>();
        int p = sub;
        while (true)
        {
            if (p >= fnt.Length) throw new InvalidDataException("FNT truncada.");
            byte len = fnt[p++];
            if (len == 0) break;
            bool isDir = (len & 0x80) != 0;
            int nameLen = len & 0x7F;
            var name = Encoding.ASCII.GetString(fnt[p..(p + nameLen)]);
            p += nameLen;
            if (isDir)
            {
                ushort childId = BitConverter.ToUInt16(fnt[p..(p + 2)]);
                p += 2;
                entries.Add(new FntEntry { Name = name, IsDirectory = true, Children = ParseDir(fnt, childId, prefix + name + "/", files) });
            }
            else
            {
                entries.Add(new FntEntry { Name = name, IsDirectory = false });
                files.Add(new NdsFileRef(fileId++, prefix + name));
            }
        }
        return entries;
    }

    /// <summary>
    /// Constrói uma FNT a partir da árvore. Ids de diretório: irmãos em sequência, depois os filhos (como o makerom da Nintendo e o ndstool).
    /// Ids de arquivo: pré-ordem, arquivos da pasta antes dos das subpastas, a partir de <paramref name="firstFileId"/>.
    /// </summary>
    public static byte[] Build(IReadOnlyList<FntEntry> tree, int firstFileId)
    {
        // 1) atribui ids de diretório (largura: irmãos antes dos filhos)
        var dirs = new List<(IReadOnlyList<FntEntry> Entries, ushort Id, ushort ParentId)>();
        var queue = new Queue<(IReadOnlyList<FntEntry>, ushort)>();
        var idOf = new Dictionary<FntEntry, ushort>(ReferenceEqualityComparer.Instance);
        ushort next = RootDirId;
        dirs.Add((tree, next, 0)); // pai do root é substituído pelo total de diretórios
        queue.Enqueue((tree, next));
        next++;
        while (queue.Count > 0)
        {
            var (entries, parentId) = queue.Dequeue();
            foreach (var e in entries.Where(e => e.IsDirectory))
            {
                idOf[e] = next;
                dirs.Add((e.Children ?? [], next, parentId));
                queue.Enqueue((e.Children ?? [], next));
                next++;
            }
        }
        if (dirs.Count > 0x1000) throw new InvalidDataException("Mais de 4096 diretórios.");

        // 2) atribui primeiro file id por diretório (pré-ordem)
        var firstOf = new Dictionary<ushort, int>();
        int counter = firstFileId;
        void Assign(IReadOnlyList<FntEntry> entries, ushort id)
        {
            firstOf[id] = counter;
            counter += entries.Count(e => !e.IsDirectory);
            foreach (var e in entries.Where(e => e.IsDirectory)) Assign(e.Children ?? [], idOf[e]);
        }
        Assign(tree, RootDirId);

        // 3) escreve subtabelas em ordem de id
        var subtables = new List<byte[]>();
        foreach (var (entries, _, _) in dirs)
        {
            using var ms = new MemoryStream();
            foreach (var e in entries)
            {
                var name = Encoding.ASCII.GetBytes(e.Name);
                if (name.Length is 0 or > 0x7F) throw new InvalidDataException($"Nome inválido na FNT: '{e.Name}'");
                ms.WriteByte((byte)(name.Length | (e.IsDirectory ? 0x80 : 0)));
                ms.Write(name);
                if (e.IsDirectory) ms.Write(BitConverter.GetBytes(idOf[e]));
            }
            ms.WriteByte(0);
            subtables.Add(ms.ToArray());
        }

        // 4) tabela principal + subtabelas
        using var fnt = new MemoryStream();
        int subOffset = dirs.Count * 8;
        for (int i = 0; i < dirs.Count; i++)
        {
            var (_, id, parent) = dirs[i];
            fnt.Write(BitConverter.GetBytes((uint)subOffset));
            fnt.Write(BitConverter.GetBytes((ushort)firstOf[id]));
            fnt.Write(BitConverter.GetBytes(i == 0 ? (ushort)dirs.Count : parent));
            subOffset += subtables[i].Length;
        }
        foreach (var s in subtables) fnt.Write(s);
        return fnt.ToArray();
    }

    /// <summary>Monta a árvore a partir de uma pasta no disco, preservando a ordem de <paramref name="reference"/> e acrescentando novidades em ordem ordinal.</summary>
    public static List<FntEntry> FromDirectory(string dir, IReadOnlyList<FntEntry>? reference)
    {
        var result = new List<FntEntry>();
        var onDisk = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToDictionary(f => f.Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (reference is not null)
        {
            foreach (var r in reference)
            {
                if (!onDisk.TryGetValue(r.Name, out var fsi)) continue; // apagado
                bool isDir = fsi is DirectoryInfo;
                if (isDir != r.IsDirectory) continue; // mudou de tipo: tratado como novo abaixo
                seen.Add(r.Name);
                result.Add(isDir
                    ? new FntEntry { Name = r.Name, IsDirectory = true, Children = FromDirectory(fsi.FullName, r.Children) }
                    : new FntEntry { Name = r.Name, IsDirectory = false });
            }
        }
        foreach (var fsi in onDisk.Values.Where(f => !seen.Contains(f.Name)).OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            result.Add(fsi is DirectoryInfo d
                ? new FntEntry { Name = d.Name, IsDirectory = true, Children = FromDirectory(d.FullName, null) }
                : new FntEntry { Name = fsi.Name, IsDirectory = false });
        }
        return result;
    }
}
