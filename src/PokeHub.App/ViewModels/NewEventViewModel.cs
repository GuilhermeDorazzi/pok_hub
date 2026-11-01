using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;
using PokeHub.Core.Localization;
using PokeHub.Core.Projects;

namespace PokeHub.App.ViewModels;

public sealed record NewEventMap(int Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record NewEventSprite(int Id, BitmapSource Image);

public sealed record NewEventCreated(CreatedNpc Npc, string Label)
{
    public override string ToString() => Label;
}

public sealed record NewEventChoice(int Value, string Label, Func<int, BitmapSource?>? IconOf = null)
{
    private BitmapSource? _icon;
    private bool _iconLoaded;

    /// <summary>Lido só quando a linha aparece: as listas têm centenas de opções.</summary>
    public BitmapSource? Icon
    {
        get
        {
            if (!_iconLoaded) { _icon = IconOf?.Invoke(Value); _iconLoaded = true; }
            return _icon;
        }
    }

    // só valor e rótulo: o ícone carrega depois, e um hash que muda faz a lista perder a opção selecionada
    public bool Equals(NewEventChoice? other) => other is not null && Value == other.Value && Label == other.Label;
    public override int GetHashCode() => HashCode.Combine(Value, Label);

    public override string ToString() => Label;
}

/// <summary>Cria um NPC: mapa e posição na planta, aparência e o que ele faz. Grava tudo de uma vez, com versão antes e depois.</summary>
public sealed partial class GuideRow : ObservableObject
{
    [ObservableProperty] private NewEventChoice? _choice;
    [ObservableProperty] private string _text = "";
}

public sealed partial class NewEventViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly Action<string> _log;
    private readonly Func<string, Task> _snapshot;
    private readonly Gen4Font? _font;
    private EventMap _map;
    private readonly List<NewEventMap> _allMaps;
    private readonly RealMapImages _realMap;
    private int _imageRequest;

    public ObservableCollection<NewEventMap> Maps { get; } = [];
    public ObservableCollection<NewEventCreated> Created { get; } = [];
    public IReadOnlyList<NewEventSprite> Sprites { get; }
    public IReadOnlyList<NewEventChoice> Facings { get; } =
    [
        new(1, Loc.T("newev.facing.down")), new(0, Loc.T("newev.facing.up")), new(2, Loc.T("newev.facing.left")), new(3, Loc.T("newev.facing.right")),
    ];
    public IReadOnlyList<NewEventChoice> Items { get; }
    public event Action? LayoutChanged;

    [ObservableProperty] private string _mapFilter = "";
    [ObservableProperty] private NewEventMap? _selectedMap;
    [ObservableProperty] private BitmapSource? _layoutImage;
    [ObservableProperty] private bool _showRealMap = true;
    [ObservableProperty] private NewEventSprite? _selectedSprite;
    [ObservableProperty] private NewEventChoice? _selectedFacing;
    [ObservableProperty] private bool _isTalk = true;
    [ObservableProperty] private bool _isItem;
    [ObservableProperty] private bool _isPokemon;
    [ObservableProperty] private bool _isTrade;
    [ObservableProperty] private bool _isQuest;
    [ObservableProperty] private bool _isSell;
    [ObservableProperty] private bool _isTrainer;
    // missão
    [ObservableProperty] private bool _isGround;
    [ObservableProperty] private bool _isGuide;
    [ObservableProperty] private NewEventChoice? _groundItem;
    [ObservableProperty] private string _groundQuantity = "1";
    [ObservableProperty] private bool _groundHidden;
    [ObservableProperty] private string _guideFinalText = "";
    public IReadOnlyList<GuideRow> GuideSteps { get; } = Enumerable.Range(0, 5).Select(_ => new GuideRow()).ToList();
    [ObservableProperty] private string _tradeMinLevel = "0";
    [ObservableProperty] private string _tradeTooLowText = "";
    [ObservableProperty] private bool _questAsksItem = true;
    [ObservableProperty] private bool _questAsksNothing;
    [ObservableProperty] private string _condMaxLevel = "0";
    [ObservableProperty] private NewEventChoice? _questAskItem;
    [ObservableProperty] private string _questAskQuantity = "1";
    [ObservableProperty] private bool _questTakeItem = true;
    [ObservableProperty] private NewEventChoice? _questAskSpecies;
    [ObservableProperty] private bool _rewardIsItem = true;
    [ObservableProperty] private bool _rewardIsPokemon;
    [ObservableProperty] private bool _rewardIsMoney;
    [ObservableProperty] private NewEventChoice? _rewardItem;
    [ObservableProperty] private string _rewardQuantity = "1";
    [ObservableProperty] private NewEventChoice? _rewardSpecies;
    [ObservableProperty] private string _rewardLevel = "10";
    [ObservableProperty] private string _rewardMoney = "1000";
    [ObservableProperty] private string _questAskText = "";
    [ObservableProperty] private string _questNotYetText = "";
    [ObservableProperty] private string _questRewardText = "";
    [ObservableProperty] private string _questDoneText = "";
    [ObservableProperty] private string _questFullText = Loc.T("newev.quest.fullDefault");
    // venda
    [ObservableProperty] private NewEventChoice? _sellSpecies;
    [ObservableProperty] private string _sellLevel = "10";
    [ObservableProperty] private string _sellPrice = "5000";
    [ObservableProperty] private bool _sellOnce = true;
    [ObservableProperty] private string _sellOfferText = "";
    [ObservableProperty] private string _sellThanksText = "";
    [ObservableProperty] private string _sellNoMoneyText = Loc.T("newev.sell.noMoneyDefault");
    [ObservableProperty] private string _sellDeclineText = Loc.T("newev.trade.declineDefault");
    [ObservableProperty] private string _sellFullText = Loc.T("newev.quest.fullDefault");
    [ObservableProperty] private string _sellSoldOutText = Loc.T("newev.sell.soldOutDefault");
    // treinador
    [ObservableProperty] private NewEventChoice? _trainerChoice;
    [ObservableProperty] private bool _trainerSighted;
    [ObservableProperty] private string _trainerSight = "3";
    [ObservableProperty] private bool _trainerVanish;
    [ObservableProperty] private NewEventChoice? _rematchMode;
    [ObservableProperty] private NewEventChoice? _rematchTrainerChoice;
    [ObservableProperty] private string _trainerChallengeText = "";
    [ObservableProperty] private string _trainerBeatenText = "";
    [ObservableProperty] private string _trainerAfterText = "";
    [ObservableProperty] private string _trainerRematchText = "";
    // condições
    [ObservableProperty] private bool _condAfterElite;
    [ObservableProperty] private string _condMinBadges = "0";
    [ObservableProperty] private NewEventChoice? _condBadge;
    [ObservableProperty] private NewEventChoice? _condAfterNpc;
    [ObservableProperty] private string _condText = "";
    [ObservableProperty] private NewEventChoice? _selectedMovement;
    [ObservableProperty] private string _range = "2";
    [ObservableProperty] private NewEventChoice? _askSpecies;
    [ObservableProperty] private NewEventChoice? _giveSpecies;
    [ObservableProperty] private string _nickname = "";
    [ObservableProperty] private string _askText = "";
    [ObservableProperty] private string _thanksText = "";
    [ObservableProperty] private string _wrongText = Loc.T("newev.trade.wrongDefault");
    [ObservableProperty] private string _declineText = Loc.T("newev.trade.declineDefault");
    [ObservableProperty] private NewEventChoice? _selectedSpecies;
    [ObservableProperty] private string _level = "10";
    [ObservableProperty] private bool _vanish = true;
    [ObservableProperty] private bool _blocks;
    [ObservableProperty] private string _pokemonText = "";
    [ObservableProperty] private BitmapSource? _pokemonSprite;
    [ObservableProperty] private string _talkText = "";
    [ObservableProperty] private string _beforeText = "";
    [ObservableProperty] private string _afterText = "";
    [ObservableProperty] private NewEventChoice? _selectedItem;
    [ObservableProperty] private string _quantity = "1";
    [ObservableProperty] private string _position = Loc.T("newev.clickMap");
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private NewEventCreated? _selectedCreated;

    public MapLayout? Layout { get; private set; }
    /// <summary>Pixels por tile da imagem na tela: o mapa real é desenhado com mais detalhe que a planta.</summary>
    public int TileScale { get; private set; } = 8;
    public bool RealMapAvailable => _realMap.Available;
    /// <summary>Tile escolhido, em coordenadas da planta.</summary>
    public (int X, int Y)? Tile { get; private set; }
    public IReadOnlyList<NewEventChoice> Trainers { get; }
    public IReadOnlyList<NewEventChoice> RematchTrainers { get; }
    public IReadOnlyList<NewEventChoice> RematchModes { get; } =
    [
        new((int)TrainerRematch.None, Loc.T("newev.rematch.none")), new((int)TrainerRematch.Always, Loc.T("newev.rematch.always")), new((int)TrainerRematch.AfterElite, Loc.T("newev.rematch.elite")),
    ];
    public IReadOnlyList<NewEventChoice> Badges { get; } =
        new[] { Loc.T("newev.cond.noBadge"), "Zephyr", "Hive", "Plain", "Fog", "Storm", "Mineral", "Glacier", "Rising", "Boulder", "Cascade", "Thunder", "Rainbow", "Soul", "Marsh", "Volcano", "Earth" }
            .Select((name, i) => new NewEventChoice(i - 1, name)).ToList();
    public ObservableCollection<NewEventChoice> FinishedNpcs { get; } = [];
    public bool UsesScriptTexts => !(IsTrainer && TrainerSighted);
    public bool UsesRematchText => RematchMode?.Value is (int)TrainerRematch.Always or (int)TrainerRematch.AfterElite;
    public bool ShowsRematchText => UsesRematchText && UsesScriptTexts;
    public bool QuestAsksPokemon => !QuestAsksItem && !QuestAsksNothing;
    public bool QuestAsksSomething => !QuestAsksNothing;
    public IReadOnlyList<NewEventChoice> Species { get; }
    public IReadOnlyList<NewEventChoice> AllSpecies { get; }
    public bool UsesSpriteList => !IsPokemon;
    public bool IsEditing => SelectedCreated is not null;
    public string SaveLabel => Loc.T(IsEditing ? "newev.save" : "newev.create");
    public string FreeTradeNote { get; }
    public IReadOnlyList<NewEventChoice> Movements { get; } =
    [
        new((int)NpcMovement.Fixed, Loc.T("newev.move.fixed")), new((int)NpcMovement.LookAround, Loc.T("newev.move.look")),
        new((int)NpcMovement.Wander, Loc.T("newev.move.wander")), new((int)NpcMovement.UpDown, Loc.T("newev.move.updown")), new((int)NpcMovement.LeftRight, Loc.T("newev.move.leftright")),
    ];
    public bool UsesFacing => SelectedMovement?.Value is null or (int)NpcMovement.Fixed;
    public bool UsesRange => SelectedMovement?.Value is (int)NpcMovement.Wander or (int)NpcMovement.UpDown or (int)NpcMovement.LeftRight;

    public NewEventViewModel(Project project, EventMap map, IReadOnlyList<string> items, IReadOnlyList<string> species, Action<string> log, Func<string, Task> snapshot)
    {
        _project = project; _map = map; _log = log; _snapshot = snapshot;
        _font = Gen4Font.Load(map.Game);
        _realMap = new RealMapImages(map.Game);
        var icons = GameIcons.TryLoad(map.Game);
        Func<int, BitmapSource?>? itemIcon = icons is null ? null : id => icons.Item(id)?.ToBitmap();
        Func<int, BitmapSource?>? speciesIcon = icons is null ? null : id => icons.Species(id)?.ToBitmap();
        Items = items.Select((name, i) => new NewEventChoice(i, $"{i:D3} {name}", itemIcon)).Skip(1).ToList();
        SelectedItem = Items.FirstOrDefault();
        Species = species.Select((name, i) => new NewEventChoice(i, $"{i:D3} {name}", speciesIcon)).Where(s => NewEventService.SpriteForSpecies(s.Value, map.Game.IsPlatinum) is not null).ToList();
        SelectedSpecies = Species.FirstOrDefault();
        // a troca aceita qualquer espécie: o que precisa de sprite de campo é só o Pokémon no mapa
        AllSpecies = species.Select((name, i) => new NewEventChoice(i, $"{i:D3} {name}", speciesIcon)).Where(s => s.Value is >= 1 and <= 493).ToList();
        AskSpecies = AllSpecies.FirstOrDefault();
        GiveSpecies = AllSpecies.FirstOrDefault();
        SelectedMovement = Movements[0];
        int freeTrades = NewEventService.FreeTradeSlots(map.Game).Count;
        FreeTradeNote = freeTrades == 0 ? Loc.T("core.newev.noTradeSlot") : Loc.T("newev.trade.free", freeTrades);
        SelectedFacing = Facings[0];
        QuestAskItem = RewardItem = GroundItem = Items.FirstOrDefault();
        QuestAskSpecies = RewardSpecies = SellSpecies = AllSpecies.FirstOrDefault();
        var trainers = Core.Gen4.TrainerRepository.Load(project);
        Trainers = trainers.Trainers.Where(t => t.Id > 0).Select(t => new NewEventChoice(t.Id, $"{t.Id:D3} {trainers.ClassOf(t)} {trainers.NameOf(t)}")).ToList();
        RematchTrainers = [new NewEventChoice(0, Loc.T("newev.rematch.same")), .. Trainers];
        TrainerChoice = Trainers.FirstOrDefault();
        RematchTrainerChoice = RematchTrainers[0];
        RematchMode = RematchModes[0];
        CondBadge = Badges[0];
        Sprites = map.Sprites is { } sprites
            ? sprites.Ids.Select(id => (id, image: sprites.Render(id))).Where(s => s.image is not null).Select(s => new NewEventSprite(s.id, s.image!.ToBitmap())).ToList()
            : [];
        SelectedSprite = Sprites.FirstOrDefault();
        _allMaps = map.Headers.Where(h => h.ScriptsBank >= 0 && h.EventsBank >= 0 && h.MsgBank >= 0)
            .Select(h => new NewEventMap(h.Id, $"{h.InternalName}  {(h.LocationNameIndex >= 0 && h.LocationNameIndex < map.LocationNames.Count ? map.LocationNames[h.LocationNameIndex] : "")}".Trim())).ToList();
        ApplyMapFilter();
        // os primeiros da lista podem ser mapas sem célula própria, que só existem como planta do mundo inteiro
        SelectedMap = Maps.FirstOrDefault(m => map.LayoutOf(m.Id) is { } layout && RealMapRenderer.CanRender(layout)) ?? Maps.FirstOrDefault();
    }

    partial void OnMapFilterChanged(string value) => ApplyMapFilter();
    partial void OnIsPokemonChanged(bool value) => OnPropertyChanged(nameof(UsesSpriteList));
    partial void OnIsTrainerChanged(bool value) => OnPropertyChanged(nameof(UsesScriptTexts));
    partial void OnTrainerSightedChanged(bool value)
    {
        OnPropertyChanged(nameof(UsesScriptTexts));
        OnPropertyChanged(nameof(ShowsRematchText));
    }
    partial void OnRematchModeChanged(NewEventChoice? value)
    {
        OnPropertyChanged(nameof(UsesRematchText));
        OnPropertyChanged(nameof(ShowsRematchText));
    }
    partial void OnQuestAsksItemChanged(bool value) => OnPropertyChanged(nameof(QuestAsksPokemon));
    partial void OnQuestAsksNothingChanged(bool value)
    {
        OnPropertyChanged(nameof(QuestAsksPokemon));
        OnPropertyChanged(nameof(QuestAsksSomething));
    }
    partial void OnSelectedMovementChanged(NewEventChoice? value)
    {
        OnPropertyChanged(nameof(UsesFacing));
        OnPropertyChanged(nameof(UsesRange));
    }
    partial void OnSelectedSpeciesChanged(NewEventChoice? value) =>
        PokemonSprite = value is not null && NewEventService.SpriteForSpecies(value.Value, _map.Game.IsPlatinum) is { } sprite ? SpriteOf(sprite) : null;

    private void ApplyMapFilter()
    {
        var keep = SelectedMap;
        Maps.Clear();
        foreach (var m in _allMaps.Where(m => string.IsNullOrWhiteSpace(MapFilter) || m.Label.Contains(MapFilter, StringComparison.OrdinalIgnoreCase))) Maps.Add(m);
        if (keep is not null && Maps.Contains(keep)) SelectedMap = keep;
    }

    partial void OnSelectedMapChanged(NewEventMap? value) => LoadLayout();

    private void LoadLayout()
    {
        Tile = null;
        Position = Loc.T("newev.clickMap");
        Layout = SelectedMap is { } m ? _map.LayoutOf(m.Id) : null;
        LoadCreated();
        _ = ShowLayoutImageAsync();
    }

    partial void OnShowRealMapChanged(bool value) => _ = ShowLayoutImageAsync();

    /// <summary>A planta aparece na hora; o mapa real é desenhado fora da tela e entra quando fica pronto, se o mapa ainda for o mesmo.</summary>
    private async Task ShowLayoutImageAsync()
    {
        int request = ++_imageRequest;
        var layout = Layout;
        var map = SelectedMap;
        var failed = Loc.T("newev.realMap.failed");
        if (Status == failed) Status = "";
        bool wantsReal = ShowRealMap && _realMap.Available && layout is not null && map is not null;
        if (wantsReal && _realMap.Known(map!.Id) is { } known)
        {
            SetLayoutImage(known, RealMapRenderer.PixelsPerTile);
            return;
        }
        SetLayoutImage(layout?.Render().ToBitmap(), 8);
        if (!wantsReal) return;

        var image = await _realMap.GetAsync(layout!, map!.Id, _map.Headers.First(h => h.Id == map.Id).AreaId);
        if (request != _imageRequest) return;
        if (image is null) Status = failed;
        else SetLayoutImage(image, RealMapRenderer.PixelsPerTile);
    }

    private void SetLayoutImage(BitmapSource? image, int scale)
    {
        LayoutImage = image;
        TileScale = scale;
        LayoutChanged?.Invoke();
    }

    private void LoadCreated()
    {
        Created.Clear();
        FinishedNpcs.Clear();
        FinishedNpcs.Add(new NewEventChoice(0, Loc.T("newev.cond.noNpc")));
        foreach (var npc in NewEventLedger.Load(_project).Npcs.Where(n => n.Flag is not null && n.Kind is "quest" or "trade" or "trainer" or "item" or "sell"))
            FinishedNpcs.Add(new NewEventChoice(npc.Flag!.Value, $"{Loc.T($"newev.kind.{npc.Kind}")} · {_map.MapTitle(npc.MapId)} · {npc.X}, {npc.Z}"));
        CondAfterNpc = FinishedNpcs[0];
        if (SelectedMap is not { } map) return;
        foreach (var npc in NewEventLedger.Load(_project).Npcs.Where(n => n.MapId == map.Id))
            Created.Add(new NewEventCreated(npc, Loc.T($"newev.kind.{npc.Kind}") + $" · {npc.X}, {npc.Z}" + (npc.Texts.FirstOrDefault() is { } text ? " · " + Unwrap(text) : "")));
    }

    private static string Unwrap(string text) => text.Replace("\\n", " ").Replace("\\r", " ").Trim();

    /// <summary>Selecionar um NPC criado leva os dados dele para o formulário; Salvar passa a alterar esse NPC.</summary>
    partial void OnSelectedCreatedChanged(NewEventCreated? value)
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(SaveLabel));
        if (value?.Npc is not { } npc) return;
        if (Layout is not null) PickTile(npc.X - Layout.OriginX, npc.Z - Layout.OriginY);
        SelectedSprite = Sprites.FirstOrDefault(s => s.Id == npc.SpriteId) ?? SelectedSprite;
        SelectedFacing = Facings.FirstOrDefault(f => f.Value == npc.Facing) ?? SelectedFacing;
        SelectedMovement = Movements.FirstOrDefault(m => m.Value == (int)npc.Movement) ?? SelectedMovement;
        Range = npc.Range.ToString();
        IsTalk = npc.Kind == "talk";
        IsItem = npc.Kind == "item";
        IsPokemon = npc.Kind == "pokemon";
        IsTrade = npc.Kind == "trade";
        IsGround = npc.Kind == "ground";
        IsGuide = npc.Kind == "guide";
        IsQuest = npc.Kind == "quest";
        IsSell = npc.Kind == "sell";
        IsTrainer = npc.Kind == "trainer";
        CondAfterElite = npc.ConditionAfterElite;
        CondMinBadges = npc.ConditionMinBadges.ToString();
        CondMaxLevel = npc.ConditionMaxPartyLevel.ToString();
        CondBadge = Badges.FirstOrDefault(b => b.Value == (npc.ConditionBadge ?? -1)) ?? Badges[0];
        CondAfterNpc = FinishedNpcs.FirstOrDefault(f => f.Value == (npc.ConditionAfterFlag ?? 0)) ?? FinishedNpcs[0];
        CondText = Unwrap(npc.ConditionText ?? "");
        switch (npc.ToBehavior())
        {
            case TalkBehavior t:
                TalkText = Unwrap(t.Text);
                break;
            case GiveItemBehavior i:
                SelectedItem = Items.FirstOrDefault(c => c.Value == i.Item) ?? SelectedItem;
                Quantity = i.Quantity.ToString();
                BeforeText = Unwrap(i.BeforeText);
                AfterText = Unwrap(i.AfterText);
                break;
            case PokemonBehavior p:
                SelectedSpecies = Species.FirstOrDefault(c => c.Value == p.Species) ?? SelectedSpecies;
                Level = p.Level.ToString();
                Vanish = p.Vanish;
                Blocks = p.Blocks;
                PokemonText = Unwrap(p.Text ?? "");
                break;
            case GroundItemBehavior g:
                GroundItem = Items.FirstOrDefault(c => c.Value == g.Item) ?? GroundItem;
                GroundQuantity = g.Quantity.ToString();
                GroundHidden = g.Hidden;
                break;
            case GuideBehavior guide:
                for (int i = 0; i < GuideSteps.Count; i++)
                {
                    var step = guide.Steps.ElementAtOrDefault(i);
                    GuideSteps[i].Choice = FinishedNpcs.FirstOrDefault(c => c.Value == (step?.Flag ?? 0)) ?? FinishedNpcs.FirstOrDefault();
                    GuideSteps[i].Text = step is null ? "" : Unwrap(step.Text);
                }
                GuideFinalText = Unwrap(guide.FinalText);
                break;
            case TradeBehavior t:
                TradeMinLevel = t.MinLevel.ToString();
                TradeTooLowText = Unwrap(t.TooLowText ?? "");
                AskSpecies = AllSpecies.FirstOrDefault(c => c.Value == t.AskSpecies) ?? AskSpecies;
                GiveSpecies = AllSpecies.FirstOrDefault(c => c.Value == t.GiveSpecies) ?? GiveSpecies;
                Nickname = t.Nickname ?? "";
                AskText = Unwrap(t.AskText);
                ThanksText = Unwrap(t.ThanksText);
                WrongText = Unwrap(t.WrongText);
                DeclineText = Unwrap(t.DeclineText).Replace("{YESNO 0}", "").Trim();
                break;
            case QuestBehavior q:
                QuestAsksItem = q.AskItem is not null;
                QuestAsksNothing = q.AskItem is null && q.AskSpecies is null;
                if (q.AskItem is { } askItem) QuestAskItem = Items.FirstOrDefault(c => c.Value == askItem) ?? QuestAskItem;
                if (q.AskSpecies is { } askSpecies) QuestAskSpecies = AllSpecies.FirstOrDefault(c => c.Value == askSpecies) ?? QuestAskSpecies;
                QuestAskQuantity = Math.Max(1, q.AskQuantity).ToString();
                QuestTakeItem = q.TakeItem;
                RewardIsItem = q.RewardItem is not null;
                RewardIsPokemon = q.RewardSpecies is not null;
                RewardIsMoney = q.RewardItem is null && q.RewardSpecies is null;
                if (q.RewardItem is { } rewardItem) RewardItem = Items.FirstOrDefault(c => c.Value == rewardItem) ?? RewardItem;
                if (q.RewardSpecies is { } rewardSpecies) RewardSpecies = AllSpecies.FirstOrDefault(c => c.Value == rewardSpecies) ?? RewardSpecies;
                RewardQuantity = Math.Max(1, q.RewardQuantity).ToString();
                RewardLevel = q.RewardLevel.ToString();
                RewardMoney = q.RewardMoney.ToString();
                QuestAskText = Unwrap(q.AskText);
                QuestNotYetText = Unwrap(q.NotYetText);
                QuestRewardText = Unwrap(q.RewardText);
                QuestDoneText = Unwrap(q.DoneText);
                QuestFullText = Unwrap(q.FullText);
                break;
            case SellBehavior v:
                SellSpecies = AllSpecies.FirstOrDefault(c => c.Value == v.Species) ?? SellSpecies;
                SellLevel = v.Level.ToString();
                SellPrice = v.Price.ToString();
                SellOnce = v.Once;
                SellOfferText = Unwrap(v.OfferText);
                SellThanksText = Unwrap(v.ThanksText);
                SellNoMoneyText = Unwrap(v.NoMoneyText);
                SellDeclineText = Unwrap(v.DeclineText);
                SellFullText = Unwrap(v.FullText);
                SellSoldOutText = Unwrap(v.SoldOutText);
                break;
            case TrainerBehavior t:
                TrainerChoice = Trainers.FirstOrDefault(c => c.Value == t.Trainer) ?? TrainerChoice;
                TrainerSighted = t.Sight > 0;
                TrainerSight = Math.Max(1, t.Sight).ToString();
                TrainerVanish = t.Vanish;
                RematchMode = RematchModes.FirstOrDefault(m => m.Value == (int)t.Rematch) ?? RematchModes[0];
                RematchTrainerChoice = RematchTrainers.FirstOrDefault(c => c.Value == (t.RematchTrainer ?? 0)) ?? RematchTrainers[0];
                TrainerChallengeText = Unwrap(t.ChallengeText);
                TrainerBeatenText = Unwrap(t.BeatenText);
                TrainerAfterText = Unwrap(t.AfterText);
                TrainerRematchText = Unwrap(t.RematchText ?? "");
                break;
        }
    }

    [RelayCommand]
    private void NewNpc()
    {
        SelectedCreated = null;
        ClearTexts();
        Status = "";
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (SelectedCreated?.Npc is not { } npc || SelectedMap is not { } map) return;
        try
        {
            await _snapshot(Loc.T("newev.snapshotBefore", map.Label));
            NewEventService.Remove(_project, _map, npc);
            await _snapshot(Loc.T("newev.snapshotRemoved", map.Label));
            _map = EventMap.Load(_project);
            SelectedCreated = null;
            LoadLayout();
            Status = Loc.T("newev.removed");
            _log(Status);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException)
        {
            Status = Loc.T("common.saveError", ex.Message);
        }
    }

    private void ClearTexts()
    {
        TalkText = BeforeText = AfterText = PokemonText = AskText = ThanksText = Nickname = "";
        QuestAskText = QuestNotYetText = QuestRewardText = QuestDoneText = "";
        SellOfferText = SellThanksText = "";
        TrainerChallengeText = TrainerBeatenText = TrainerAfterText = TrainerRematchText = "";
        CondText = "";
        CondAfterElite = false;
        CondMinBadges = "0";
        CondMaxLevel = "0";
        CondBadge = Badges[0];
        CondAfterNpc = FinishedNpcs.FirstOrDefault();
    }

    /// <summary>null quando nenhuma condição foi escolhida. Texto vazio com condição escolhida é recusado pelo serviço, para o jogador nunca ver uma fala em branco.</summary>
    private NpcCondition? BuildCondition()
    {
        int badges = int.TryParse(CondMinBadges, out var n) ? n : 0;
        int? badge = CondBadge is { Value: >= 0 } b ? b.Value : null;
        int? flag = CondAfterNpc is { Value: > 0 } f ? f.Value : null;
        int maxLevel = int.TryParse(CondMaxLevel, out var m) ? m : 0;
        return !CondAfterElite && badges == 0 && badge is null && flag is null && maxLevel == 0 ? null : new NpcCondition(Prepare(CondText), CondAfterElite, badges, badge, flag, maxLevel);
    }

    private static bool Filled(params string[] texts) => texts.All(t => !string.IsNullOrWhiteSpace(t));

    private NpcBehavior BuildQuest() => new QuestBehavior(
        QuestAsksItem ? QuestAskItem?.Value : null, int.Parse(QuestAskQuantity), QuestTakeItem, QuestAsksPokemon ? QuestAskSpecies?.Value : null,
        RewardIsItem ? RewardItem?.Value : null, int.Parse(RewardQuantity), RewardIsPokemon ? RewardSpecies?.Value : null, int.Parse(RewardLevel), RewardIsMoney ? int.Parse(RewardMoney) : 0,
        Prepare(QuestAskText), Prepare(QuestNotYetText), Prepare(QuestRewardText), Prepare(QuestDoneText), Prepare(QuestFullText));

    private List<PokeHub.Core.Gen4.Scripts.GuideStep> BuildGuideSteps() => GuideSteps.Where(g => g.Choice is { Value: > 0 } && !string.IsNullOrWhiteSpace(g.Text)).Select(g => new PokeHub.Core.Gen4.Scripts.GuideStep(g.Choice!.Value, Prepare(g.Text))).ToList();

    private NpcBehavior BuildSell() => new SellBehavior(SellSpecies!.Value, int.Parse(SellLevel), int.Parse(SellPrice), SellOnce,
        Prepare(SellOfferText), Prepare(SellThanksText), Prepare(SellNoMoneyText), Prepare(SellDeclineText), Prepare(SellFullText), Prepare(SellSoldOutText));

    private NpcBehavior BuildTrainer()
    {
        var rematch = (TrainerRematch)(RematchMode?.Value ?? 0);
        return new TrainerBehavior(TrainerChoice!.Value, Prepare(TrainerChallengeText), Prepare(TrainerBeatenText), Prepare(TrainerAfterText),
            TrainerSighted ? int.Parse(TrainerSight) : 0, TrainerVanish, rematch, RematchTrainerChoice is { Value: > 0 } r ? r.Value : null,
            rematch == TrainerRematch.None ? null : Prepare(TrainerRematchText));
    }

    public void PickTile(int x, int y)
    {
        if (Layout is null || x < 0 || y < 0 || x >= Layout.Width || y >= Layout.Height) return;
        Tile = (x, y);
        Position = Loc.T(Layout.IsBlocked(x, y) ? "newev.tileBlocked" : "newev.tile", x + Layout.OriginX, y + Layout.OriginY);
        LayoutChanged?.Invoke();
    }

    public BitmapSource? SpriteOf(int id) => _map.Sprites?.Render(id)?.ToBitmap();

    /// <summary>Texto como o jogo vai receber: quebrado para caber na caixa, com a fonte do próprio jogo.</summary>
    private string Prepare(string text)
    {
        var map = (Func<string, int>)(s => _font is null ? s.Length * 6 : TextPreview.Render(Gen4TextArchive.Encode(s), _font).Lines.Max(l => l.Width));
        return TextPreview.Wrap(text.Trim(), map);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (SelectedMap is not { } map || Layout is null) { Status = Loc.T("newev.needMap"); return; }
        if (Tile is not { } tile) { Status = Loc.T("newev.clickMap"); return; }
        if (SelectedSprite is not { } sprite || SelectedFacing is not { } facing) { Status = Loc.T("newev.needSprite"); return; }
        if (IsTalk && string.IsNullOrWhiteSpace(TalkText) || IsItem && (string.IsNullOrWhiteSpace(BeforeText) || string.IsNullOrWhiteSpace(AfterText))) { Status = Loc.T("newev.needText"); return; }
        if (IsItem && (SelectedItem is null || !int.TryParse(Quantity, out _))) { Status = Loc.T("core.newev.badQuantity"); return; }
        if (IsPokemon && (SelectedSpecies is null || !int.TryParse(Level, out _))) { Status = Loc.T("core.newev.badLevel"); return; }
        if (IsTrade && (AskSpecies is null || GiveSpecies is null)) { Status = Loc.T("core.newev.badSpecies"); return; }
        if (IsTrade && (string.IsNullOrWhiteSpace(AskText) || string.IsNullOrWhiteSpace(ThanksText) || string.IsNullOrWhiteSpace(WrongText) || string.IsNullOrWhiteSpace(DeclineText))) { Status = Loc.T("newev.needText"); return; }
        if (IsQuest && (!Filled(QuestAskText, QuestRewardText, QuestDoneText, QuestFullText) || (QuestAsksSomething && !Filled(QuestNotYetText)))) { Status = Loc.T("newev.needText"); return; }
        if (IsQuest && !(int.TryParse(QuestAskQuantity, out _) && int.TryParse(RewardQuantity, out _))) { Status = Loc.T("core.newev.badQuantity"); return; }
        if (IsQuest && !int.TryParse(RewardLevel, out _)) { Status = Loc.T("core.newev.badLevel"); return; }
        if (IsQuest && !int.TryParse(RewardMoney, out _)) { Status = Loc.T("core.newev.badMoney", 999999); return; }
        if (IsSell && !Filled(SellOfferText, SellThanksText, SellNoMoneyText, SellDeclineText, SellFullText, SellSoldOutText)) { Status = Loc.T("newev.needText"); return; }
        if (IsSell && !int.TryParse(SellLevel, out _)) { Status = Loc.T("core.newev.badLevel"); return; }
        if (IsGround && (GroundItem is null || !int.TryParse(GroundQuantity, out _))) { Status = Loc.T("core.newev.badQuantity"); return; }
        if (IsGuide && (BuildGuideSteps().Count == 0 || !Filled(GuideFinalText))) { Status = Loc.T("core.newev.badGuide", 8); return; }
        if (IsTrade && !int.TryParse(TradeMinLevel, out _)) { Status = Loc.T("core.newev.badLevel"); return; }
        if (IsSell && !int.TryParse(SellPrice, out _)) { Status = Loc.T("core.newev.badMoney", 999999); return; }
        if (IsTrainer && TrainerChoice is null) { Status = Loc.T("newev.needTrainer"); return; }
        if (IsTrainer && TrainerSighted && !int.TryParse(TrainerSight, out _)) { Status = Loc.T("core.newev.badSight", 15); return; }
        if (IsTrainer && !TrainerSighted && !Filled(TrainerChallengeText, TrainerBeatenText, TrainerAfterText)) { Status = Loc.T("newev.needText"); return; }
        if (!int.TryParse(CondMinBadges, out _)) { Status = Loc.T("core.newev.badBadges", 16); return; }
        if (!int.TryParse(CondMaxLevel, out var condLevel) || condLevel is < 0 or > 100) { Status = Loc.T("core.newev.badLevel"); return; }
        int range = 1;
        if (UsesRange && !int.TryParse(Range, out range)) { Status = Loc.T("core.newev.badRange", 8); return; }

        try
        {
            NpcBehavior behavior = IsGround ? new GroundItemBehavior(GroundItem!.Value, int.Parse(GroundQuantity), GroundHidden) : IsGuide ? new GuideBehavior(BuildGuideSteps(), Prepare(GuideFinalText)) : IsQuest ? BuildQuest() : IsSell ? BuildSell() : IsTrainer ? BuildTrainer() : IsTalk ? new TalkBehavior(Prepare(TalkText))
                : IsItem ? new GiveItemBehavior(SelectedItem!.Value, int.Parse(Quantity), Prepare(BeforeText), Prepare(AfterText))
                : IsTrade ? new TradeBehavior(AskSpecies!.Value, GiveSpecies!.Value, string.IsNullOrWhiteSpace(Nickname) ? null : Nickname.Trim(), Prepare(AskText), Prepare(ThanksText), Prepare(WrongText), Prepare(DeclineText), int.Parse(TradeMinLevel), Prepare(TradeTooLowText))
                : new PokemonBehavior(SelectedSpecies!.Value, int.Parse(Level), Vanish, string.IsNullOrWhiteSpace(PokemonText) ? null : Prepare(PokemonText), Vanish && Blocks);
            await _snapshot(Loc.T("newev.snapshotBefore", map.Label));
            var request = new NewNpc(map.Id, tile.X + Layout.OriginX, tile.Y + Layout.OriginY, sprite.Id, facing.Value, behavior, (NpcMovement)(SelectedMovement?.Value ?? 0), range, BuildCondition());
            bool editing = SelectedCreated is not null;
            var result = SelectedCreated?.Npc is { } existing ? NewEventService.Update(_project, _map, existing, request) : NewEventService.Create(_project, _map, request);
            await _snapshot(Loc.T("newev.snapshotAfter", map.Label));
            _map = EventMap.Load(_project); // flags, scripts e planta precisam refletir o NPC novo
            LoadLayout();
            SelectedCreated = null;
            Status = Loc.T(editing ? "newev.updated" : "newev.created", result.ScriptId) + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : "");
            _log(Status);
            ClearTexts();
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.InvalidDataException or NotSupportedException or System.IO.IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            Status = Loc.T("common.saveError", ex.Message);
        }
    }
}
