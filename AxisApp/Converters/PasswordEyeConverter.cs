using System.Globalization;
using AxisApp;

namespace AxisApp.Converters;

/// <summary>Picks the Lucide glyph (AppConstants.Icons.Eye/EyeOff) for a password-visibility
/// toggle — the crossed-out eye means "currently visible, tap to hide", the open eye means
/// "currently masked, tap to show". Pair with FontFamily="Lucide" on the consuming Label.</summary>
public class PasswordEyeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? AppConstants.Icons.EyeOff : AppConstants.Icons.Eye;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>TalkBack name for the same toggle: what tapping it will do, following its state.</summary>
public class PasswordToggleNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Localization.LocalizationResourceManager.Instance[value is true ? "Common_HidePassword" : "Common_ShowPassword"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
