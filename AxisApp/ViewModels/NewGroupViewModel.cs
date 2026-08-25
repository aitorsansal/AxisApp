using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

public partial class NewGroupViewModel : ObservableObject
{
    private readonly IGroupsRepository groupsRepository;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string errorMessage = "";

    public NewGroupViewModel(IGroupsRepository groupsRepository)
    {
        this.groupsRepository = groupsRepository;
    }

    [RelayCommand]
    private async Task Create()
    {
        if (string.IsNullOrWhiteSpace(GroupName))
        {
            ErrorMessage = "Enter a group name.";
            return;
        }

        IsBusy = true;
        ErrorMessage = "";
        try
        {
            await groupsRepository.CreateAsync(GroupName.Trim());
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
