using System.IO;
using System.Windows.Media.Imaging;
using PokeHub.Core.Gen4;

namespace PokeHub.App.ViewModels;

/// <summary>Uma linha do seletor. A imagem só é decodificada quando a linha aparece na lista.</summary>
public sealed class SpeciesOption(int id, string name, SpeciesCatalog catalog)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string Label => $"#{Id:D3} {Name}";
    public BitmapSource? Sprite => catalog.Sprite(Id);
    /// <summary>O que leitores de tela e a automação de interface leem em cada linha.</summary>
    public override string ToString() => Label;
}

/// <summary>Espécies do jogo com nome e sprite, compartilhadas por todos os seletores de uma janela.</summary>
public sealed class SpeciesCatalog
{
    private readonly PokemonSprites? _sprites;
    private readonly Dictionary<(int Species, int Form, bool Female), BitmapSource?> _cache = [];

    public IReadOnlyList<SpeciesOption> Options { get; }

    public SpeciesCatalog(IReadOnlyList<string> names, PokemonSprites? sprites)
    {
        _sprites = sprites;
        Options = names.Select((name, id) => new SpeciesOption(id, name, this)).ToList();
    }

    /// <summary>Catálogo direto de um projeto, para janelas que não carregam os dados do jogo. null se os arquivos não estiverem lá.</summary>
    public static SpeciesCatalog? TryForProject(PokeHub.Core.Projects.Project project)
    {
        try
        {
            var game = Gen4Game.For(project);
            var text = Narc.Read(System.IO.File.ReadAllBytes(game.TextArchivePath));
            var names = Gen4TextArchive.Read(text.Files[game.SpeciesBank]).Messages.Select(m => Gen4TextArchive.Decode(m)).ToList();
            return new SpeciesCatalog(names, PokemonSprites.Load(game));
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public BitmapSource? Sprite(int species, int form = 0, bool female = false)
    {
        var key = (species, form, female);
        if (!_cache.TryGetValue(key, out var bitmap))
            _cache[key] = bitmap = _sprites?.Render(species, form, female)?.ToBitmap();
        return bitmap;
    }

    public string LabelOf(int species) => species >= 0 && species < Options.Count ? Options[species].Label : $"#{species:D3}";

    /// <summary>Por parte do nome ou pelo número da espécie, na ordem da Pokédex.</summary>
    public List<SpeciesOption> Search(string text, bool hideEmpty)
    {
        var query = text.Trim().TrimStart('#');
        bool isNumber = int.TryParse(query, out var number);
        return Options.Where(o => (!hideEmpty || (o.Id > 0 && o.Name.Length > 0))
            && (query.Length == 0 || o.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (isNumber && o.Id == number))).ToList();
    }
}
