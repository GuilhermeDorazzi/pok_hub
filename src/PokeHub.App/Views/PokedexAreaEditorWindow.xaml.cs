using System.ComponentModel;
using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class PokedexAreaEditorWindow : Window
{
    public PokedexAreaEditorWindow(PokedexAreaEditorViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (DataContext is not PokedexAreaEditorViewModel vm || !vm.IsDirty) return;
        var r = MessageBox.Show(this, "Há alterações não salvas. Fechar mesmo assim?", "Editor de áreas da Pokédex",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) e.Cancel = true;
    }
}
