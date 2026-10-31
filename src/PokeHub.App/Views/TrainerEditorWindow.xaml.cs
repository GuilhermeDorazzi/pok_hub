using System.ComponentModel;
using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class TrainerEditorWindow : Window
{
    public TrainerEditorWindow(TrainerEditorViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (DataContext is not TrainerEditorViewModel vm || !vm.IsDirty) return;
        var r = MessageBox.Show(this, "Há alterações não salvas. Fechar mesmo assim?", "Editor de treinadores",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) e.Cancel = true;
    }
}
