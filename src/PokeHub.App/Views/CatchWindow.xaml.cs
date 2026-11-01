using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class CatchWindow : Window
{
    public CatchWindow(CatchViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
