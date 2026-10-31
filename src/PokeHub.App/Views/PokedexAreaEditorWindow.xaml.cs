using System.ComponentModel;
using System.Windows;
using PokeHub.App.ViewModels;
using PokeHub.Core.Localization;

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
        var r = MessageBox.Show(this, Loc.T("common.closeConfirm"), Loc.T("dx.title"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) e.Cancel = true;
    }
}
