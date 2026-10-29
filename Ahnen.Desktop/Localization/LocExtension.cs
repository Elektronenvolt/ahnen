using System.Windows.Data;
using System.Windows.Markup;

namespace Ahnen.Desktop.Localization;

/// <summary>XAML usage: <c>Text="{loc:Loc Nav_Tree}"</c>. Re-evaluates when the language changes.</summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Localizer.Instance, Mode = BindingMode.OneWay }
            .ProvideValue(serviceProvider);
}
