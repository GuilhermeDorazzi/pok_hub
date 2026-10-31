using System.Windows;
using System.Windows.Controls;
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

    /// <summary>O erro do compilador vem por último: sem rolar, ficaria fora de vista.</summary>
    private void Output_TextChanged(object sender, TextChangedEventArgs e) => ((TextBox)sender).ScrollToEnd();
}
