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
            await groupsRepository.CreateAsync(GroupName.Trim(), currency);
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
        }
    });
}
