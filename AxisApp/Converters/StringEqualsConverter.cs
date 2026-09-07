using System.Globalization;

namespace AxisApp.Converters;

/// <summary>Compares the bound string to ConverterParameter — for wiring Juice.Selected onto a
/// chip whose "selected" state is a string equality (e.g. MyResponse == "going"), not a plain bool.</summary>
public class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && parameter is string p && s == p;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
