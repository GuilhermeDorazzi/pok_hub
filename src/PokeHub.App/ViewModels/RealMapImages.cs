using System.Windows.Media.Imaging;
using PokeHub.Core.Gen4;
using PokeHub.Core.Gen4.Scripts;

namespace PokeHub.App.ViewModels;

/// <summary>Mapa real para as telas com planta: desenha fora da tela, um por vez, e guarda o que já desenhou.</summary>
public sealed class RealMapImages(Gen4Game game)
{
    private readonly RealMapRenderer? _renderer = RealMapRenderer.TryLoad(game);
    private readonly Dictionary<int, BitmapSource?> _images = [];
    private readonly object _drawing = new();

    public bool Available => _renderer is not null;

    /// <summary>A imagem que já está pronta, sem esperar. Mapa que não deu para desenhar também volta null.</summary>
    public BitmapSource? Known(int mapId) => _images.GetValueOrDefault(mapId);

    /// <summary>null se este mapa não puder ser desenhado.</summary>
    public async Task<BitmapSource?> GetAsync(MapLayout layout, int mapId, int areaId)
    {
        if (_renderer is not { } renderer) return null;
        if (_images.TryGetValue(mapId, out var known)) return known;
        var drawn = await Task.Run(() => { lock (_drawing) return renderer.Render(layout, areaId); });
        return _images[mapId] = drawn?.ToBitmap();
    }
}
