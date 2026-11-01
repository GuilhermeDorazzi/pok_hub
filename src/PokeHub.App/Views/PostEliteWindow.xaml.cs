using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class PostEliteWindow : Window
{
    public PostEliteWindow(PostEliteViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
}
