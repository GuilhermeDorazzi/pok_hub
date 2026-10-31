using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class EventMapWindow : Window
{
    public EventMapWindow(EventMapViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
