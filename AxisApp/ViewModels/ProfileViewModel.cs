using System.Globalization;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Media;

namespace AxisApp.ViewModels;

/// <summary>Everything that used to live directly in GroupsPage's floating account menu beyond
/// language/logout — own display name, birthday, avatar, language, and account credentials — now
/// lives on its own page instead. Reached via GroupsViewModel.OpenProfileCommand.</summary>
public partial class ProfileViewModel : BaseViewModel
{
    private static readonly IReadOnlyDictionary<Guid, string> NoAliases = new Dictionary<Guid, string>();

    private readonly IMembersRepository membersRepository;
    private readonly IAvatarsRepository avatarsRepository;
    private readonly IAuthService authService;

    private Member? myMember;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string userEmail = "";
    [ObservableProperty] private string? avatarUrl;
    [ObservableProperty] private string initials = "";
    [ObservableProperty] private bool hasBirthday;
    [ObservableProperty] private DateTime birthday = DateTime.Today.AddYears(-25);
    [ObservableProperty] private string birthdayDisplay = "";
    [ObservableProperty] private string selectedLanguageOverride;
    [ObservableProperty] private string newEmail = "";
    [ObservableProperty] private string newPassword = "";
    [ObservableProperty] private string statusMessage = "";

    public ProfileViewModel(IMembersRepository membersRepository, IAvatarsRepository avatarsRepository, IAuthService authService)
    {
        this.membersRepository = membersRepository;
        this.avatarsRepository = avatarsRepository;
        this.authService = authService;

        UserEmail = authService.CurrentEmail ?? "";
        selectedLanguageOverride = LocalizationResourceManager.Instance.CurrentOverride;
    }

    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        IsBusy = true;
        try
        {
            myMember = await membersRepository.GetMyMemberAsync();
            ApplyMemberToFields();
        }
        finally
        {
            IsBusy = false;
        }
    });

    private void ApplyMemberToFields()
    {
        if (myMember is null) return;

        DisplayName = myMember.DisplayName;
        AvatarUrl = MemberDisplay.AvatarUrl(myMember);
        Initials = MemberDisplay.Initials(myMember, NoAliases);

        HasBirthday = myMember.BirthDate is not null;
        Birthday = myMember.BirthDate ?? Birthday;
        RefreshBirthdayDisplay();
    }

    private void RefreshBirthdayDisplay() =>
        BirthdayDisplay = HasBirthday
            ? Birthday.ToString("d", CultureInfo.CurrentUICulture)
            : LocalizationResourceManager.Instance["Profile_BirthdayNotSet"];

    partial void OnBirthdayChanged(DateTime value) => RefreshBirthdayDisplay();

    [RelayCommand]
    private void SetBirthday()
    {
        HasBirthday = true;
        RefreshBirthdayDisplay();
    }

    [RelayCommand]
    private void ClearBirthday()
    {
        HasBirthday = false;
        RefreshBirthdayDisplay();
    }

    /// <summary>Saves DisplayName + BirthDate onto the existing, fully-loaded myMember — never a
    /// freshly-constructed Member — since IMembersRepository.UpdateAsync sends the whole model
    /// (same carry-every-field-over caveat as SupabaseExpensesRepository.UpdateAsync).</summary>
    [RelayCommand]
    private Task SaveProfile() => RunSafeAsync(async () =>
    {
        if (myMember is null) return;

        StatusMessage = "";
        IsBusy = true;
        try
        {
            myMember.DisplayName = DisplayName.Trim();
            myMember.BirthDate = HasBirthday ? Birthday.Date : null;
            myMember = await membersRepository.UpdateAsync(myMember);
            ApplyMemberToFields();
            StatusMessage = LocalizationResourceManager.Instance["Profile_ProfileSaved"];
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task ChangePhoto() => RunSafeAsync(async () =>
    {
        if (myMember is null) return;

        var photo = await MediaPicker.Default.PickPhotoAsync();
        if (photo is null) return;

        await using var stream = await photo.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);

        var webp = ImageResizer.ToAvatarWebp(memory.ToArray());
        myMember = await avatarsRepository.SetAvatarAsync(myMember, webp);
        AvatarUrl = MemberDisplay.AvatarUrl(myMember);
    });

    [RelayCommand]
    private Task RemovePhoto() => RunSafeAsync(async () =>
    {
        if (myMember is null) return;

        myMember = await avatarsRepository.RemoveAvatarAsync(myMember);
        AvatarUrl = null;
    });

    [RelayCommand]
    private void ChangeLanguage(string? languageCode)
    {
        LocalizationResourceManager.Instance.SetLanguage(languageCode);
        SelectedLanguageOverride = languageCode ?? "";
    }

    [RelayCommand]
    private Task ChangeEmail() => RunSafeAsync(async () =>
    {
        var trimmed = NewEmail.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        StatusMessage = "";
        var result = await authService.UpdateEmailAsync(trimmed);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "";
            return;
        }

        NewEmail = "";
        StatusMessage = LocalizationResourceManager.Instance["Profile_EmailUpdateSent"];
    });

    [RelayCommand]
    private Task ChangePassword() => RunSafeAsync(async () =>
    {
        var trimmed = NewPassword.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;

        StatusMessage = "";
        var result = await authService.UpdatePasswordAsync(trimmed);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "";
            return;
        }

        NewPassword = "";
        StatusMessage = LocalizationResourceManager.Instance["Profile_PasswordUpdated"];
    });
}
