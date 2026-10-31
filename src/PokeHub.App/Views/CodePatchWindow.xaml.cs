using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class CodePatchWindow : Window
{
    public CodePatchWindow(CodePatchViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
