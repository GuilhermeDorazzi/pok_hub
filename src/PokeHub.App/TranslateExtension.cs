using System.Windows.Data;
using System.Windows.Markup;
using PokeHub.Core.Localization;

namespace PokeHub.App;

/// <summary>{loc:T chave} no XAML: liga ao dicionário atual e atualiza quando o idioma muda.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; }

    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
