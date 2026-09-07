using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>
/// Add/edit an Event — doubles as edit via the same `?eventId=` query-param pattern
/// AddExpensePage already uses for expenses (see /EVENTS_PLAN.md Milestone 3b). Any current group
/// member can create or edit an event (matching Milestone 1's RLS), but only the creator sees the
/// Delete action — a plain group-member edit stays open (fixing a typo'd time/location shouldn't
/// need the organizer specifically), only delete is restricted, per the plan's "Decisions locked".
/// </summary>
public partial class AddEventViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IEventsRepository eventsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private Guid? editingEventId;
    private Guid? editingCreatedBy;
    private DateTime editingCreatedAt;

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string location = "";
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private TimeSpan startTime = RoundedNow();
    [ObservableProperty] private string startDateDisplay = "";
    [ObservableProperty] private string startTimeDisplay = "";
    [ObservableProperty] private bool hasEndTime;
    [ObservableProperty] private DateTime endDate = DateTime.Today;
    [ObservableProperty] private TimeSpan endTime = RoundedNow();
    [ObservableProperty] private string endDateDisplay = "";
    [ObservableProperty] private string endTimeDisplay = "";
    [ObservableProperty] private bool needsTransport;
    [ObservableProperty] private bool canSave;
    [ObservableProperty] private bool isBusy;
    /// <summary>True only while the initial LoadAsync is in flight — kept separate from IsBusy
    /// (which Save/Delete also set) so the skeleton doesn't flash back over an already-loaded
    /// form on every save tap. See AddExpenseViewModel.IsInitialLoading for the same reasoning.</summary>
    [ObservableProperty] private bool isInitialLoading;
    [ObservableProperty] private bool isEditMode;

    private bool isEventCreator;

    /// <summary>Drives the Delete button's visibility — true only when editing an existing event
    /// you created (never on a fresh add, since there's nothing to delete yet, and never when
    /// editing someone else's event).</summary>
    [ObservableProperty] private bool canDelete;

    [ObservableProperty] private string pageTitle = LocalizationResourceManager.Instance["AddEvent_Title"];

    partial void OnTitleChanged(string value) => CanSave = !string.IsNullOrWhiteSpace(value);

    partial void OnStartDateChanged(DateTime value) => StartDateDisplay = FormatDate(value);
    partial void OnStartTimeChanged(TimeSpan value) => StartTimeDisplay = FormatTime(value);
    partial void OnEndDateChanged(DateTime value) => EndDateDisplay = FormatDate(value);
    partial void OnEndTimeChanged(TimeSpan value) => EndTimeDisplay = FormatTime(value);

    private static string FormatDate(DateTime value) =>
        value.Date == DateTime.Today
            ? LocalizationResourceManager.Instance["Common_Today"]
            : value.ToString("MMM d, yyyy");

    private static string FormatTime(TimeSpan value) => DateTime.Today.Add(value).ToString("HH:mm");

    private static TimeSpan RoundedNow() => DateTime.Now.TimeOfDay;

    public AddEventViewModel(IEventsRepository eventsRepository, IMembersRepository membersRepository, IAuthService authService)
    {
        this.eventsRepository = eventsRepository;
        this.membersRepository = membersRepository;
        this.authService = authService;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Guid? eventId = query.TryGetValue("eventId", out var eventValue)
            && Guid.TryParse(eventValue?.ToString(), out var parsedEventId)
                ? parsedEventId
                : null;

        if (query.TryGetValue("groupId", out var groupValue) && Guid.TryParse(groupValue?.ToString(), out var groupIdValue))
            _ = LoadAsync(groupIdValue, eventId);
    }

    public Task LoadAsync(Guid forGroupId, Guid? forEventId = null) => RunSafeAsync(async () =>
    {
        groupId = forGroupId;
        editingEventId = forEventId;
        IsEditMode = forEventId is not null;
        PageTitle = LocalizationResourceManager.Instance[IsEditMode ? "AddEvent_EditTitle" : "AddEvent_Title"];

        IsBusy = true;
        IsInitialLoading = true;
        try
        {
            await WithMinimumDurationAsync(TimeSpan.FromMilliseconds(400), async () =>
            {
            if (forEventId is { } id)
            {
                var ev = await eventsRepository.GetByIdAsync(id);
                if (ev is not null)
                {
                    editingCreatedBy = ev.CreatedBy;
                    editingCreatedAt = ev.CreatedAt;
                    isEventCreator = ev.CreatedBy == authService.CurrentAccountId;

                    Title = ev.Title;
                    Description = ev.Description ?? "";
                    Location = ev.Location ?? "";
                    NeedsTransport = ev.NeedsTransport;

                    var localStart = ev.StartsAt.ToLocalTime();
                    StartDate = localStart.Date;
                    StartTime = localStart.TimeOfDay;

                    if (ev.EndsAt is { } endsAtValue)
                    {
                        var localEnd = endsAtValue.ToLocalTime();
                        HasEndTime = true;
                        EndDate = localEnd.Date;
                        EndTime = localEnd.TimeOfDay;
                    }
                    else
                    {
                        HasEndTime = false;
                    }
                }
            }
            else
            {
                Title = "";
                Description = "";
                Location = "";
                NeedsTransport = false;
                StartDate = DateTime.Today;
                StartTime = RoundedNow();
                HasEndTime = false;
                EndDate = DateTime.Today;
                EndTime = RoundedNow();
                isEventCreator = true;
            }

            CanSave = !string.IsNullOrWhiteSpace(Title);
            CanDelete = IsEditMode && isEventCreator;
            });
        }
        finally
        {
            IsBusy = false;
            IsInitialLoading = false;
        }
    });

    /// <summary>Same DateTime.Today/ToUniversalTime() idiom AddExpenseViewModel.Save already uses
    /// for OccurredAt — a DatePicker's value carries Kind=Unspecified, and ToUniversalTime treats
    /// that as local time, which is what's actually wanted here.</summary>
    [RelayCommand]
    private Task Save() => RunSafeAsync(async () =>
    {
        if (!CanSave) return;

        IsBusy = true;
        try
        {
            var startsAt = StartDate.Date.Add(StartTime).ToUniversalTime();
            DateTime? endsAt = HasEndTime ? EndDate.Date.Add(EndTime).ToUniversalTime() : null;

            var ev = new Event
            {
                GroupId = groupId,
                Title = Title.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                Location = string.IsNullOrWhiteSpace(Location) ? null : Location.Trim(),
                StartsAt = startsAt,
                EndsAt = endsAt,
                NeedsTransport = NeedsTransport
            };

            if (IsEditMode && editingEventId is { } id)
            {
                ev.Id = id;
                ev.CreatedBy = editingCreatedBy;
                ev.CreatedAt = editingCreatedAt;
                await eventsRepository.UpdateAsync(ev);
            }
            else
            {
                var inserted = await eventsRepository.AddAsync(ev);

                // Creator auto-RSVPs "going" — see /EVENTS_PLAN.md's Milestone 3b remarks.
                var myMember = await membersRepository.GetMyMemberAsync();
                if (myMember is not null)
                    await eventsRepository.UpsertRsvpAsync(inserted.Id, myMember.Id, "going");
            }

            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task Delete() => RunSafeAsync(async () =>
    {
        if (!IsEditMode || editingEventId is not { } id) return;

        IsBusy = true;
        try
        {
            await eventsRepository.DeleteAsync(id);
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task Cancel() => RunSafeAsync(() => Shell.Current.GoToAsync(".."));
}
