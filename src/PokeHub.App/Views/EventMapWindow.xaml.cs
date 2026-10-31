using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PokeHub.App.ViewModels;
using PokeHub.Core.Gen4.Scripts;

namespace PokeHub.App.Views;

public partial class EventMapWindow : Window
{
    private static readonly IReadOnlyDictionary<EventKind, Brush> Colors = new Dictionary<EventKind, Brush>
    {
        [EventKind.Npc] = Frozen(0xE0, 0x40, 0x3A),
        [EventKind.Trigger] = Frozen(0xE8, 0xB8, 0x20, 110),
        [EventKind.Sign] = Frozen(0x30, 0x70, 0xE0),
        [EventKind.Warp] = Frozen(0xA0, 0x40, 0xC0),
    };

    private static readonly Brush SelectedStroke = Frozen(0xFF, 0x7A, 0x00);

    private readonly EventMapViewModel _vm;

    public EventMapWindow(EventMapViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        vm.LayoutChanged += DrawLayout;
        Closed += (_, _) => vm.LayoutChanged -= DrawLayout;
        DrawLayout();
    }

    private static Brush Frozen(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Redesenha só os marcadores; a imagem da planta vem do binding.</summary>
    private void DrawLayout()
    {
        foreach (var old in LayoutCanvas.Children.OfType<FrameworkElement>().Where(c => c != LayoutImageView).ToList()) LayoutCanvas.Children.Remove(old);
        var layout = _vm.Layout;
        int scale = EventMapViewModel.TileScale;
        LayoutImageView.Width = LayoutCanvas.Width = (layout?.Width ?? 0) * scale;
        LayoutImageView.Height = LayoutCanvas.Height = (layout?.Height ?? 0) * scale;
        if (layout is null) return;

        Rectangle? selected = null;
        // gatilhos cobrem áreas: vão por baixo para não esconder o que está em cima deles
        foreach (var m in layout.Markers.OrderBy(m => m.Kind == EventKind.Trigger ? 0 : 1))
        {
            bool isSelected = _vm.IsSelected(m);
            // o selecionado cresce para fora do tile: num marcador de 8 px, a borda cobriria a cor toda
            int grow = isSelected ? 5 : 0;
            var mark = new Rectangle
            {
                Width = m.Width * scale + grow * 2,
                Height = m.Height * scale + grow * 2,
                Fill = Colors[m.Kind],
                Stroke = isSelected ? SelectedStroke : Brushes.Black,
                StrokeThickness = isSelected ? 3 : 1,
                ToolTip = _vm.MarkerTip(m),
                Cursor = Cursors.Hand,
            };
            mark.MouseLeftButtonDown += (_, e) => { _vm.ActivateMarker(m); e.Handled = true; };
            Canvas.SetLeft(mark, m.X * scale - grow);
            Canvas.SetTop(mark, m.Y * scale - grow);
            LayoutCanvas.Children.Add(mark);
            if (_vm.NpcSprite(m) is { } sprite)
            {
                // o sprite tem o dobro do tile e fica de pé sobre ele, como no jogo
                var image = new Image { Source = sprite, Width = scale * 2, Height = scale * 2, ToolTip = mark.ToolTip, Cursor = Cursors.Hand };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                image.MouseLeftButtonDown += (_, e) => { _vm.ActivateMarker(m); e.Handled = true; };
                Canvas.SetLeft(image, m.X * scale - scale / 2);
                Canvas.SetTop(image, m.Y * scale - scale);
                LayoutCanvas.Children.Add(image);
            }
            if (isSelected) selected = mark;
        }
        if (selected is null) return;
        Panel.SetZIndex(selected, 1);
        // o marcador recém-criado ainda não tem posição: a rolagem precisa esperar o layout
        Dispatcher.BeginInvoke(selected.BringIntoView, DispatcherPriority.Loaded);
    }
}
