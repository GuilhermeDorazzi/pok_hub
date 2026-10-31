using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.Core.Batch;

/// <summary>Regras de lote sobre treinadores: filtro por classe, escala e deslocamento de nível, item segurado.</summary>
public sealed class TrainerBatchOptions
{
    /// <summary>Classe (índice) a afetar; null = todas.</summary>
    public int? ClassFilter { get; set; }
    /// <summary>Texto no nome do treinador (sem distinção de maiúsculas); vazio = todos.</summary>
    public string NameFilter { get; set; } = "";
    public double LevelPercent { get; set; } = 100;
    public int LevelOffset { get; set; }
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 100;
    /// <summary>Item segurado para todos os Pokémon do time; null = não mexer.</summary>
    public ushort? HeldItem { get; set; }
    /// <summary>Trocar espécie A por B nos times; null = não mexer.</summary>
    public ushort? ReplaceSpeciesFrom { get; set; }
    public ushort? ReplaceSpeciesTo { get; set; }

    public bool ChangesLevel => Math.Abs(LevelPercent - 100) > 0.001 || LevelOffset != 0;
}

/// <summary>Regras de lote sobre encontros: troca de espécie em todas as tabelas e escala de nível.</summary>
public sealed class EncounterBatchOptions
{
    public ushort? ReplaceSpeciesFrom { get; set; }
    public ushort? ReplaceSpeciesTo { get; set; }
    public double LevelPercent { get; set; } = 100;
    public int LevelOffset { get; set; }
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 100;

    public bool ChangesLevel => Math.Abs(LevelPercent - 100) > 0.001 || LevelOffset != 0;
}

/// <summary>
/// Operações em lote. Preview devolve as mudanças em texto sem alterar nada; Apply faz as mesmas mudanças.
/// A mesma função gera os dois para a prévia nunca divergir do que é aplicado.
/// </summary>
public static class BatchEditor
{
    public static List<string> PreviewTrainers(TrainerRepository repo, TrainerBatchOptions o) => RunTrainers(repo, o, apply: false);
    public static List<string> ApplyTrainers(TrainerRepository repo, TrainerBatchOptions o) => RunTrainers(repo, o, apply: true);
    public static List<string> PreviewEncounters(EncounterRepository repo, EncounterBatchOptions o) => RunEncounters(repo, o, apply: false);
    public static List<string> ApplyEncounters(EncounterRepository repo, EncounterBatchOptions o) => RunEncounters(repo, o, apply: true);

    public static int ScaleLevel(int level, double percent, int offset, int min, int max)
    {
        var v = (int)Math.Round(level * percent / 100.0, MidpointRounding.AwayFromZero) + offset;
        return Math.Clamp(v, Math.Max(1, min), Math.Min(100, max));
    }

    private static List<string> RunTrainers(TrainerRepository repo, TrainerBatchOptions o, bool apply)
    {
        var lines = new List<string>();
        string Sp(IReadOnlyList<string> t, int i) => i >= 0 && i < t.Count ? t[i] : $"#{i}";
        foreach (var t in repo.Trainers)
        {
            if (o.ClassFilter is { } c && t.Class != c) continue;
            if (o.NameFilter.Length > 0 && !repo.NameOf(t).Contains(o.NameFilter, StringComparison.OrdinalIgnoreCase)) continue;
            var who = $"{repo.ClassOf(t)} {repo.NameOf(t)} (#{t.Id})";
            for (int k = 0; k < t.Party.Count; k++)
            {
                var m = t.Party[k];
                if (o.ChangesLevel)
                {
                    var nl = ScaleLevel(m.Level, o.LevelPercent, o.LevelOffset, o.MinLevel, o.MaxLevel);
                    if (nl != m.Level)
                    {
                        lines.Add(Loc.T("batch.tr.level", who, Sp(repo.Species, m.Species), m.Level, nl));
                        if (apply) m.Level = (ushort)nl;
                    }
                }
                if (o.ReplaceSpeciesFrom is { } from && o.ReplaceSpeciesTo is { } to && m.Species == from && from != to)
                {
                    lines.Add(Loc.T("batch.tr.species", who, Sp(repo.Species, from), Sp(repo.Species, to)));
                    if (apply) m.Species = to;
                }
                if (o.HeldItem is { } item && m.Item != item)
                {
                    if (!t.HasItems)
                    {
                        lines.Add(Loc.T("batch.tr.noItems", who));
                        break;
                    }
                    lines.Add(Loc.T("batch.tr.item", who, Sp(repo.Species, m.Species), Sp(repo.Items, m.Item), Sp(repo.Items, item)));
                    if (apply) m.Item = item;
                }
            }
        }
        return lines;
    }

    private static List<string> RunEncounters(EncounterRepository repo, EncounterBatchOptions o, bool apply)
    {
        var lines = new List<string>();
        string Sp(int i) => i >= 0 && i < repo.Species.Count ? repo.Species[i] : $"#{i}";
        bool replace = o.ReplaceSpeciesFrom is { } && o.ReplaceSpeciesTo is { } && o.ReplaceSpeciesFrom != o.ReplaceSpeciesTo;

        void Species(ushort[] arr, string where, string what)
        {
            if (!replace) return;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == o.ReplaceSpeciesFrom)
                {
                    lines.Add(Loc.T("batch.enc.species", where, $"{what} {i + 1}", Sp(arr[i]), Sp(o.ReplaceSpeciesTo!.Value)));
                    if (apply) arr[i] = o.ReplaceSpeciesTo.Value;
                }
        }
        void Slots(EncounterSlot[] slots, string where, string what)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (replace && s.Species == o.ReplaceSpeciesFrom)
                {
                    lines.Add(Loc.T("batch.enc.species", where, $"{what} {i + 1}", Sp(s.Species), Sp(o.ReplaceSpeciesTo!.Value)));
                    if (apply) s.Species = o.ReplaceSpeciesTo.Value;
                }
                if (o.ChangesLevel && s.Species != 0)
                {
                    int lo = ScaleLevel(s.MinLevel, o.LevelPercent, o.LevelOffset, o.MinLevel, o.MaxLevel);
                    int hi = ScaleLevel(s.MaxLevel, o.LevelPercent, o.LevelOffset, o.MinLevel, o.MaxLevel);
                    if (lo != s.MinLevel || hi != s.MaxLevel)
                    {
                        lines.Add(Loc.T("batch.enc.level", where, $"{what} {i + 1}", $"{s.MinLevel}-{s.MaxLevel}", $"{lo}-{hi}"));
                        if (apply) { s.MinLevel = (byte)lo; s.MaxLevel = (byte)hi; }
                    }
                }
            }
        }

        foreach (var e in repo.Entries)
        {
            var where = repo.Label(e);
            if (e.Hgss is { } h)
            {
                Species(h.Morning, where, Loc.T("report.enc.kind.morning")); Species(h.Day, where, Loc.T("report.enc.kind.day")); Species(h.Night, where, Loc.T("report.enc.kind.night"));
                Species(h.HoennSound, where, Loc.T("report.enc.kind.hoenn")); Species(h.SinnohSound, where, Loc.T("report.enc.kind.sinnoh")); Species(h.Swarm, where, Loc.T("report.enc.kind.swarm"));
                if (o.ChangesLevel)
                    for (int i = 0; i < 12; i++)
                    {
                        if (h.Morning[i] == 0 && h.Day[i] == 0 && h.Night[i] == 0) continue;
                        int nl = ScaleLevel(h.WalkingLevels[i], o.LevelPercent, o.LevelOffset, o.MinLevel, o.MaxLevel);
                        if (nl != h.WalkingLevels[i])
                        {
                            lines.Add(Loc.T("batch.enc.level", where, $"{Loc.T("report.enc.kind.walking")} {i + 1}", h.WalkingLevels[i], nl));
                            if (apply) h.WalkingLevels[i] = (byte)nl;
                        }
                    }
                Slots(h.Surf, where, Loc.T("report.enc.kind.surf")); Slots(h.RockSmash, where, Loc.T("report.enc.kind.rockSmash"));
                Slots(h.OldRod, where, Loc.T("report.enc.kind.oldRod")); Slots(h.GoodRod, where, Loc.T("report.enc.kind.goodRod")); Slots(h.SuperRod, where, Loc.T("report.enc.kind.superRod"));
            }
            else if (e.Dppt is { } d)
            {
                Slots(d.Walking, where, Loc.T("report.enc.kind.walking"));
                Species(d.Swarm, where, Loc.T("report.enc.kind.swarm")); Species(d.Day, where, Loc.T("report.enc.kind.day")); Species(d.Night, where, Loc.T("report.enc.kind.night"));
                Species(d.Radar, where, Loc.T("report.enc.kind.radar"));
                Species(d.Ruby, where, "Ruby"); Species(d.Sapphire, where, "Sapphire"); Species(d.Emerald, where, "Emerald"); Species(d.FireRed, where, "FireRed"); Species(d.LeafGreen, where, "LeafGreen");
                Slots(d.Surf, where, Loc.T("report.enc.kind.surf")); Slots(d.OldRod, where, Loc.T("report.enc.kind.oldRod"));
                Slots(d.GoodRod, where, Loc.T("report.enc.kind.goodRod")); Slots(d.SuperRod, where, Loc.T("report.enc.kind.superRod"));
            }
        }
        return lines;
    }
}
