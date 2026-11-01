using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class GameFaceWindow : Window
{
    public GameFaceWindow(GameFaceViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
