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
    /// <summary>Opções de IA a ligar e a desligar, em bits. O que não estiver em nenhuma das duas fica como está.</summary>
    public uint AiFlagsOn { get; set; }
    public uint AiFlagsOff { get; set; }
    /// <summary>Os 4 itens que o treinador usa em batalha; null = não mexer. Zero deixa o espaço vazio.</summary>
    public ushort[]? BattleItems { get; set; }
    /// <summary>Dificuldade de cada Pokémon do time, de 0 a 255, que define os IVs (cerca de valor × 31 / 255); null = não mexer.</summary>
    public byte? Difficulty { get; set; }
    /// <summary>Com a tabela, cada Pokémon vira a espécie que já seria no nível final; null = não mexer.</summary>
    public EvolutionChart? Evolutions { get; set; }
    /// <summary>Nível em que entram as evoluções sem nível (pedra, amizade, troca). Zero deixa essas de fora.</summary>
    public int EvolveOtherAt { get; set; }

    public bool ChangesLevel => Math.Abs(LevelPercent - 100) > 0.001 || LevelOffset != 0;
}

/// <summary>Região do jogo que o lote de encontros alcança. No Platinum tudo conta como a primeira região.</summary>
public enum EncounterRegion { All, First, Second }

/// <summary>Regras de lote sobre encontros: filtro por área, troca de espécie e escala de nível.</summary>
public sealed class EncounterBatchOptions
{
    public EncounterRegion Region { get; set; }
    /// <summary>Lugares separados por vírgula, pelo nome do local ou nome interno do mapa; vazio = todos.</summary>
    public string PlaceFilter { get; set; } = "";
    /// <summary>Com a tabela, cada encontro com nível vira a espécie que já seria nesse nível; null = não mexer.</summary>
    public EvolutionChart? Evolutions { get; set; }
    /// <summary>Nível em que entram as evoluções sem nível (pedra, amizade, troca). Zero deixa essas de fora.</summary>
    public int EvolveOtherAt { get; set; }
    public ushort? ReplaceSpeciesFrom { get; set; }
    public ushort? ReplaceSpeciesTo { get; set; }
    public double LevelPercent { get; set; } = 100;
    public int LevelOffset { get; set; }
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 100;
    /// <summary>Faixa por raridade: o espaço mais comum fica no piso, o mais raro no teto. Com ela, porcentagem e soma não valem.</summary>
    public int? RarityFloor { get; set; }
    public int? RarityCeiling { get; set; }

    public bool HasRarityRange => RarityFloor is not null && RarityCeiling is not null;
    public bool ChangesLevel => HasRarityRange || Math.Abs(LevelPercent - 100) > 0.001 || LevelOffset != 0;

    /// <summary>Nível final de um espaço. Os espaços vêm do jogo em ordem de chance, do mais comum para o mais raro.</summary>
    public int LevelOf(int current, int slot, int slots)
    {
        if (HasRarityRange)
            return (int)Math.Round(RarityFloor!.Value + (RarityCeiling!.Value - RarityFloor.Value) * (double)slot / Math.Max(1, slots - 1), MidpointRounding.AwayFromZero);
        return ChangesLevel ? BatchEditor.ScaleLevel(current, LevelPercent, LevelOffset, MinLevel, MaxLevel) : current;
    }
}

/// <summary>Regra de lote sobre as espécies: quanto de experiência cada uma dá ao ser vencida.</summary>
public sealed class SpeciesBatchOptions
{
    public double ExpPercent { get; set; } = 100;
    public const int MaxExp = 255;
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

    public static List<string> PreviewSpecies(PersonalRepository repo, SpeciesBatchOptions o) => RunSpecies(repo, o, apply: false);
    public static List<string> ApplySpecies(PersonalRepository repo, SpeciesBatchOptions o) => RunSpecies(repo, o, apply: true);

    private static List<string> RunSpecies(PersonalRepository repo, SpeciesBatchOptions o, bool apply)
    {
        var lines = new List<string>();
        for (int s = 1; s < repo.Count; s++)
        {
            if (repo.Get(s) is not { ExpYield: > 0 } d) continue;
            int exp = Math.Clamp((int)Math.Round(d.ExpYield * o.ExpPercent / 100.0, MidpointRounding.AwayFromZero), 1, SpeciesBatchOptions.MaxExp);
            if (exp == d.ExpYield) continue;
            lines.Add(Loc.T("batch.sp.exp", s < repo.Species.Count ? repo.Species[s] : $"#{s}", d.ExpYield, exp));
            if (apply) d.ExpYield = (byte)exp;
        }
        return lines;
    }

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
            uint ai = (t.AiFlags | o.AiFlagsOn) & ~o.AiFlagsOff;
            if (ai != t.AiFlags)
            {
                lines.Add(Loc.T("batch.tr.ai", who, $"0x{t.AiFlags:X}", $"0x{ai:X}"));
                if (apply) t.AiFlags = ai;
            }
            if (o.BattleItems is { } items && !items.SequenceEqual(t.Items))
            {
                lines.Add(Loc.T("batch.tr.battleItems", who, string.Join(", ", t.Items.Where(i => i > 0).Select(i => Sp(repo.Items, i))), string.Join(", ", items.Where(i => i > 0).Select(i => Sp(repo.Items, i)))));
                if (apply) items.CopyTo(t.Items, 0);
            }
            for (int k = 0; k < t.Party.Count; k++)
            {
                var m = t.Party[k];
                int startLevel = m.Level;
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
                if (o.Evolutions is { } chart)
                {
                    int level = o.ChangesLevel ? ScaleLevel(startLevel, o.LevelPercent, o.LevelOffset, o.MinLevel, o.MaxLevel) : startLevel;
                    int species = apply ? m.Species : m.Species == o.ReplaceSpeciesFrom && o.ReplaceSpeciesTo is { } shown ? shown : m.Species;
                    int evolved = chart.Evolve(species, level, o.EvolveOtherAt);
                    if (evolved != species)
                    {
                        lines.Add(Loc.T("batch.tr.evolve", who, Sp(repo.Species, species), Sp(repo.Species, evolved), level));
                        if (apply) m.Species = (ushort)evolved;
                    }
                }
                if (o.Difficulty is { } difficulty && m.Difficulty != difficulty)
                {
                    lines.Add(Loc.T("batch.tr.difficulty", who, Sp(repo.Species, m.Species), m.Difficulty, difficulty));
                    if (apply) m.Difficulty = difficulty;
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
                if (o.Evolutions is { } chart && s.Species != 0)
                {
                    int level = o.LevelOf(s.MaxLevel, i, slots.Length);
                    int species = !apply && replace && s.Species == o.ReplaceSpeciesFrom ? o.ReplaceSpeciesTo!.Value : s.Species;
                    int evolved = chart.Evolve(species, level, o.EvolveOtherAt);
                    if (evolved != species)
                    {
                        lines.Add(Loc.T("batch.enc.evolve", where, $"{what} {i + 1}", Sp(species), Sp(evolved), level));
                        if (apply) s.Species = (ushort)evolved;
                    }
                }
                if (o.ChangesLevel && s.Species != 0)
                {
                    int lo = o.HasRarityRange ? o.LevelOf(s.MinLevel, i, slots.Length) : o.LevelOf(s.MinLevel, 0, 1);
                    int hi = o.HasRarityRange ? lo : o.LevelOf(s.MaxLevel, 0, 1);
                    if (lo != s.MinLevel || hi != s.MaxLevel)
                    {
                        lines.Add(Loc.T("batch.enc.level", where, $"{what} {i + 1}", $"{s.MinLevel}-{s.MaxLevel}", $"{lo}-{hi}"));
                        if (apply) { s.MinLevel = (byte)lo; s.MaxLevel = (byte)hi; }
                    }
                }
            }
        }

        var places = o.PlaceFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool InArea(EncounterEntry e)
        {
            if (o.Region == EncounterRegion.All && places.Length == 0) return true;
            return e.Maps.Any(m =>
                (o.Region == EncounterRegion.All || m.Region == (o.Region == EncounterRegion.First ? 0 : 1))
                && (places.Length == 0 || places.Any(p => repo.LocationName(m.LocationNameIndex).Contains(p, StringComparison.OrdinalIgnoreCase) || m.InternalName.Contains(p, StringComparison.OrdinalIgnoreCase))));
        }

        foreach (var e in repo.Entries)
        {
            if (!InArea(e)) continue;
            var where = repo.Label(e);
            if (e.Hgss is { } h)
            {
                Species(h.Morning, where, Loc.T("report.enc.kind.morning")); Species(h.Day, where, Loc.T("report.enc.kind.day")); Species(h.Night, where, Loc.T("report.enc.kind.night"));
                Species(h.HoennSound, where, Loc.T("report.enc.kind.hoenn")); Species(h.SinnohSound, where, Loc.T("report.enc.kind.sinnoh")); Species(h.Swarm, where, Loc.T("report.enc.kind.swarm"));
                if (o.Evolutions is { } grass)
                    for (int i = 0; i < 12; i++)
                    {
                        int level = o.LevelOf(h.WalkingLevels[i], i, 12);
                        foreach (var (period, kind) in new[] { (h.Morning, "report.enc.kind.morning"), (h.Day, "report.enc.kind.day"), (h.Night, "report.enc.kind.night") })
                        {
                            if (period[i] == 0) continue;
                            int species = !apply && replace && period[i] == o.ReplaceSpeciesFrom ? o.ReplaceSpeciesTo!.Value : period[i];
                            int evolved = grass.Evolve(species, level, o.EvolveOtherAt);
                            if (evolved == species) continue;
                            lines.Add(Loc.T("batch.enc.evolve", where, $"{Loc.T(kind)} {i + 1}", Sp(species), Sp(evolved), level));
                            if (apply) period[i] = (ushort)evolved;
                        }
                    }
                if (o.ChangesLevel)
                    for (int i = 0; i < 12; i++)
                    {
                        if (h.Morning[i] == 0 && h.Day[i] == 0 && h.Night[i] == 0) continue;
                        int nl = o.LevelOf(h.WalkingLevels[i], i, 12);
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
