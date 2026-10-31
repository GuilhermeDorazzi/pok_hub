using PokeHub.Core.Graphics;

namespace PokeHub.Core.Gen4;

/// <summary>
/// Sprites frontais de treinador: 5 arquivos por classe no NARC (NCGR, NCLR, NCER, NANR e um NCGR "NCBR" com a imagem inteira).
/// Usa o NCBR, que não precisa montar células.
/// </summary>
public sealed class TrainerSprites
{
    private const int FilesPerClass = 5;
    private readonly Narc _narc;

    private TrainerSprites(Narc narc) => _narc = narc;

    /// <summary>null se o jogo não tiver o arquivo: o editor segue funcionando, só sem a imagem.</summary>
    public static TrainerSprites? Load(Gen4Game game) =>
        File.Exists(game.TrainerGraphicsPath) ? new TrainerSprites(Narc.Read(File.ReadAllBytes(game.TrainerGraphicsPath))) : null;

    /// <summary>Primeiro quadro da classe. null se a classe não tem sprite ou o arquivo não está no formato esperado.</summary>
    public RgbaImage? Render(int trainerClass)
    {
        int first = trainerClass * FilesPerClass;
        if (trainerClass < 0 || first + FilesPerClass > _narc.Files.Count) return null;
        return SpriteStrip.TryDecode(_narc.Files[first + 4], _narc.Files[first + 1]);
    }
}
