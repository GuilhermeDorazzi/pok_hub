using System.ComponentModel;
using ModelContextProtocol.Server;
using PokeHub.Core.Batch;
using PokeHub.Core.Gen4;
using PokeHub.Core.Projects;

namespace PokeHub.Mcp;

/// <summary>Escrita de dados do jogo: cada chamada valida antes de gravar e salva uma versão antes e depois, quando há git.</summary>
[McpServerToolType]
public static class GameWriteTools
{
    private static async Task Snapshot(HubContext hub, Project p, string message, LogCollector log)
    {
        if (hub.Git.IsAvailable) await hub.Git.SnapshotAsync(p, message, log);
    }

    private static Project Open(HubContext hub, string project)
    {
        var p = hub.OpenProject(project);
        if (!WorkspaceService.IsExtracted(p)) throw new InvalidOperationException("Workspace vazio: extraia a ROM primeiro (extract_rom).");
        return p;
    }

    [McpServerTool(Name = "set_text"), Description("Troca uma mensagem de um banco de texto. Use \\n para quebrar a linha e mantenha os comandos {NOME ...}. Diz se alguma linha passa da caixa de diálogo.")]
    public static async Task<object> SetText(HubContext hub, string project, [Description("Banco de texto")] int bank, [Description("Mensagem no banco")] int index, string text)
    {
        var p = Open(hub, project);
        var repo = TextRepository.Load(p);
        if (bank < 0 || bank >= repo.BankCount) throw new ArgumentException($"Banco {bank} não existe (0 a {repo.BankCount - 1}).");
        if (index < 0 || index >= repo.MessageCount(bank)) throw new ArgumentException($"Mensagem {index} não existe no banco {bank} (0 a {repo.MessageCount(bank) - 1}).");
        var log = new LogCollector();
        var before = repo.Bank(bank).Texts[index];
        await Snapshot(hub, p, $"Antes de editar o texto {bank}:{index}", log);
        repo.SetText(bank, index, text); // recusa caractere fora do charmap antes de gravar
        repo.Save();
        await Snapshot(hub, p, $"Texto {bank}:{index} editado", log);
        var font = Gen4Font.Load(repo.Game);
        var tooLong = font is null ? [] : TextPreview.Render(Gen4TextArchive.Encode(text), font).Lines.Where(l => l.TooLong).Select(l => new { line = l.Number, width = l.Width }).ToList();
        return new { before, after = text, maxWidth = TextPreview.BoxWidth, tooLong };
    }

    [McpServerTool(Name = "set_trainer_pokemon"), Description("Altera um Pokémon do time de um treinador. Só os campos informados mudam. Espécie e item são números, de list_trainers e get_trainer.")]
    public static async Task<object> SetTrainerPokemon(HubContext hub, string project, [Description("Id do treinador")] int trainer, [Description("Posição no time, a partir de 0")] int slot,
        int? species = null, int? level = null, int? item = null, [Description("0 a 255; os IVs são dificuldade * 31 / 255")] int? difficulty = null)
    {
        var p = Open(hub, project);
        var repo = TrainerRepository.Load(p);
        var t = repo.Trainers.FirstOrDefault(x => x.Id == trainer) ?? throw new ArgumentException($"Treinador {trainer} não existe.");
        if (slot < 0 || slot >= t.Party.Count) throw new ArgumentException($"O time tem {t.Party.Count} Pokémon: posição {slot} não existe.");
        if (species is { } s && (s <= 0 || s >= repo.Species.Count)) throw new ArgumentException($"Espécie {s} não existe (1 a {repo.Species.Count - 1}).");
        if (level is < 1 or > 100) throw new ArgumentException("Nível vai de 1 a 100.");
        if (item is { } it && (it < 0 || it >= repo.Items.Count)) throw new ArgumentException($"Item {it} não existe (0 a {repo.Items.Count - 1}).");
        if (difficulty is < 0 or > 255) throw new ArgumentException("Dificuldade vai de 0 a 255.");

        var log = new LogCollector();
        await Snapshot(hub, p, $"Antes de editar o treinador {trainer}", log);
        var m = t.Party[slot];
        if (species is { } ns) m.Species = (ushort)ns;
        if (level is { } nl) m.Level = (ushort)nl;
        if (item is { } ni) m.Item = (ushort)ni;
        if (difficulty is { } nd) m.Difficulty = (byte)nd;
        repo.Save();
        await Snapshot(hub, p, $"Treinador {trainer} editado", log);
        return new { trainer = repo.NameOf(t), slot, species = repo.Species[m.Species], m.Level, item = repo.Items[m.Item], m.Difficulty, itemShows = t.HasItems };
    }

    [McpServerTool(Name = "replace_encounter_species"), Description("Troca uma espécie por outra em todas as tabelas de encontros selvagens. Com apply=false (padrão), só mostra o que mudaria.")]
    public static async Task<object> ReplaceEncounterSpecies(HubContext hub, string project, int from, int to, bool apply = false)
    {
        var p = Open(hub, project);
        var repo = EncounterRepository.Load(p);
        if (from <= 0 || from >= repo.Species.Count || to <= 0 || to >= repo.Species.Count) throw new ArgumentException($"Espécie fora de 1 a {repo.Species.Count - 1}.");
        var options = new EncounterBatchOptions { ReplaceSpeciesFrom = (ushort)from, ReplaceSpeciesTo = (ushort)to };
        var preview = BatchEditor.PreviewEncounters(repo, options);
        if (!apply) return new { applied = false, changes = preview.Count, preview = preview.Take(50).ToList() };
        var log = new LogCollector();
        await Snapshot(hub, p, "Antes de trocar espécie nos encontros", log);
        BatchEditor.ApplyEncounters(repo, options);
        repo.Save();
        await Snapshot(hub, p, $"Encontros: {repo.Species[from]} trocado por {repo.Species[to]}", log);
        return new { applied = true, changes = preview.Count };
    }

    private static async Task<object> RunBatch(HubContext hub, Project p, string what, bool apply, List<string> preview, Action write)
    {
        if (!apply) return new { applied = false, changes = preview.Count, preview = preview.Take(50).ToList() };
        var log = new LogCollector();
        await Snapshot(hub, p, $"Antes de {what}", log);
        write();
        await Snapshot(hub, p, $"Lote: {what} ({preview.Count} mudanças)", log);
        return new { applied = true, changes = preview.Count };
    }

    [McpServerTool(Name = "batch_wild_levels"), Description("Encontros selvagens em lote. Filtra por region (all, johto, kanto) e places (nomes separados por vírgula). floor e ceiling distribuem o nível por raridade: o espaço mais comum no piso, o mais raro no teto. evolve troca pela espécie evoluída para o nível; pedra, amizade e troca entram em otherAt. Com apply=false (padrão), só mostra a prévia.")]
    public static Task<object> BatchWildLevels(HubContext hub, string project, string region = "all", string places = "", int? floor = null, int? ceiling = null, bool evolve = false, int otherAt = 40, bool apply = false)
    {
        var p = Open(hub, project);
        var repo = EncounterRepository.Load(p);
        var options = new EncounterBatchOptions
        {
            Region = region.ToLowerInvariant() switch { "johto" => EncounterRegion.First, "kanto" => EncounterRegion.Second, "all" => EncounterRegion.All, _ => throw new ArgumentException("region: all, johto ou kanto.") },
            PlaceFilter = places, RarityFloor = floor, RarityCeiling = ceiling, Evolutions = evolve ? EvolutionChart.Load(p) : null, EvolveOtherAt = otherAt,
        };
        return RunBatch(hub, p, "mudar encontros", apply, BatchEditor.PreviewEncounters(repo, options), () => { BatchEditor.ApplyEncounters(repo, options); repo.Save(); });
    }

    [McpServerTool(Name = "batch_species_exp"), Description("Multiplica a experiência que cada espécie dá ao ser vencida. percent 70 deixa o jogo mais lento. Com apply=false (padrão), só mostra a prévia.")]
    public static Task<object> BatchSpeciesExp(HubContext hub, string project, double percent, bool apply = false)
    {
        var p = Open(hub, project);
        var repo = PersonalRepository.Load(p);
        var options = new SpeciesBatchOptions { ExpPercent = percent };
        return RunBatch(hub, p, "mudar a experiência das espécies", apply, BatchEditor.PreviewSpecies(repo, options), () => { BatchEditor.ApplySpecies(repo, options); repo.Save(); });
    }

    [McpServerTool(Name = "batch_trainer_difficulty"), Description("Define a dificuldade (0 a 255, define os IVs) dos times, por texto no nome do treinador; vazio alcança todos. Com apply=false (padrão), só mostra a prévia.")]
    public static Task<object> BatchTrainerDifficulty(HubContext hub, string project, int difficulty, string nameFilter = "", bool apply = false)
    {
        var p = Open(hub, project);
        if (difficulty is < 0 or > 255) throw new ArgumentException("Dificuldade vai de 0 a 255.");
        var repo = TrainerRepository.Load(p);
        var options = new TrainerBatchOptions { NameFilter = nameFilter, Difficulty = (byte)difficulty };
        return RunBatch(hub, p, "mudar a dificuldade dos times", apply, BatchEditor.PreviewTrainers(repo, options), () => { BatchEditor.ApplyTrainers(repo, options); repo.Save(); });
    }

    [McpServerTool(Name = "get_pokemon_stats"), Description("Lê stats base, tipos, habilidades e outros dados de uma espécie.")]
    public static object GetPokemonStats(HubContext hub, string project, int species)
    {
        var repo = PersonalRepository.Load(Open(hub, project));
        var d = repo.Get(species) ?? throw new ArgumentException($"Espécie {species} não existe.");
        return Describe(repo, species, d);
    }

    [McpServerTool(Name = "set_pokemon_stats"), Description("Altera stats base e dados de uma espécie. Só os campos informados mudam. Salva versão antes e depois.")]
    public static async Task<object> SetPokemonStats(HubContext hub, string project, int species, int? hp = null, int? attack = null, int? defense = null, int? spAttack = null, int? spDefense = null, int? speed = null, int? type1 = null, int? type2 = null, int? ability1 = null, int? ability2 = null, int? catchRate = null)
    {
        var p = Open(hub, project);
        var repo = PersonalRepository.Load(p);
        var d = repo.Get(species) ?? throw new ArgumentException($"Espécie {species} não existe.");
        foreach (var (name, value) in new[] { ("hp", hp), ("attack", attack), ("defense", defense), ("spAttack", spAttack), ("spDefense", spDefense), ("speed", speed), ("catchRate", catchRate) })
            if (value is < 0 or > 255) throw new ArgumentException($"{name} vai de 0 a 255.");
        foreach (var (name, value) in new[] { ("type1", type1), ("type2", type2) })
            if (value is { } v && (v < 0 || v >= PersonalRepository.TypeNames.Count)) throw new ArgumentException($"{name} vai de 0 a {PersonalRepository.TypeNames.Count - 1}.");
        foreach (var (name, value) in new[] { ("ability1", ability1), ("ability2", ability2) })
            if (value is { } v && (v < 0 || v >= repo.Abilities.Count || v > 255)) throw new ArgumentException($"{name} não existe.");

        var log = new LogCollector();
        await Snapshot(hub, p, $"Antes de editar os dados de {repo.NameOf(species)}", log);
        if (hp is { } a) d.Hp = (byte)a;
        if (attack is { } b) d.Attack = (byte)b;
        if (defense is { } c) d.Defense = (byte)c;
        if (spAttack is { } e) d.SpAttack = (byte)e;
        if (spDefense is { } f) d.SpDefense = (byte)f;
        if (speed is { } g) d.Speed = (byte)g;
        if (type1 is { } h) d.Type1 = (byte)h;
        if (type2 is { } i) d.Type2 = (byte)i;
        if (ability1 is { } j) d.Ability1 = (byte)j;
        if (ability2 is { } k) d.Ability2 = (byte)k;
        if (catchRate is { } l) d.CatchRate = (byte)l;
        repo.Save();
        await Snapshot(hub, p, $"Dados de {repo.NameOf(species)} editados", log);
        return Describe(repo, species, d);
    }

    private static object Describe(PersonalRepository repo, int species, PersonalData d) => new
    {
        species, name = repo.NameOf(species),
        stats = new { d.Hp, d.Attack, d.Defense, d.SpAttack, d.SpDefense, d.Speed, d.Total },
        types = new[] { PersonalRepository.TypeNames.ElementAtOrDefault(d.Type1), PersonalRepository.TypeNames.ElementAtOrDefault(d.Type2) },
        abilities = new[] { repo.Abilities.ElementAtOrDefault(d.Ability1), repo.Abilities.ElementAtOrDefault(d.Ability2) },
        d.CatchRate, d.ExpYield, d.Friendship,
    };
}
