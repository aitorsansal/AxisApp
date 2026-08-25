using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

public partial class NewGroupViewModel : BaseViewModel
{
    private readonly IGroupsRepository groupsRepository;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private bool isBusy;

    public NewGroupViewModel(IGroupsRepository groupsRepository)
    {
        this.groupsRepository = groupsRepository;
    }

    [RelayCommand]
    private Task Create() => RunSafeAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            ErrorMessage = "Enter a group name.";
            return;
        }

        IsBusy = true;
        try
        {
            await groupsRepository.CreateAsync(GroupName.Trim());
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
        }
    });
}
