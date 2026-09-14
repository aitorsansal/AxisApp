using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

public partial class NewGroupViewModel : BaseViewModel
{
    private readonly IGroupsRepository groupsRepository;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private bool isBusy;

    /// <summary>Optional — an unselected color defaults to Group.Color's own default (Blue) and
    /// an unselected icon just leaves the group with the initials-circle fallback. Not required
    /// to Create, unlike the currency picker, since this is cosmetic rather than a locked-in
    /// decision — see CHANGELOG.md's "Group appearance" entry.</summary>
    public ObservableCollection<GroupColorSwatch> ColorSwatches { get; }
    public ObservableCollection<GroupIconOption> IconOptions { get; }

    /// <summary>Display strings ("USD ($)") for the Picker — CurrencyOptions[i] corresponds to
    /// AppConstants.Currencies.All[i]. No entry is pre-selected (SelectedCurrencyIndex starts at
    /// -1, Picker's own default), so picking one is always a deliberate action — see
    /// /MULTI_CURRENCY_PLAN.md's "Decisions locked" section.</summary>
    public IReadOnlyList<string> CurrencyOptions { get; } =
        AppConstants.Currencies.All.Select(c => $"{c.Code} ({c.Symbol})").ToList();

    [ObservableProperty] private int selectedCurrencyIndex = -1;

    public NewGroupViewModel(IGroupsRepository groupsRepository)
    {
        this.groupsRepository = groupsRepository;

        ColorSwatches = new ObservableCollection<GroupColorSwatch>(Enum.GetValues<AccentPreset>().Select(preset =>
            new GroupColorSwatch
            {
                Preset = preset,
                Color = AccentPalettes.SwatchColor(preset),
                IsSelected = preset == AccentPreset.Blue,
            }));

        IconOptions = new ObservableCollection<GroupIconOption>(AppConstants.GroupIcons.All.Select(i =>
            new GroupIconOption { Key = i.Key, Glyph = i.Glyph }));
    }

    [RelayCommand]
    private void SelectColor(GroupColorSwatch swatch)
    {
        foreach (var s in ColorSwatches)
            s.IsSelected = s == swatch;
    }

    [RelayCommand]
    private void SelectIcon(GroupIconOption option)
    {
        // Tapping an already-selected icon clears it — Icon is optional, unlike Color, so there
        // needs to be a way back to "no icon" (the initials-circle fallback) without a separate
        // "clear" affordance.
        var wasSelected = option.IsSelected;
        foreach (var o in IconOptions)
            o.IsSelected = false;
        option.IsSelected = !wasSelected;
    }

    [RelayCommand]
    private Task Create() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            ErrorMessage = LocalizationResourceManager.Instance["NewGroup_EnterName"];
            return;
        }

        if (SelectedCurrencyIndex < 0)
        {
            ErrorMessage = LocalizationResourceManager.Instance["NewGroup_SelectCurrency"];
            return;
        }

        IsBusy = true;
        try
        {
            var currency = AppConstants.Currencies.All[SelectedCurrencyIndex].Code;
            var group = await groupsRepository.CreateAsync(GroupName.Trim(), currency);

            var color = ColorSwatches.FirstOrDefault(s => s.IsSelected)?.Preset.ToString() ?? group.Color;
            var icon = IconOptions.FirstOrDefault(o => o.IsSelected)?.Key;
            if (color != group.Color || icon is not null)
                await groupsRepository.UpdateAppearanceAsync(group.Id, color, icon);

            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
        }
    });
}
