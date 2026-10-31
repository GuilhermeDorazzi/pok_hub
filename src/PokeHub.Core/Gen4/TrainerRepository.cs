using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.Core.Gen4;

/// <summary>Carrega e grava os treinadores de um projeto Gen 4, com as listas de nomes vindas do próprio jogo.</summary>
public sealed class TrainerRepository
{
    private readonly Gen4Game _game;
    private readonly Narc _trData;
    private readonly Narc _trParty;
    private readonly Narc _text;
    private readonly Gen4TextArchive _names;
    private readonly List<string> _originalNames;

    public Gen4Game Game => _game;
    public List<Trainer> Trainers { get; } = [];
    public List<string> Names { get; }
    public IReadOnlyList<string> Classes { get; }
    public IReadOnlyList<string> Species { get; }
    public IReadOnlyList<string> Items { get; }
    public IReadOnlyList<string> Moves { get; }
    /// <summary>Só leitura; null se o jogo não tiver o arquivo.</summary>
    public TrainerSprites? Sprites { get; }
    public ItemIcons? ItemIcons { get; }

    private TrainerRepository(Gen4Game game, Narc trData, Narc trParty, Narc text)
    {
        _game = game;
        _trData = trData;
        _trParty = trParty;
        _text = text;
        _names = Gen4TextArchive.Read(text.Files[game.TrainerNamesBank]);
        Names = _names.Messages.Select(m => Gen4TextArchive.Decode(m)).ToList();
        _originalNames = new List<string>(Names);
        Classes = ReadBank(game.TrainerClassesBank);
        Species = ReadBank(game.SpeciesBank);
        Items = ReadBank(game.ItemsBank);
        Moves = ReadBank(game.MovesBank);
        Sprites = TrainerSprites.Load(game);
        ItemIcons = ItemIcons.Load(game);

        for (int i = 0; i < trData.Files.Count; i++)
            Trainers.Add(Trainer.Parse(i, trData.Files[i], i < trParty.Files.Count ? trParty.Files[i] : [], game.HasBallSeal));
    }

    private List<string> ReadBank(int bank) =>
        bank < _text.Files.Count
            ? Gen4TextArchive.Read(_text.Files[bank]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList()
            : [];

    public static TrainerRepository Load(Project project)
    {
        var game = Gen4Game.For(project);
        foreach (var f in new[] { game.TrainerDataPath, game.TrainerPartyPath, game.TextArchivePath })
            if (!File.Exists(f)) throw new FileNotFoundException(Loc.T("core.trainer.fileMissing"), f);
        return new TrainerRepository(game,
            Narc.Read(File.ReadAllBytes(game.TrainerDataPath)),
            Narc.Read(File.ReadAllBytes(game.TrainerPartyPath)),
            Narc.Read(File.ReadAllBytes(game.TextArchivePath)));
    }

    public string NameOf(Trainer t) => t.Id < Names.Count ? Names[t.Id] : $"#{t.Id}";
    public string ClassOf(Trainer t) => t.Class < Classes.Count ? Classes[t.Class] : Loc.T("tr.classN", t.Class);

    /// <summary>Grava trdata, trpoke e, se algum nome mudou, o banco de nomes dentro do msg.narc.</summary>
    public void Save()
    {
        _trData.Files.Clear();
        _trParty.Files.Clear();
        foreach (var t in Trainers.OrderBy(t => t.Id))
        {
            var (data, party) = t.BytesToWrite(_game.HasBallSeal);
            _trData.Files.Add(data);
            _trParty.Files.Add(party);
            t.OriginalData = data;
            t.OriginalParty = party;
        }
        File.WriteAllBytes(_game.TrainerDataPath, _trData.Write());
        File.WriteAllBytes(_game.TrainerPartyPath, _trParty.Write());

        bool namesChanged = Names.Count != _originalNames.Count || Names.Where((n, i) => n != _originalNames[i]).Any();
        if (namesChanged)
        {
            while (_names.Messages.Count < Names.Count) _names.Messages.Add(new Gen4Message { IsCompressed = true });
            for (int i = 0; i < Names.Count; i++)
            {
                if (i < _originalNames.Count && Names[i] == _originalNames[i]) continue;
                _names.Messages[i].SetCodes(Gen4TextArchive.Encode(Names[i]));
            }
            _text.Files[_game.TrainerNamesBank] = _names.Write();
            File.WriteAllBytes(_game.TextArchivePath, _text.Write());
            _originalNames.Clear();
            _originalNames.AddRange(Names);
        }
    }
}
