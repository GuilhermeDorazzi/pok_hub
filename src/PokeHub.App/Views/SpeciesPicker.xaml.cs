using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PokeHub.App.ViewModels;
using PokeHub.Core.Localization;

namespace PokeHub.App.Views;

/// <summary>Seletor de espécie com sprite e busca. A lista só é montada ao abrir, porque uma janela de encontros tem dezenas destes.</summary>
public partial class SpeciesPicker : UserControl
{
    public static readonly DependencyProperty CatalogProperty = DependencyProperty.Register(nameof(Catalog), typeof(SpeciesCatalog), typeof(SpeciesPicker),
        new PropertyMetadata(null, (d, _) => ((SpeciesPicker)d).ShowSelected()));

    /// <summary>Espécie escolhida; -1 quando ainda não há escolha.</summary>
    public static readonly DependencyProperty SelectedSpeciesProperty = DependencyProperty.Register(nameof(SelectedSpecies), typeof(int), typeof(SpeciesPicker),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SpeciesPicker)d).ShowSelected()));

    public static readonly DependencyProperty HideEmptyProperty = DependencyProperty.Register(nameof(HideEmpty), typeof(bool), typeof(SpeciesPicker), new PropertyMetadata(false));

    public SpeciesCatalog? Catalog { get => (SpeciesCatalog?)GetValue(CatalogProperty); set => SetValue(CatalogProperty, value); }
    public int SelectedSpecies { get => (int)GetValue(SelectedSpeciesProperty); set => SetValue(SelectedSpeciesProperty, value); }
    /// <summary>Tira da lista a espécie 0 (espaço vazio) onde ela não faz sentido.</summary>
    public bool HideEmpty { get => (bool)GetValue(HideEmptyProperty); set => SetValue(HideEmptyProperty, value); }

    public SpeciesPicker() => InitializeComponent();

    private void ShowSelected()
    {
        bool none = Catalog is null || SelectedSpecies < 0;
        Label.Text = none ? Loc.T("picker.choose") : Catalog!.LabelOf(SelectedSpecies);
        Thumb.Source = none ? null : Catalog!.Sprite(SelectedSpecies);
    }

    private void Drop_Opened(object? sender, EventArgs e)
    {
        Toggle.IsHitTestVisible = false; // com o StaysOpen falso, clicar no botão fecharia e reabriria na mesma hora
        Search.Text = "";
        Fill();
        if (List.SelectedItem is { } current) List.ScrollIntoView(current);
        Dispatcher.BeginInvoke(() => Search.Focus(), DispatcherPriority.Input);
    }

    private void Drop_Closed(object? sender, EventArgs e)
    {
        Toggle.IsHitTestVisible = true;
        List.ItemsSource = null;
    }

    private void Fill()
    {
        if (Catalog is null) return;
        var options = Catalog.Search(Search.Text, HideEmpty);
        List.ItemsSource = options;
        List.SelectedItem = options.FirstOrDefault(o => o.Id == SelectedSpecies) ?? options.FirstOrDefault();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Drop.IsOpen) Fill();
    }

    private void Search_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Move(1); break;
            case Key.Up: Move(-1); break;
            case Key.Enter: Commit(List.SelectedItem as SpeciesOption); break;
            case Key.Escape: Toggle.IsChecked = false; Toggle.Focus(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Move(int delta)
    {
        if (List.Items.Count == 0) return;
        List.SelectedIndex = Math.Clamp(List.SelectedIndex + delta, 0, List.Items.Count - 1);
        List.ScrollIntoView(List.SelectedItem);
    }

    private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // a barra de rolagem também passa por aqui: só vale o clique em cima de uma linha
        if (ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: SpeciesOption option })
            Commit(option);
    }

    private void Commit(SpeciesOption? option)
    {
        if (option is null) return;
        SelectedSpecies = option.Id;
        Toggle.IsChecked = false;
        Toggle.Focus();
    }
}
