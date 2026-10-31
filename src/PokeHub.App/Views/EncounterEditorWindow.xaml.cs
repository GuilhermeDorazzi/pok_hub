using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using PokeHub.App.ViewModels;
using PokeHub.Core.Gen4;
using PokeHub.Core.Localization;

namespace PokeHub.App.Views;

public partial class EncounterEditorWindow : Window
{
    public EncounterEditorWindow(EncounterEditorViewModel vm)
    {
        Resources["EntryLabel"] = new EntryLabelConverter(vm);
        InitializeComponent();
        DataContext = vm;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (DataContext is not EncounterEditorViewModel vm || !vm.IsDirty) return;
        var r = MessageBox.Show(this, Loc.T("common.closeConfirm"), Loc.T("enc.title"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) e.Cancel = true;
    }

    /// <summary>Rótulo da lista: id, locais e nomes internos dos mapas que usam o arquivo.</summary>
    private sealed class EntryLabelConverter(EncounterEditorViewModel vm) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is EncounterEntry e ? vm.LabelOf(e) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
