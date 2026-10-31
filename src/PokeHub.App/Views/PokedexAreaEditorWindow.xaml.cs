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
    private static readonly Brush HoverStroke = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
    private uint? _hovered;

    public PokedexAreaEditorWindow(PokedexAreaEditorViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        if (vm.Map is { } map)
        {
            MapImage.Source = map.Image.ToBitmap();
            MapCanvas.Width = map.Image.Width;
            MapCanvas.Height = map.Image.Height;
            vm.MapChanged += DrawOverlay;
            DrawOverlay();
        }
    }

    /// <summary>Desenha as áreas marcadas do bloco mostrado (amarelo) e o contorno da área sob o mouse.</summary>
    private void DrawOverlay()
    {
        MapCanvas.Children.Clear();
        if (_vm.Map is not { } map || _vm.SelectedMapBlock is not { } block) return;
        bool special = PokedexAreaRepository.IsSpecial(block.Block);
        const int t = PokedexAreaMap.Tile;
        foreach (var area in block.Areas)
        {
            if (map.Shape(area.Location.Id, special) is not { } s) continue;
            bool hovered = _hovered == area.Location.Id;
            if (!area.IsSelected && !hovered) continue;
            var r = new Rectangle
            {
                Width = s.Width * t, Height = s.Height * t,
                Fill = area.IsSelected ? SelectedFill : Brushes.Transparent,
                Stroke = hovered ? HoverStroke : SelectedStroke,
                StrokeThickness = hovered ? 1.5 : 1,
                RadiusX = 1.5, RadiusY = 1.5,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(r, s.X * t);
            Canvas.SetTop(r, s.Y * t);
            MapCanvas.Children.Add(r);
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
        var (id, name) = _vm.AreaAt(x, y);
        HoverText.Text = name ?? "";
        MapCanvas.Cursor = name is null ? Cursors.Arrow : Cursors.Hand;
        if (id != _hovered) { _hovered = id; DrawOverlay(); }
    }

    private void MapCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        HoverText.Text = "";
        if (_hovered is not null) { _hovered = null; DrawOverlay(); }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (DataContext is not PokedexAreaEditorViewModel vm || !vm.IsDirty) return;
        var r = MessageBox.Show(this, Loc.T("common.closeConfirm"), Loc.T("dx.title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) e.Cancel = true;
    }

    /// <summary>Janela aberta com Show(): IsCancel não fecha, então o botão fecha explicitamente (Closing confirma se houver edição).</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
