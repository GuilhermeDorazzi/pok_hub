using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PokeHub.App.ViewModels;
using PokeHub.Core.Gen4;
using PokeHub.Core.Graphics;
using PokeHub.Core.Localization;

namespace PokeHub.App.Views;

public partial class PokedexAreaEditorWindow : Window
{
    private readonly PokedexAreaEditorViewModel _vm;
    private static readonly Brush SelectedFill = new SolidColorBrush(Color.FromArgb(150, 255, 210, 60));
    private static readonly Brush SelectedStroke = new SolidColorBrush(Color.FromArgb(255, 200, 120, 0));
    private static readonly Brush OtherStroke = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));

    public PokedexAreaEditorWindow(PokedexAreaEditorViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        if (vm.Map is { } map)
        {
            MapImage.Source = ToBitmap(map.Image);
            MapCanvas.Width = map.Image.Width;
            MapCanvas.Height = map.Image.Height;
            vm.MapChanged += DrawOverlay;
            DrawOverlay();
        }
    }

    /// <summary>RGBA do Core para um BitmapSource (WPF usa BGRA).</summary>
    private static BitmapSource ToBitmap(RgbaImage img)
    {
        var bgra = new byte[img.Pixels.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = img.Pixels[i + 2]; bgra[i + 1] = img.Pixels[i + 1]; bgra[i + 2] = img.Pixels[i]; bgra[i + 3] = img.Pixels[i + 3];
        }
        var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, bgra, img.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Desenha as áreas do bloco mostrado: marcadas em amarelo, as outras só com contorno fino.</summary>
    private void DrawOverlay()
    {
        MapCanvas.Children.Clear();
        if (_vm.Map is not { } map || _vm.SelectedMapBlock is not { } block) return;
        bool special = PokedexAreaRepository.IsSpecial(block.Block);
        const int t = PokedexAreaMap.Tile;
        foreach (var area in block.Areas)
        {
            if (map.Shape(area.Location.Id, special) is not { } s) continue;
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    if (!s.Covers(s.X + x, s.Y + y)) continue;
                    var r = new Rectangle
                    {
                        Width = t, Height = t,
                        Fill = area.IsSelected ? SelectedFill : Brushes.Transparent,
                        Stroke = area.IsSelected ? SelectedStroke : OtherStroke,
                        StrokeThickness = area.IsSelected ? 1 : 0.5,
                        IsHitTestVisible = false,
                        ToolTip = area.Name,
                    };
                    Canvas.SetLeft(r, (s.X + x) * t);
                    Canvas.SetTop(r, (s.Y + y) * t);
                    MapCanvas.Children.Add(r);
                }
        }
    }

    private (int X, int Y) TileAt(MouseEventArgs e)
    {
        var p = e.GetPosition(MapCanvas);
        return ((int)(p.X / PokedexAreaMap.Tile), (int)(p.Y / PokedexAreaMap.Tile));
    }

    private void MapCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var (x, y) = TileAt(e);
        _vm.ToggleAreaAt(x, y);
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var (x, y) = TileAt(e);
        var name = _vm.AreaNameAt(x, y);
        HoverText.Text = name ?? (x < PokedexAreaMap.KantoFirstColumn ? Loc.T("dx.johto") : Loc.T("dx.kanto"));
        MapCanvas.Cursor = name is null ? Cursors.Arrow : Cursors.Hand;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (DataContext is not PokedexAreaEditorViewModel vm || !vm.IsDirty) return;
        var r = MessageBox.Show(this, Loc.T("common.closeConfirm"), Loc.T("dx.title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) e.Cancel = true;
    }
}
