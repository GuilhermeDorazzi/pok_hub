using System.ComponentModel;
using System.Windows;
using PokeHub.App.ViewModels;
using PokeHub.Core.Localization;

namespace PokeHub.App.Views;

public partial class ScriptEditorWindow : Window
{
    private readonly ScriptEditorViewModel _vm;

    public ScriptEditorWindow(ScriptEditorViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_vm.IsDirty) return;
        var answer = MessageBox.Show(this, Loc.T("common.closeConfirm"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) e.Cancel = true;
    }
}
