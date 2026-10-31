using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using PokeHub.App.ViewModels;
using PokeHub.Core.Gen4.Scripts;

namespace PokeHub.App.Views;

public partial class NewEventWindow : Window
{
    private const int Scale = 8;
    private readonly NewEventViewModel _vm;

    public NewEventWindow(NewEventViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        vm.LayoutChanged += Draw;
        Closed += (_, _) => vm.LayoutChanged -= Draw;
        Draw();
    }

    private void Draw()
    {
        foreach (var old in LayoutCanvas.Children.OfType<FrameworkElement>().Where(c => c != LayoutImageView).ToList()) LayoutCanvas.Children.Remove(old);
        var layout = _vm.Layout;
        LayoutImageView.Width = LayoutCanvas.Width = (layout?.Width ?? 0) * Scale;
        LayoutImageView.Height = LayoutCanvas.Height = (layout?.Height ?? 0) * Scale;
        if (layout is null) return;

        foreach (var m in layout.Markers.Where(m => m.Kind == EventKind.Npc))
        {
            if (m.SpriteId is { } id && _vm.SpriteOf(id) is { } sprite)
            {
                var image = new Image { Source = sprite, Width = Scale * 2, Height = Scale * 2, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(image, m.X * Scale - Scale / 2);
                Canvas.SetTop(image, m.Y * Scale - Scale);
                LayoutCanvas.Children.Add(image);
            }
        }
        if (_vm.Tile is { } tile)
        {
            var mark = new Rectangle { Width = Scale + 4, Height = Scale + 4, Stroke = Brushes.OrangeRed, StrokeThickness = 2, Fill = Brushes.Transparent, IsHitTestVisible = false };
            Canvas.SetLeft(mark, tile.X * Scale - 2);
            Canvas.SetTop(mark, tile.Y * Scale - 2);
            LayoutCanvas.Children.Add(mark);
        }
    }

    private void LayoutCanvas_Click(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(LayoutCanvas);
        _vm.PickTile((int)(p.X / Scale), (int)(p.Y / Scale));
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
