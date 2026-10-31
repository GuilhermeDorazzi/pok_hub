using PokeHub.Core.Projects;
using PokeHub.Core.Rom;

namespace PokeHub.Core.Gen4;

/// <summary>Onde cada dado de Gen 4 fica dentro do workspace, por jogo e idioma. Fonte: RomInfo do DSPRE.</summary>
public sealed class Gen4Game
{
    public required BaseGame Game { get; init; }
    public required bool Japanese { get; init; }
    /// <summary>Raiz dos arquivos da ROM: workspace/files (ds-rom) ou workspace/data (ndstool).</summary>
    public required string FilesRoot { get; init; }

    public string TrainerDataPath => Path.Combine(FilesRoot, Game == BaseGame.HeartGold || Game == BaseGame.SoulSilver ? @"a\0\5\5" : @"poketool\trainer\trdata.narc");
    public string TrainerPartyPath => Path.Combine(FilesRoot, IsHgss ? @"a\0\5\6" : @"poketool\trainer\trpoke.narc");
    public string TextArchivePath => Path.Combine(FilesRoot, IsHgss ? @"a\0\2\7" : Game == BaseGame.Platinum ? @"msgdata\pl_msg.narc" : @"msgdata\msg.narc");

    public bool IsHgss => Game is BaseGame.HeartGold or BaseGame.SoulSilver;
    public bool IsDp => Game is BaseGame.Diamond or BaseGame.Pearl;
    /// <summary>DP não guarda o ball seal por Pokémon de treinador; Pt e HGSS guardam (u16 no fim).</summary>
    public bool HasBallSeal => !IsDp;

    public int TrainerNamesBank => IsHgss ? (Japanese ? 719 : 729) : Game == BaseGame.Platinum ? 618 : (Japanese ? 550 : 559);
    public int TrainerClassesBank => IsHgss ? (Japanese ? 720 : 730) : Game == BaseGame.Platinum ? 619 : (Japanese ? 551 : 560);
    public int SpeciesBank => IsHgss ? (Japanese ? 232 : 237) : Game == BaseGame.Platinum ? 412 : 362;
    public int ItemsBank => IsHgss ? (Japanese ? 219 : 222) : Game == BaseGame.Platinum ? 392 : (Japanese ? 341 : 344);
    public int MovesBank => IsHgss ? (Japanese ? 739 : 750) : Game == BaseGame.Platinum ? 647 : 588;
    public int AbilitiesBank => IsHgss ? 720 : Game == BaseGame.Platinum ? 610 : 552; // GEN5: tabelas diferentes

    public static Gen4Game For(Project p)
    {
        if (p.Config.Generation != 4) throw new NotSupportedException("Só Gen 4 por enquanto."); // GEN5
        var ws = p.Paths.Workspace;
        var filesRoot = Directory.Exists(Path.Combine(ws, "files")) ? Path.Combine(ws, "files")
                      : Directory.Exists(Path.Combine(ws, "data")) ? Path.Combine(ws, "data")
                      : throw new DirectoryNotFoundException("workspace/ não tem files/ nem data/: extraia a ROM primeiro.");
        return new Gen4Game
        {
            Game = p.Config.BaseGame,
            Japanese = p.Config.GameCode.Length >= 4 && p.Config.GameCode[3] == 'J',
            FilesRoot = filesRoot,
        };
    }
}
