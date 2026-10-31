using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>Um arquivo de encontros e os mapas que o usam.</summary>
public sealed class EncounterEntry
{
    public required int Id { get; init; }
    public HgssEncounters? Hgss { get; set; }
    public DpptEncounters? Dppt { get; set; }
    public List<MapHeaderInfo> Maps { get; } = [];
    public byte[] Original { get; set; } = [];
}

/// <summary>Carrega e grava o NARC de encontros selvagens de um projeto Gen 4, com nomes de espécies e locais do jogo.</summary>
public sealed class EncounterRepository
{
    private readonly Narc _narc;

    public Gen4Game Game { get; }
    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> LocationNames { get; }
    public List<EncounterEntry> Entries { get; } = [];
    public IReadOnlyList<MapHeaderInfo> Headers { get; }

    private EncounterRepository(Gen4Game game, Narc narc, IReadOnlyList<string> species, IReadOnlyList<string> locations, IReadOnlyList<MapHeaderInfo> headers)
    {
        Game = game;
        _narc = narc;
        Species = species;
        LocationNames = locations;
        Headers = headers;
        for (int i = 0; i < narc.Files.Count; i++)
        {
            var data = narc.Files[i];
            var entry = new EncounterEntry { Id = i, Original = data.ToArray() };
            if (game.IsHgss) entry.Hgss = HgssEncounters.Parse(data);
            else entry.Dppt = DpptEncounters.Parse(data);
            entry.Maps.AddRange(headers.Where(h => h.EncounterFile == i));
            Entries.Add(entry);
        }
    }

    public static EncounterRepository Load(Project project)
    {
        var game = Gen4Game.For(project);
        if (!File.Exists(game.EncountersPath)) throw new FileNotFoundException(Loc.T("core.enc.narcMissing"), game.EncountersPath);
        var narc = Narc.Read(File.ReadAllBytes(game.EncountersPath));
        var text = Narc.Read(File.ReadAllBytes(game.TextArchivePath));
        List<string> Bank(int b) => b < text.Files.Count ? Gen4TextArchive.Read(text.Files[b]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList() : [];
        return new EncounterRepository(game, narc, Bank(game.SpeciesBank), Bank(game.LocationNamesBank), MapHeaders.Load(game));
    }

    public string LocationName(int index) => index >= 0 && index < LocationNames.Count ? LocationNames[index] : $"#{index}";

    /// <summary>Rótulo da entrada: id e os mapas que a usam (nome interno e nome do local).</summary>
    public string Label(EncounterEntry e)
    {
        if (e.Maps.Count == 0) return $"{e.Id:D3}";
        var locs = e.Maps.Select(m => LocationName(m.LocationNameIndex)).Distinct().Take(3);
        var internals = e.Maps.Select(m => m.InternalName).Take(3);
        return $"{e.Id:D3}  {string.Join(", ", locs)}  [{string.Join(", ", internals)}{(e.Maps.Count > 3 ? ", …" : "")}]";
    }

    public bool HasChanges => Entries.Any(e => !Bytes(e).AsSpan().SequenceEqual(e.Original));

    private static byte[] Bytes(EncounterEntry e) => e.Hgss?.Serialize() ?? e.Dppt!.Serialize();

    public void Save()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            var bytes = Bytes(e);
            // arquivo original tem o mesmo tamanho; preserva bytes se nada mudou (inclusive lixo após o fim conhecido)
            if (!bytes.AsSpan().SequenceEqual(e.Original.AsSpan(0, Math.Min(bytes.Length, e.Original.Length))))
            {
                var merged = e.Original.Length > bytes.Length ? (byte[])e.Original.Clone() : bytes;
                if (merged != bytes) bytes.CopyTo(merged, 0);
                _narc.Files[i] = merged;
                e.Original = merged.ToArray();
            }
            else _narc.Files[i] = e.Original;
        }
        File.WriteAllBytes(Game.EncountersPath, _narc.Write());
    }
}
