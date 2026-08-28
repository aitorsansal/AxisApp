using System.Globalization;
using Microsoft.Maui.Controls.Xaml;

namespace AxisApp.Localization;

/// <summary>XAML usage: Text="{loc:Translate Groups_YourGroups}". Binds to
/// LocalizationResourceManager.CurrentLanguage — an ordinary notified property, not an indexer —
/// through a converter that resolves Key against whatever language value it's handed. See
/// LocalizationResourceManager's remarks for why this shape was chosen over an indexer binding.</summary>
[ContentProperty(nameof(Key))]
public class TranslateExtension : IMarkupExtension<BindingBase>
{
    public string Key { get; set; } = "";

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding(
        nameof(LocalizationResourceManager.CurrentLanguage),
        BindingMode.OneWay,
        source: LocalizationResourceManager.Instance,
        converter: new TranslateConverter(Key));

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}

/// <summary>Ignores the incoming language string's identity beyond using it as the lookup
/// culture — the binding exists only so a CurrentLanguage change re-invokes this converter.</summary>
internal sealed class TranslateConverter(string key) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AppStrings.Get(key, value as string ?? AppStrings.English);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
