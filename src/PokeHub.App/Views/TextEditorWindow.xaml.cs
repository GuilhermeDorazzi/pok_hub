using System.ComponentModel;
using System.Windows;
using PokeHub.App.ViewModels;
using PokeHub.Core.Localization;

namespace PokeHub.App.Views;

public partial class TextEditorWindow : Window
{
    public TextEditorWindow(TextEditorViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Editor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is TextEditorViewModel vm) vm.ApplyCommand.Execute(null);
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (DataContext is not TextEditorViewModel vm || !vm.IsDirty) return;
        var r = MessageBox.Show(this, Loc.T("common.closeConfirm"), Loc.T("txt.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) e.Cancel = true;
    }

    /// <summary>Janela aberta com Show(): IsCancel não fecha, então o botão fecha explicitamente (Closing confirma se houver edição).</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
