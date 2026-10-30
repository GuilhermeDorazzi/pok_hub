using System.Windows;
using System.Windows.Controls;

namespace PokeHub.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void Log_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb) tb.ScrollToEnd();
    }
}
