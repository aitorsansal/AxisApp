using System.Collections.ObjectModel;
using System.Globalization;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One row in the attendee roster, grouped by RSVP response — see
/// EventDetailViewModel.Rebuild. A plain display wrapper, not editable directly (unlike
/// GroupEventsViewModel's own RSVP/transport controls, which act on the viewer's own row only).</summary>
public partial class EventAttendeeRowItem : ObservableObject
{
    public string Name { get; init; } = "";
    public string Initials { get; init; } = "";
    public string? AvatarUrl { get; init; }
    public bool IsYou { get; init; }
    public bool IsPhantom { get; init; }

    /// <summary>"Has a car (2 free seats)" / "Needs a ride" / "" — only meaningful for a Going row.</summary>
    public string CarCaption { get; init; } = "";
}

/// <summary>
/// Event detail screen — reached from GroupEventsViewModel.OpenEvent (never for a birthday event,
/// guarded there same as before). Per the design discussion in CLAUDE.md: RSVP stays available both
/// here and inline on GroupEventsView's row (a cheap, frequent action shouldn't require navigating
/// in), duplicated as a thin wrapper over the same IEventsRepository.UpsertRsvpAsync call — no new
/// business logic, just new bindings. Transport controls are mirrored the same way. What's genuinely
/// new here: a real attendee roster (names, not just an avatar stack) grouped by response, and the
/// expenses linked to this event via Expense.EventId, with a "+ Add expense" that pre-filters
/// AddExpensePage's participants to whoever is currently "going" (see AddExpenseViewModel.LoadAsync's
/// forEventId handling).
/// </summary>
public partial class EventDetailViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IEventsRepository eventsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IAliasesRepository aliasesRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly IGroupsRepository groupsRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private Guid eventId;
    private Guid? myMemberId;
    private int? myCarExtraSeats;
    private Group? currentGroup;
    private Dictionary<Guid, Member> membersById = new();
    private Dictionary<Guid, string> aliases = new();

    /// <summary>Cached from the last LoadAsync — lets the RSVP/transport commands below patch a
    /// single attendee row and re-derive the visible state locally (BuildRsvpAndTransport/
    /// BuildRoster are pure/synchronous) instead of a full LoadAsync per tap. See
    /// GroupEventsViewModel.ApplyAttendeeUpdate's matching remarks on why that reload was slow.</summary>
    private Event? currentEvent;
    private List<EventAttendee> currentAttendees = [];

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string subCaption = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private bool hasDescription;
    [ObservableProperty] private bool isBusy;
    /// <summary>True only while the very first LoadAsync is in flight — same IsInitialLoading
    /// skeleton-scoping pattern as every other tab/page in this app.</summary>
    [ObservableProperty] private bool isInitialLoading;
    private bool hasLoadedOnce;

    /// <summary>"going" / "maybe" / "not_going" / "" (never RSVP'd) — see GroupEventsViewModel's
    /// matching remarks on EventListItem.MyResponse.</summary>
    [ObservableProperty] private string myResponse = "";
    [ObservableProperty] private string carStatus = "none";
    [ObservableProperty] private int carOfferedSeats;
    [ObservableProperty] private bool needsTransport;
    [ObservableProperty] private bool showTransportControls;
    [ObservableProperty] private bool canOfferCar;
    [ObservableProperty] private int seatsOffered;
    [ObservableProperty] private int ridersNeeded;
    [ObservableProperty] private bool hasShortfall;
    [ObservableProperty] private string transportSummaryText = "";

    [ObservableProperty] private ObservableCollection<EventAttendeeRowItem> goingAttendees = [];
    [ObservableProperty] private ObservableCollection<EventAttendeeRowItem> maybeAttendees = [];
    [ObservableProperty] private ObservableCollection<EventAttendeeRowItem> notGoingAttendees = [];
    [ObservableProperty] private bool hasMaybeAttendees;
    [ObservableProperty] private bool hasNotGoingAttendees;

    [ObservableProperty] private ObservableCollection<ActivityItem> expenses = [];
    [ObservableProperty] private bool hasExpenses;

    public EventDetailViewModel(
        IEventsRepository eventsRepository,
        IMembersRepository membersRepository,
        IAliasesRepository aliasesRepository,
        IExpensesRepository expensesRepository,
        IGroupsRepository groupsRepository,
        IAuthService authService)
    {
        this.eventsRepository = eventsRepository;
        this.membersRepository = membersRepository;
        this.aliasesRepository = aliasesRepository;
        this.expensesRepository = expensesRepository;
        this.groupsRepository = groupsRepository;
        this.authService = authService;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("eventId", out var eventValue) || !Guid.TryParse(eventValue?.ToString(), out var parsedEventId))
            return;

        if (query.TryGetValue("groupId", out var groupValue) && Guid.TryParse(groupValue?.ToString(), out var groupIdValue))
            _ = LoadAsync(groupIdValue, parsedEventId);
    }

    public Task LoadAsync(Guid forGroupId, Guid forEventId) => RunSafeAsync(async () =>
    {
        groupId = forGroupId;
        eventId = forEventId;
        IsBusy = true;
        var isFirstLoad = !hasLoadedOnce;
        IsInitialLoading = isFirstLoad;
        try
        {
            async Task DoLoad()
            {
                var loadEvent = eventsRepository.GetByIdAsync(eventId);
                var loadAttendees = eventsRepository.GetAttendeesAsync(eventId);
                var loadMembers = membersRepository.GetForGroupAsync(groupId);
                var loadAliases = aliasesRepository.GetMyAliasesAsync();
                var loadGroup = groupsRepository.GetByIdAsync(groupId);
                var loadExpenses = expensesRepository.GetForEventAsync(eventId);
                await Task.WhenAll(loadEvent, loadAttendees, loadMembers, loadAliases, loadGroup, loadExpenses);

                var ev = loadEvent.Result;
                if (ev is null) return;

                var members = loadMembers.Result;
                membersById = members.ToDictionary(m => m.Id);
                aliases = loadAliases.Result;
                currentGroup = loadGroup.Result;
                var myMember = members.FirstOrDefault(m => m.AccountId == authService.CurrentAccountId);
                myMemberId = myMember?.Id;
                myCarExtraSeats = myMember?.CarExtraSeats;

                currentEvent = ev;
                currentAttendees = loadAttendees.Result;

                BuildHeader(ev);
                BuildRsvpAndTransport(ev, currentAttendees);
                BuildRoster(currentAttendees);
                await BuildExpensesAsync(loadExpenses.Result);
            }

            if (isFirstLoad)
                await WithMinimumDurationAsync(TimeSpan.FromMilliseconds(400), DoLoad);
            else
                await DoLoad();
        }
        finally
        {
            IsBusy = false;
            IsInitialLoading = false;
            hasLoadedOnce = true;
        }
    });

    private void BuildHeader(Event ev)
    {
        Title = ev.Title;
        var subParts = new List<string> { ev.StartsAt.ToLocalTime().ToString("ddd, MMM d · HH:mm") };
        if (!string.IsNullOrWhiteSpace(ev.Location)) subParts.Add(ev.Location!);
        SubCaption = string.Join(" · ", subParts);
        Description = ev.Description ?? "";
        HasDescription = !string.IsNullOrWhiteSpace(Description);
    }

    private void BuildRsvpAndTransport(Event ev, List<EventAttendee> attendees)
    {
        var mine = attendees.FirstOrDefault(a => a.MemberId == myMemberId);
        MyResponse = mine?.Response ?? "";
        CarStatus = mine?.CarStatus ?? "none";
        CarOfferedSeats = mine?.CarOfferedSeats ?? 0;
        NeedsTransport = ev.NeedsTransport;
        ShowTransportControls = MyResponse is "going" or "maybe";
        CanOfferCar = MyResponse == "going";

        SeatsOffered = attendees.Where(a => a.CarStatus == "offering").Sum(a => a.CarOfferedSeats ?? 0);
        RidersNeeded = attendees.Count(a => a.CarStatus == "needs_ride");
        HasShortfall = SeatsOffered < RidersNeeded;
        TransportSummaryText = LocalizationResourceManager.Instance.Format("GroupEvents_TransportSummary", SeatsOffered, RidersNeeded);
    }

    private void BuildRoster(List<EventAttendee> attendees)
    {
        var loc = LocalizationResourceManager.Instance;

        EventAttendeeRowItem? BuildRow(EventAttendee attendee)
        {
            if (!membersById.TryGetValue(attendee.MemberId, out var member)) return null;
            var carCaption = attendee.CarStatus switch
            {
                "offering" => loc.Format("EventDetail_HasCarSeats", attendee.CarOfferedSeats ?? 0),
                "needs_ride" => loc["GroupEvents_NeedRide"],
                _ => ""
            };
            return new EventAttendeeRowItem
            {
                Name = MemberDisplay.Name(member, aliases),
                Initials = MemberDisplay.Initials(member, aliases),
                AvatarUrl = MemberDisplay.AvatarUrl(member),
                IsYou = member.Id == myMemberId,
                IsPhantom = member.IsPhantom,
                CarCaption = carCaption
            };
        }

        GoingAttendees = new ObservableCollection<EventAttendeeRowItem>(
            attendees.Where(a => a.Response == "going").Select(BuildRow).Where(r => r is not null)!);
        MaybeAttendees = new ObservableCollection<EventAttendeeRowItem>(
            attendees.Where(a => a.Response == "maybe").Select(BuildRow).Where(r => r is not null)!);
        NotGoingAttendees = new ObservableCollection<EventAttendeeRowItem>(
            attendees.Where(a => a.Response == "not_going").Select(BuildRow).Where(r => r is not null)!);
        HasMaybeAttendees = MaybeAttendees.Count > 0;
        HasNotGoingAttendees = NotGoingAttendees.Count > 0;
    }

    /// <summary>Same row shape (and the same FormatExpenseAmount/FormatRelative helpers) as
    /// GroupExpensesViewModel's Recent Activity list — reused rather than duplicated.</summary>
    private async Task BuildExpensesAsync(List<Expense> forEvent)
    {
        var groupSymbol = AppConstants.Currencies.SymbolFor(currentGroup!.Currency);
        var showConverted = Microsoft.Maui.Storage.Preferences.Default.Get(AppConstants.Preferences.AmountDisplayConverted, true);
        var loc = LocalizationResourceManager.Instance;
        var items = new List<ActivityItem>();

        foreach (var expense in forEvent)
        {
            var payer = membersById.TryGetValue(expense.PaidByMemberId, out var expensePayer)
                ? MemberDisplay.Name(expensePayer, aliases) : loc["GroupDetail_SomeoneCapitalized"];
            var shares = await expensesRepository.GetSharesAsync(expense.Id);

            var categoryLabel = string.IsNullOrEmpty(expense.Category) ? "" : loc[$"Category_{expense.Category}"];
            var description = string.IsNullOrWhiteSpace(expense.Description) ? categoryLabel : expense.Description;
            var subCaption = loc.Format("GroupDetail_PaidSplit", payer, shares.Count);

            var (amountText, secondaryAmountText) = GroupExpensesViewModel.FormatExpenseAmount(
                expense, groupSymbol, currentGroup!.Currency, showConverted);

            items.Add(new ActivityItem
            {
                Description = description,
                SubCaption = subCaption,
                AmountText = amountText,
                SecondaryAmountText = secondaryAmountText,
                IsSettlement = expense.IsSettlement,
                OccurredAt = expense.OccurredAt,
                CreatedAt = expense.CreatedAt,
                RelativeDate = GroupExpensesViewModel.FormatRelative(expense.OccurredAt),
                ExpenseId = expense.Id
            });
        }

        Expenses = new ObservableCollection<ActivityItem>(
            items.OrderByDescending(i => i.OccurredAt).ThenByDescending(i => i.CreatedAt));
        HasExpenses = Expenses.Count > 0;
    }

    [RelayCommand]
    private Task EditEvent() => RunSafeAsync(() =>
        Shell.Current.GoToAsync($"{AppConstants.Routes.AddEvent}?groupId={groupId}&eventId={eventId}"));

    [RelayCommand]
    private Task AddExpense() => RunSafeAsync(() =>
        Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}&eventId={eventId}"));

    [RelayCommand]
    private Task OpenExpense(ActivityItem? item) => RunSafeAsync(async () =>
    {
        if (item?.ExpenseId is not { } expenseId) return;
        await Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}&expenseId={expenseId}");
    });

    [RelayCommand]
    private Task SetRsvpGoing() => SetRsvpAsync("going");

    [RelayCommand]
    private Task SetRsvpMaybe() => SetRsvpAsync("maybe");

    [RelayCommand]
    private Task SetRsvpNotGoing() => SetRsvpAsync("not_going");

    /// <summary>Same carry-through-carStatus reasoning as GroupEventsViewModel.SetRsvpAsync — shared
    /// by all three RSVP buttons, so it must never blindly default carStatus to "none" on a
    /// going/maybe toggle, only on an actual switch to not_going (and on Going→Maybe while
    /// offering, per the same "a maybe can't be trusted with the car" rule). Patches the result
    /// into local state (ApplyAttendeeUpdate) rather than a full LoadAsync — see that method's
    /// remarks.</summary>
    private Task SetRsvpAsync(string response) => RunSafeAsync(async () =>
    {
        if (myMemberId is not { } me) return;
        var newCarStatus = response switch
        {
            "not_going" => "none",
            "maybe" when CarStatus == "offering" => "none",
            _ => CarStatus
        };
        var carSeats = newCarStatus == "offering" ? CarOfferedSeats : (int?)null;
        var updated = await eventsRepository.UpsertRsvpAsync(eventId, me, response, newCarStatus, carSeats);
        ApplyAttendeeUpdate(updated);
    });

    [RelayCommand]
    private Task ToggleCarOffering() => RunSafeAsync(async () =>
    {
        if (myMemberId is not { } me || MyResponse != "going") return;
        var newStatus = CarStatus == "offering" ? "none" : "offering";
        var seats = newStatus == "offering" ? myCarExtraSeats ?? 0 : (int?)null;
        var updated = await eventsRepository.UpsertRsvpAsync(eventId, me, MyResponse, newStatus, seats);
        ApplyAttendeeUpdate(updated);
    });

    [RelayCommand]
    private Task ToggleCarNeedsRide() => RunSafeAsync(async () =>
    {
        if (myMemberId is not { } me || MyResponse is not ("going" or "maybe")) return;
        var newStatus = CarStatus == "needs_ride" ? "none" : "needs_ride";
        var updated = await eventsRepository.UpsertRsvpAsync(eventId, me, MyResponse, newStatus);
        ApplyAttendeeUpdate(updated);
    });

    [RelayCommand]
    private Task IncrementCarSeats() => AdjustCarSeatsAsync(+1);

    [RelayCommand]
    private Task DecrementCarSeats() => AdjustCarSeatsAsync(-1);

    private Task AdjustCarSeatsAsync(int delta) => RunSafeAsync(async () =>
    {
        if (myMemberId is not { } me || CarStatus != "offering") return;
        var newSeats = Math.Max(0, CarOfferedSeats + delta);
        var updated = await eventsRepository.UpsertRsvpAsync(eventId, me, MyResponse, "offering", newSeats);
        ApplyAttendeeUpdate(updated);
    });

    /// <summary>Patches the single (event, member) row UpsertRsvpAsync just returned into
    /// currentAttendees, then rebuilds the RSVP/transport state and roster from it — both are
    /// pure/synchronous, no network — instead of a full LoadAsync per tap. Expenses/header don't
    /// need rebuilding since an RSVP write can't change either.</summary>
    private void ApplyAttendeeUpdate(EventAttendee updated)
    {
        if (currentEvent is null) return;

        var index = currentAttendees.FindIndex(a => a.MemberId == updated.MemberId);
        if (index >= 0) currentAttendees[index] = updated;
        else currentAttendees.Add(updated);

        BuildRsvpAndTransport(currentEvent, currentAttendees);
        BuildRoster(currentAttendees);
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync(groupId, eventId);
}
