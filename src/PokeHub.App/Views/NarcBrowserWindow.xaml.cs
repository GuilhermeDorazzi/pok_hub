using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class NarcBrowserWindow : Window
{
    public NarcBrowserWindow(NarcBrowserViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
