using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class PatchHooksWindow : Window
{
    public PatchHooksWindow(PatchHooksViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
