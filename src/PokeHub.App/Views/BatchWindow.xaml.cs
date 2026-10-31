using System.Windows;
using PokeHub.App.ViewModels;

namespace PokeHub.App.Views;

public partial class BatchWindow : Window
{
    public BatchViewModel ViewModel { get; }

    public BatchWindow(BatchViewModel vm)
    {
        ViewModel = vm;
        DataContext = vm;
        InitializeComponent();
    }
}
