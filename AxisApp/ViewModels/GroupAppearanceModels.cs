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

    [ObservableProperty] private bool isSelected;
}

/// <summary>One tile in a group icon picker — Key is what actually gets stored (Group.Icon),
/// Glyph is what gets rendered (AppConstants.GroupIcons).</summary>
public partial class GroupIconOption : ObservableObject
{
    public string Key { get; init; } = "";
    public string Glyph { get; init; } = "";

    [ObservableProperty] private bool isSelected;
}
