using System.Globalization;
using AxisApp.ViewModels;

namespace AxisApp.Converters;

public class SelectedNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string name)
            return BindableProperty.UnsetValue;

        var selected = string.Equals(values[1]?.ToString(), parameter?.ToString() ?? "True", StringComparison.Ordinal);
        return A11y.Selectable(name, selected);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
