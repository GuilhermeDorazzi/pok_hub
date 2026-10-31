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
    private readonly Dictionary<int, BitmapSource?> _cache = [];

    public IReadOnlyList<SpeciesOption> Options { get; }

    public SpeciesCatalog(IReadOnlyList<string> names, PokemonSprites? sprites)
    {
        _sprites = sprites;
        Options = names.Select((name, id) => new SpeciesOption(id, name, this)).ToList();
    }

    public BitmapSource? Sprite(int species)
    {
        if (!_cache.TryGetValue(species, out var bitmap))
            _cache[species] = bitmap = _sprites?.Render(species)?.ToBitmap();
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
