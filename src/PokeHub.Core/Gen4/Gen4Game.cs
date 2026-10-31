using PokeHub.Core.Localization;
using PokeHub.Core.Projects;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Gen4;

public enum Gen4Language { English, Spanish, Italian, French, German, Japanese, Korean, Unknown }

/// <summary>Onde cada dado de Gen 4 fica dentro do workspace, por jogo e idioma. Fonte: RomInfo do DSPRE.</summary>
public sealed class Gen4Game
{
    public required BaseGame Game { get; init; }
    public required Gen4Language Language { get; init; }
    /// <summary>Raiz dos arquivos da ROM: workspace/files (ds-rom) ou workspace/data (ndstool).</summary>
    public required string FilesRoot { get; init; }
    /// <summary>arm9 principal: workspace/arm9/arm9.bin (ds-rom, já descomprimido) ou workspace/arm9.bin (ndstool, talvez comprimido).</summary>
    public required string Arm9Path { get; init; }

    public bool Japanese => Language == Gen4Language.Japanese;
    public bool IsHgss => Game is BaseGame.HeartGold or BaseGame.SoulSilver;
    public bool IsDp => Game is BaseGame.Diamond or BaseGame.Pearl;
    public bool IsPlatinum => Game == BaseGame.Platinum;
    /// <summary>DP não guarda o ball seal por Pokémon de treinador; Pt e HGSS guardam (u16 no fim).</summary>
    public bool HasBallSeal => !IsDp;

    public string TrainerDataPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\5\5" : @"poketool\trainer\trdata.narc");
    public string TrainerPartyPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\5\6" : @"poketool\trainer\trpoke.narc");
    public string TrainerGraphicsPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\5\8" : @"poketool\trgra\trfgra.narc");
    public string PokemonSpritesPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\0\4" : IsPlatinum ? @"poketool\pokegra\pl_pokegra.narc" : @"poketool\pokegra\pokegra.narc");
    public string PokemonFormSpritesPath => Path.Combine(FilesRoot, IsHgss ? @"a\1\1\4" : IsPlatinum ? @"poketool\pokegra\pl_otherpoke.narc" : @"poketool\pokegra\otherpoke.narc");
    public string ItemIconsPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\1\8" : @"itemtool\itemdata\item_icon.narc");
    public string FontPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\1\6" : IsPlatinum ? @"graphic\pl_font.narc" : @"graphic\font.narc");
    public string ScriptsPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\1\2" : @"fielddata\script\scr_seq.narc");
    public string EventsPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\3\2" : @"fielddata\eventdata\zone_event.narc");
    public string MatrixPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\4\1" : @"fielddata\mapmatrix\map_matrix.narc");
    public string LandPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\6\5" : @"fielddata\land_data\land_data.narc");
    public string PersonalPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\0\2" : IsPlatinum ? @"poketool\personal\pl_personal.narc" : @"poketool\personal\personal.narc");
    public string TextArchivePath => Path.Combine(FilesRoot, IsHgss ? @"a\0\2\7" : IsPlatinum ? @"msgdata\pl_msg.narc" : @"msgdata\msg.narc");
    public string EncountersPath => Path.Combine(FilesRoot, Game switch
    {
        BaseGame.HeartGold => @"a\0\3\7",
        BaseGame.SoulSilver => @"a\1\3\6",
        BaseGame.Platinum => @"fielddata\encountdata\pl_enc_data.narc",
        BaseGame.Diamond => @"fielddata\encountdata\d_enc_data.narc",
        _ => @"fielddata\encountdata\p_enc_data.narc",
    });
    public string MapNamesPath => Path.Combine(FilesRoot, @"fielddata\maptable\mapname.bin");

    public int TrainerNamesBank => IsHgss ? (Japanese ? 719 : 729) : IsPlatinum ? 618 : (Japanese ? 550 : 559);
    public int TrainerClassesBank => IsHgss ? (Japanese ? 720 : 730) : IsPlatinum ? 619 : (Japanese ? 551 : 560);
    public int SpeciesBank => IsHgss ? (Japanese ? 232 : 237) : IsPlatinum ? 412 : 362;
    public int ItemsBank => IsHgss ? (Japanese ? 219 : 222) : IsPlatinum ? 392 : (Japanese ? 341 : 344);
    public int MovesBank => IsHgss ? (Japanese ? 739 : 750) : IsPlatinum ? 647 : 588;
    public int AbilitiesBank => IsHgss ? 720 : IsPlatinum ? 610 : 552; // GEN5: tabelas diferentes
    public int LocationNamesBank => IsHgss ? (Japanese ? 272 : 279) : IsPlatinum ? 433 : 382;

    /// <summary>Offset da tabela de cabeçalhos de mapa dentro do arm9 descomprimido (24 bytes por cabeçalho). Fonte: DSPRE.</summary>
    public uint? HeaderTableOffset => (Game, Language) switch
    {
        (BaseGame.Diamond or BaseGame.Pearl, Gen4Language.English) => 0xEEDBC,
        (BaseGame.Diamond or BaseGame.Pearl, Gen4Language.Spanish) => 0xEEE08,
        (BaseGame.Diamond or BaseGame.Pearl, Gen4Language.Italian) => 0xEED70,
        (BaseGame.Diamond or BaseGame.Pearl, Gen4Language.French) => 0xEEDFC,
        (BaseGame.Diamond or BaseGame.Pearl, Gen4Language.German) => 0xEEDCC,
        (BaseGame.Diamond, Gen4Language.Japanese) => 0xF0D68,
        (BaseGame.Pearl, Gen4Language.Japanese) => 0xF0D6C,
        (BaseGame.Platinum, Gen4Language.English) => 0xE601C,
        (BaseGame.Platinum, Gen4Language.Spanish) => 0xE60B0,
        (BaseGame.Platinum, Gen4Language.Italian) => 0xE6038,
        (BaseGame.Platinum, Gen4Language.French) => 0xE60A4,
        (BaseGame.Platinum, Gen4Language.German) => 0xE6074,
        (BaseGame.Platinum, Gen4Language.Japanese) => 0xE56F0,
        (BaseGame.HeartGold or BaseGame.SoulSilver, Gen4Language.English) => 0xF6BE0,
        (BaseGame.HeartGold, Gen4Language.Spanish) => 0xF6BC8,
        (BaseGame.SoulSilver, Gen4Language.Spanish) => 0xF6BD0,
        (BaseGame.HeartGold or BaseGame.SoulSilver, Gen4Language.Italian) => 0xF6B58,
        (BaseGame.HeartGold or BaseGame.SoulSilver, Gen4Language.French) => 0xF6BC4,
        (BaseGame.HeartGold or BaseGame.SoulSilver, Gen4Language.German) => 0xF6B94,
        (BaseGame.HeartGold or BaseGame.SoulSilver, Gen4Language.Japanese) => 0xF6390,
        _ => null,
    };

    public static Gen4Language LanguageFromGameCode(string gameCode) => gameCode.Length < 4 ? Gen4Language.Unknown : gameCode[3] switch
    {
        'E' or 'O' => Gen4Language.English, 'S' => Gen4Language.Spanish, 'I' => Gen4Language.Italian,
        'F' => Gen4Language.French, 'D' => Gen4Language.German, 'J' => Gen4Language.Japanese, 'K' => Gen4Language.Korean,
        _ => Gen4Language.Unknown,
    };

    public static Gen4Game For(Project p)
    {
        if (p.Config.Generation != 4) throw new NotSupportedException(Loc.T("core.gen4.notGen4")); // GEN5
        var ws = p.Paths.Workspace;
        bool dsrom = Directory.Exists(Path.Combine(ws, "files"));
        var filesRoot = dsrom ? Path.Combine(ws, "files")
                      : Directory.Exists(Path.Combine(ws, "data")) ? Path.Combine(ws, "data")
                      : throw new DirectoryNotFoundException(Loc.T("core.gen4.noFiles"));
        return new Gen4Game
        {
            Game = p.Config.BaseGame,
            Language = LanguageFromGameCode(p.Config.GameCode),
            FilesRoot = filesRoot,
            Arm9Path = dsrom ? Path.Combine(ws, "arm9", "arm9.bin") : Path.Combine(ws, "arm9.bin"),
        };
    }
}
