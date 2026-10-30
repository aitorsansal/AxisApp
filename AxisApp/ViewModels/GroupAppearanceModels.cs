using AxisApp.Localization;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AxisApp.ViewModels;

/// <summary>One swatch in a group color picker (NewGroupPage, GroupDetailPage's "Edit
/// appearance" overlay) — same shape as ProfileViewModel's AccentSwatch, kept separate since
/// group color and the per-device accent theme are unrelated concerns that happen to share a
/// palette (Services/AccentPalettes.cs).</summary>
public partial class GroupColorSwatch : ObservableObject
{
    public AccentPreset Preset { get; init; }
    public Color Color { get; init; } = Colors.Transparent;

    /// <summary>TalkBack name: the colour name plus its selected state (the tile itself is only colour/glyph).</summary>
    public string A11yName => A11y.Selectable(LocalizationResourceManager.Instance[$"GroupColor_{Preset}"], IsSelected);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(A11yName))] private bool isSelected;
}

/// <summary>One tile in a group icon picker — Key is what actually gets stored (Group.Icon),
/// Glyph is what gets rendered (AppConstants.GroupIcons).</summary>
public partial class GroupIconOption : ObservableObject
{
    public string Key { get; init; } = "";
    public string Glyph { get; init; } = "";

    /// <summary>TalkBack name: the icon name plus its selected state (the tile itself is only colour/glyph).</summary>
    public string A11yName => A11y.Selectable(LocalizationResourceManager.Instance[$"GroupIcon_{Key}"], IsSelected);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(A11yName))] private bool isSelected;
}

/// <summary>Screen-reader helpers shared by the picker tiles.</summary>
public static class A11y
{
    public static string Selectable(string name, bool isSelected) =>
        isSelected ? $"{name}, {LocalizationResourceManager.Instance["Common_Selected"]}" : name;
}
