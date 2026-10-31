using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Reports;

public sealed record ChangeLine(string Category, string Text);

/// <summary>
/// "O que mudou": compara uma versão salva do workspace com o estado atual em termos de jogo
/// (treinadores, encontros, textos), em vez de bytes. A versão antiga é exportada do git para uma pasta
/// temporária com os mesmos arquivos que os repositórios leem, e carregada como um projeto comum.
/// </summary>
public static class ChangeReport
{
    /// <summary>Arquivos (relativos ao workspace) que os repositórios Gen 4 precisam.</summary>
    public static IReadOnlyList<string> NeededFiles(Project p)
    {
        var g = Gen4Game.For(p);
        var ws = p.Paths.Workspace;
        return new[] { g.TrainerDataPath, g.TrainerPartyPath, g.TextArchivePath, g.EncountersPath, g.MapNamesPath, g.Arm9Path }
            .Select(f => Path.GetRelativePath(ws, f)).ToList();
    }

    /// <summary>Exporta os arquivos necessários de uma versão para tempRoot/workspace e devolve um projeto apontando para lá.</summary>
    public static async Task<Project> ExportVersionAsync(WorkspaceGit git, Project p, string hash, string tempRoot)
    {
        var paths = new ProjectPaths(tempRoot);
        Directory.CreateDirectory(paths.Workspace);
        // marca o layout (ds-rom tem a pasta files/; ndstool tem data/)
        foreach (var rel in NeededFiles(p))
        {
            var bytes = await git.ReadFileAtAsync(p, hash, rel);
            if (bytes is null) continue;
            var dest = Path.Combine(paths.Workspace, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await File.WriteAllBytesAsync(dest, bytes);
        }
        return new Project { Paths = paths, Config = p.Config };
    }

    public static List<ChangeLine> Compare(Project old, Project current, IReadOnlyList<string> changedFiles)
    {
        var lines = new List<ChangeLine>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var g = Gen4Game.For(current);
        string Rel(string f) => Path.GetRelativePath(current.Paths.Workspace, f).Replace('\\', '/');

        if (Changed(changedFiles, Rel(g.TrainerDataPath)) || Changed(changedFiles, Rel(g.TrainerPartyPath)))
        {
            covered.Add(Rel(g.TrainerDataPath)); covered.Add(Rel(g.TrainerPartyPath));
            Try(lines, "trainers", () => CompareTrainers(TrainerRepository.Load(old), TrainerRepository.Load(current), lines));
        }
        if (Changed(changedFiles, Rel(g.EncountersPath)))
        {
            covered.Add(Rel(g.EncountersPath));
            Try(lines, "encounters", () => CompareEncounters(EncounterRepository.Load(old), EncounterRepository.Load(current), lines));
        }
        if (Changed(changedFiles, Rel(g.TextArchivePath)))
        {
            covered.Add(Rel(g.TextArchivePath));
            Try(lines, "texts", () => CompareTexts(TextRepository.Load(old), TextRepository.Load(current), lines));
        }
        foreach (var f in changedFiles.Where(f => !covered.Contains(f.Replace('\\', '/'))))
            lines.Add(new("files", f));
        return lines;
    }

    private static bool Changed(IReadOnlyList<string> files, string rel) => files.Any(f => f.Replace('\\', '/').Equals(rel, StringComparison.OrdinalIgnoreCase));

    private static void Try(List<ChangeLine> lines, string category, Action compare)
    {
        try { compare(); }
        catch (Exception ex) { lines.Add(new(category, Loc.T("report.error", ex.Message))); }
    }

    private static void CompareTrainers(TrainerRepository a, TrainerRepository b, List<ChangeLine> lines)
    {
        string Sp(IReadOnlyList<string> t, int i) => i >= 0 && i < t.Count ? t[i] : $"#{i}";
        for (int i = 0; i < Math.Min(a.Trainers.Count, b.Trainers.Count); i++)
        {
            var x = a.Trainers[i]; var y = b.Trainers[i];
            var (xd, xp) = x.BytesToWrite(a.Game.HasBallSeal);
            var (yd, yp) = y.BytesToWrite(b.Game.HasBallSeal);
            var nameChanged = a.NameOf(x) != b.NameOf(y);
            if (!nameChanged && xd.AsSpan().SequenceEqual(yd) && xp.AsSpan().SequenceEqual(yp)) continue;
            var who = $"{b.ClassOf(y)} {b.NameOf(y)} (#{i})";
            if (nameChanged) lines.Add(new("trainers", Loc.T("report.tr.name", who, a.NameOf(x), b.NameOf(y))));
            if (x.Class != y.Class) lines.Add(new("trainers", Loc.T("report.tr.class", who, a.ClassOf(x), b.ClassOf(y))));
            if (x.DoubleBattle != y.DoubleBattle) lines.Add(new("trainers", Loc.T("report.tr.double", who, y.DoubleBattle)));
            if (x.AiFlags != y.AiFlags) lines.Add(new("trainers", Loc.T("report.tr.ai", who, x.AiFlags, y.AiFlags)));
            for (int k = 0; k < 4; k++)
                if (x.Items[k] != y.Items[k]) lines.Add(new("trainers", Loc.T("report.tr.item", who, k + 1, Sp(a.Items, x.Items[k]), Sp(b.Items, y.Items[k]))));
            int n = Math.Max(x.Party.Count, y.Party.Count);
            for (int k = 0; k < n; k++)
            {
                if (k >= x.Party.Count) { var m = y.Party[k]; lines.Add(new("trainers", Loc.T("report.tr.added", who, Sp(b.Species, m.Species), m.Level))); continue; }
                if (k >= y.Party.Count) { var m = x.Party[k]; lines.Add(new("trainers", Loc.T("report.tr.removed", who, Sp(a.Species, m.Species), m.Level))); continue; }
                var p = x.Party[k]; var q = y.Party[k];
                var slot = $"{who}, {Loc.T("report.tr.slot", k + 1)}";
                if (p.Species != q.Species || p.Level != q.Level)
                    lines.Add(new("trainers", Loc.T("report.tr.mon", slot, $"{Sp(a.Species, p.Species)} Lv{p.Level}", $"{Sp(b.Species, q.Species)} Lv{q.Level}")));
                if (p.Item != q.Item) lines.Add(new("trainers", Loc.T("report.tr.held", slot, Sp(a.Items, p.Item), Sp(b.Items, q.Item))));
                if (y.HasMoves && !p.Moves.AsSpan().SequenceEqual(q.Moves))
                    lines.Add(new("trainers", Loc.T("report.tr.moves", slot, string.Join("/", p.Moves.Select(m => Sp(a.Moves, m))), string.Join("/", q.Moves.Select(m => Sp(b.Moves, m))))));
                if (p.Difficulty != q.Difficulty || p.Form != q.Form || p.GenderAbility != q.GenderAbility || p.BallSeal != q.BallSeal)
                    lines.Add(new("trainers", Loc.T("report.tr.other", slot)));
            }
        }
        if (a.Trainers.Count != b.Trainers.Count) lines.Add(new("trainers", Loc.T("report.tr.count", a.Trainers.Count, b.Trainers.Count)));
    }

    private static void CompareEncounters(EncounterRepository a, EncounterRepository b, List<ChangeLine> lines)
    {
        string Sp(IReadOnlyList<string> t, int i) => i >= 0 && i < t.Count ? t[i] : $"#{i}";
        for (int i = 0; i < Math.Min(a.Entries.Count, b.Entries.Count); i++)
        {
            var x = a.Entries[i]; var y = b.Entries[i];
            if (x.Original.AsSpan().SequenceEqual(y.Original)) continue;
            var where = b.Label(y);
            if (x.Hgss is { } h1 && y.Hgss is { } h2)
            {
                Rate(lines, where, "walking", h1.WalkingRate, h2.WalkingRate); Rate(lines, where, "surf", h1.SurfRate, h2.SurfRate);
                Rate(lines, where, "rockSmash", h1.RockSmashRate, h2.RockSmashRate); Rate(lines, where, "oldRod", h1.OldRodRate, h2.OldRodRate);
                Rate(lines, where, "goodRod", h1.GoodRodRate, h2.GoodRodRate); Rate(lines, where, "superRod", h1.SuperRodRate, h2.SuperRodRate);
                for (int k = 0; k < 12; k++)
                {
                    if (h1.WalkingLevels[k] != h2.WalkingLevels[k]) lines.Add(new("encounters", Loc.T("report.enc.level", where, k + 1, h1.WalkingLevels[k], h2.WalkingLevels[k])));
                    Species(lines, where, Loc.T("report.enc.morning", k + 1), Sp(a.Species, h1.Morning[k]), Sp(b.Species, h2.Morning[k]));
                    Species(lines, where, Loc.T("report.enc.day", k + 1), Sp(a.Species, h1.Day[k]), Sp(b.Species, h2.Day[k]));
                    Species(lines, where, Loc.T("report.enc.night", k + 1), Sp(a.Species, h1.Night[k]), Sp(b.Species, h2.Night[k]));
                }
                Slots(lines, where, "surf", h1.Surf, h2.Surf, a.Species, b.Species); Slots(lines, where, "rockSmash", h1.RockSmash, h2.RockSmash, a.Species, b.Species);
                Slots(lines, where, "oldRod", h1.OldRod, h2.OldRod, a.Species, b.Species); Slots(lines, where, "goodRod", h1.GoodRod, h2.GoodRod, a.Species, b.Species);
                Slots(lines, where, "superRod", h1.SuperRod, h2.SuperRod, a.Species, b.Species);
                for (int k = 0; k < 2; k++) { Species(lines, where, Loc.T("report.enc.hoenn", k + 1), Sp(a.Species, h1.HoennSound[k]), Sp(b.Species, h2.HoennSound[k])); Species(lines, where, Loc.T("report.enc.sinnoh", k + 1), Sp(a.Species, h1.SinnohSound[k]), Sp(b.Species, h2.SinnohSound[k])); }
                for (int k = 0; k < 4; k++) Species(lines, where, Loc.T("report.enc.swarm", k + 1), Sp(a.Species, h1.Swarm[k]), Sp(b.Species, h2.Swarm[k]));
            }
            else if (x.Dppt is { } d1 && y.Dppt is { } d2)
            {
                Rate(lines, where, "walking", d1.WalkingRate, d2.WalkingRate); Rate(lines, where, "surf", d1.SurfRate, d2.SurfRate);
                Rate(lines, where, "oldRod", d1.OldRodRate, d2.OldRodRate); Rate(lines, where, "goodRod", d1.GoodRodRate, d2.GoodRodRate); Rate(lines, where, "superRod", d1.SuperRodRate, d2.SuperRodRate);
                Slots(lines, where, "walking", d1.Walking, d2.Walking, a.Species, b.Species);
                Slots(lines, where, "surf", d1.Surf, d2.Surf, a.Species, b.Species); Slots(lines, where, "oldRod", d1.OldRod, d2.OldRod, a.Species, b.Species);
                Slots(lines, where, "goodRod", d1.GoodRod, d2.GoodRod, a.Species, b.Species); Slots(lines, where, "superRod", d1.SuperRod, d2.SuperRod, a.Species, b.Species);
                for (int k = 0; k < 2; k++) { Species(lines, where, Loc.T("report.enc.swarm", k + 1), Sp(a.Species, d1.Swarm[k]), Sp(b.Species, d2.Swarm[k])); Species(lines, where, Loc.T("report.enc.day", k + 1), Sp(a.Species, d1.Day[k]), Sp(b.Species, d2.Day[k])); Species(lines, where, Loc.T("report.enc.night", k + 1), Sp(a.Species, d1.Night[k]), Sp(b.Species, d2.Night[k])); }
                for (int k = 0; k < 4; k++) Species(lines, where, Loc.T("report.enc.radar", k + 1), Sp(a.Species, d1.Radar[k]), Sp(b.Species, d2.Radar[k]));
            }
            else lines.Add(new("encounters", Loc.T("report.enc.changed", where)));
        }
    }

    private static void Rate(List<ChangeLine> lines, string where, string kind, uint a, uint b)
    {
        if (a != b) lines.Add(new("encounters", Loc.T("report.enc.rate", where, Loc.T("report.enc.kind." + kind), a, b)));
    }

    private static void Species(List<ChangeLine> lines, string where, string slot, string a, string b)
    {
        if (a != b) lines.Add(new("encounters", Loc.T("report.enc.species", where, slot, a, b)));
    }

    private static void Slots(List<ChangeLine> lines, string where, string kind, EncounterSlot[] a, EncounterSlot[] b, IReadOnlyList<string> sa, IReadOnlyList<string> sb)
    {
        string Sp(IReadOnlyList<string> t, int i) => i >= 0 && i < t.Count ? t[i] : $"#{i}";
        for (int k = 0; k < Math.Min(a.Length, b.Length); k++)
        {
            if (a[k].Species == b[k].Species && a[k].MinLevel == b[k].MinLevel && a[k].MaxLevel == b[k].MaxLevel) continue;
            lines.Add(new("encounters", Loc.T("report.enc.species", where, $"{Loc.T("report.enc.kind." + kind)} {k + 1}",
                $"{Sp(sa, a[k].Species)} Lv{a[k].MinLevel}-{a[k].MaxLevel}", $"{Sp(sb, b[k].Species)} Lv{b[k].MinLevel}-{b[k].MaxLevel}")));
        }
    }

    private static void CompareTexts(TextRepository a, TextRepository b, List<ChangeLine> lines)
    {
        for (int id = 0; id < Math.Min(a.BankCount, b.BankCount); id++)
        {
            if (a.MessageCount(id) == b.MessageCount(id) && a.Bank(id).Texts.SequenceEqual(b.Bank(id).Texts)) continue;
            var x = a.Bank(id).Texts; var y = b.Bank(id).Texts;
            var diffs = Enumerable.Range(0, Math.Min(x.Count, y.Count)).Where(i => x[i] != y[i]).ToList();
            var title = b.Bank(id).Title;
            lines.Add(new("texts", Loc.T("report.txt.bank", title, diffs.Count + Math.Abs(x.Count - y.Count))));
            foreach (var i in diffs.Take(5))
                lines.Add(new("texts", Loc.T("report.txt.msg", title, i, Short(x[i]), Short(y[i]))));
            if (diffs.Count > 5) lines.Add(new("texts", Loc.T("report.txt.more", diffs.Count - 5)));
        }
    }

    private static string Short(string s) => s.Length <= 60 ? s.Replace('\n', ' ') : s[..57].Replace('\n', ' ') + "...";
}
