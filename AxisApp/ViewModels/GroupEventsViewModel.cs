using System.Collections.ObjectModel;
using System.Globalization;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>A small avatar shown in an event row's "going" stack.</summary>
public partial class EventAttendeeAvatar : ObservableObject
{
    public string Initials { get; init; } = "";
    public string? AvatarUrl { get; init; }
}

/// <summary>One row in the grouped-by-month events list.</summary>
public partial class EventListItem : ObservableObject
{
    public Guid EventId { get; init; }
    public string Title { get; init; } = "";
    public string SubCaption { get; init; } = "";
    public DateTime StartsAt { get; init; }
    public ObservableCollection<EventAttendeeAvatar> GoingAvatars { get; init; } = [];

    /// <summary>"going" / "maybe" / "not_going", or "" when the viewer has no event_attendees row
    /// yet (never RSVP'd — a real, distinct state from "going", not defaulted to it, so the RSVP
    /// pill shows nothing selected rather than silently claiming they're going).</summary>
    [ObservableProperty] private string myResponse = "";

    /// <summary>The viewer's own car_status — "none" / "offering" / "needs_ride". Only shown as
    /// interactive controls when ShowTransportControls is true (see Milestone 4's "Decisions
    /// locked": can't offer/need a ride for an event you're not attending).</summary>
    [ObservableProperty] private string carStatus = "none";

    /// <summary>Meaningful only when CarStatus is "offering" — the viewer's own offered seat
    /// count for this specific event, editable via the +/- stepper, defaulting from
    /// Member.CarExtraSeats the first time they toggle "I have a car" on.</summary>
    [ObservableProperty] private int carOfferedSeats;

    /// <summary>Whether the transport section applies to this event at all.</summary>
    public bool NeedsTransport { get; init; }

    /// <summary>The interactive "I need a ride" control makes sense for anyone actually
    /// attending, including a "maybe" — asking for a ride costs nothing if they don't end up
    /// coming. Can't offer or need a ride for an event you're not going to at all, and Milestone
    /// 3a's RSVP/car_status coupling would silently reset the choice anyway the next time response
    /// changes to not_going.</summary>
    public bool ShowTransportControls { get; init; }

    /// <summary>"I have a car" is Going-only, not Maybe — a maybe can't be trusted to actually
    /// show up with the car, per explicit user feedback while reviewing this feature. A "maybe"
    /// who was previously "going" and offering has that offer cleared automatically (see
    /// GroupEventsViewModel.SetRsvpAsync) rather than left stranded and hidden.</summary>
    public bool CanOfferCar { get; init; }

    /// <summary>Sum of every offering attendee's seats vs. count of every needs-a-ride attendee —
    /// aggregate-only, see /EVENTS_PLAN.md's "Decisions locked" (no per-driver assignment).</summary>
    public int SeatsOffered { get; init; }
    public int RidersNeeded { get; init; }
    public bool HasShortfall => SeatsOffered < RidersNeeded;
    public string TransportSummaryText { get; init; } = "";
}

/// <summary>A month header ("September 2026") plus the events starting in it, in display order.</summary>
public partial class EventMonthGroup : ObservableObject
{
    public string Header { get; init; } = "";
    public ObservableCollection<EventListItem> Events { get; init; } = [];
}

/// <summary>Events tab of GroupDetailPage (see /EVENTS_PLAN.md Milestones 3b/4) — the
/// grouped-by-date events list, Upcoming/Past toggle, RSVP, navigation into AddEventPage for
/// create/edit, and the transport/carpooling aggregate + per-viewer offer/need toggle.
/// Independently fetches its own group members/aliases, same "genuinely self-sufficient tab"
/// design as GroupExpensesViewModel (see Milestone 2's remarks on that tradeoff).</summary>
public partial class GroupEventsViewModel : BaseViewModel
{
    private readonly IEventsRepository eventsRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IAliasesRepository aliasesRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private Guid? myMemberId;
    private int? myCarExtraSeats;
    private Dictionary<Guid, Member> membersById = new();
    private Dictionary<Guid, string> aliases = new();
    private List<Event> allEvents = [];
    private Dictionary<Guid, List<EventAttendee>> attendeesByEvent = new();

    [ObservableProperty] private ObservableCollection<EventMonthGroup> groupedEvents = [];
    [ObservableProperty] private bool hasEvents;
    [ObservableProperty] private bool isBusy;

    /// <summary>false = Upcoming (default), true = Past.</summary>
    [ObservableProperty] private bool isPastSelected;

    partial void OnIsPastSelectedChanged(bool value) => Rebuild();

    public GroupEventsViewModel(
        IEventsRepository eventsRepository,
        IMembersRepository membersRepository,
        IAliasesRepository aliasesRepository,
        IAuthService authService)
    {
        this.eventsRepository = eventsRepository;
        this.membersRepository = membersRepository;
        this.aliasesRepository = aliasesRepository;
        this.authService = authService;
    }

    public Task LoadAsync(Guid groupId) => RunSafeAsync(async () =>
    {
        this.groupId = groupId;
        IsBusy = true;
        try
        {
            await WithMinimumDurationAsync(TimeSpan.FromMilliseconds(400), async () =>
            {
                var loadEvents = eventsRepository.GetForGroupAsync(groupId);
                var loadMembers = membersRepository.GetForGroupAsync(groupId);
                var loadAliases = aliasesRepository.GetMyAliasesAsync();
                await Task.WhenAll(loadEvents, loadMembers, loadAliases);

                allEvents = loadEvents.Result;
                var members = loadMembers.Result;
                membersById = members.ToDictionary(m => m.Id);
                aliases = loadAliases.Result;
                var myMember = members.FirstOrDefault(m => m.AccountId == authService.CurrentAccountId);
                myMemberId = myMember?.Id;
                myCarExtraSeats = myMember?.CarExtraSeats;

                var attendees = new Dictionary<Guid, List<EventAttendee>>();
                foreach (var ev in allEvents)
                    attendees[ev.Id] = await eventsRepository.GetAttendeesAsync(ev.Id);
                attendeesByEvent = attendees;

                Rebuild();
            });
        }
        finally
        {
            IsBusy = false;
        }
    });

    /// <summary>Upcoming = hasn't started yet; Past = has started. An in-progress event (started,
    /// EndsAt still in the future) lands in Past — a deliberate simplification, not worth the extra
    /// bucketing logic for a mostly-optional EndsAt field.</summary>
    private void Rebuild()
    {
        var now = DateTime.UtcNow;
        var relevant = IsPastSelected
            ? allEvents.Where(e => e.StartsAt < now).OrderByDescending(e => e.StartsAt)
            : allEvents.Where(e => e.StartsAt >= now).OrderBy(e => e.StartsAt);

        var groups = new List<EventMonthGroup>();
        EventMonthGroup? current = null;
        string? currentKey = null;

        foreach (var ev in relevant)
        {
            var key = ev.StartsAt.ToLocalTime().ToString("MMMM yyyy", CultureInfo.CurrentUICulture);
            if (key != currentKey)
            {
                current = new EventMonthGroup { Header = key };
                groups.Add(current);
                currentKey = key;
            }
            current!.Events.Add(BuildListItem(ev));
        }

        GroupedEvents = new ObservableCollection<EventMonthGroup>(groups);
        HasEvents = groups.Count > 0;
    }

    private EventListItem BuildListItem(Event ev)
    {
        var attendees = attendeesByEvent.TryGetValue(ev.Id, out var list) ? list : [];
        var mine = attendees.FirstOrDefault(a => a.MemberId == myMemberId);

        var goingAvatars = attendees
            .Where(a => a.Response == "going")
            .Select(a => membersById.TryGetValue(a.MemberId, out var m) ? m : null)
            .Where(m => m is not null)
            .Select(m => new EventAttendeeAvatar { Initials = MemberDisplay.Initials(m!, aliases), AvatarUrl = MemberDisplay.AvatarUrl(m!) });

        var subParts = new List<string> { ev.StartsAt.ToLocalTime().ToString("ddd, MMM d · HH:mm") };
        if (!string.IsNullOrWhiteSpace(ev.Location)) subParts.Add(ev.Location!);

        var myResponse = mine?.Response ?? "";
        var seatsOffered = attendees.Where(a => a.CarStatus == "offering").Sum(a => a.CarOfferedSeats ?? 0);
        var ridersNeeded = attendees.Count(a => a.CarStatus == "needs_ride");

        return new EventListItem
        {
            EventId = ev.Id,
            Title = ev.Title,
            SubCaption = string.Join(" · ", subParts),
            NeedsTransport = ev.NeedsTransport,
            StartsAt = ev.StartsAt,
            GoingAvatars = new ObservableCollection<EventAttendeeAvatar>(goingAvatars),
            MyResponse = myResponse,
            CarStatus = mine?.CarStatus ?? "none",
            CarOfferedSeats = mine?.CarOfferedSeats ?? 0,
            ShowTransportControls = myResponse is "going" or "maybe",
            CanOfferCar = myResponse == "going",
            SeatsOffered = seatsOffered,
            RidersNeeded = ridersNeeded,
            TransportSummaryText = LocalizationResourceManager.Instance.Format("GroupEvents_TransportSummary", seatsOffered, ridersNeeded)
        };
    }

    [RelayCommand]
    private void ShowUpcoming() => IsPastSelected = false;

    [RelayCommand]
    private void ShowPast() => IsPastSelected = true;

    [RelayCommand]
    private Task AddEvent() => RunSafeAsync(() =>
        Shell.Current.GoToAsync($"{AppConstants.Routes.AddEvent}?groupId={groupId}"));

    [RelayCommand]
    private Task OpenEvent(EventListItem? item) => RunSafeAsync(() =>
        item is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{AppConstants.Routes.AddEvent}?groupId={groupId}&eventId={item.EventId}"));

    [RelayCommand]
    private Task SetRsvpGoing(EventListItem? item) => SetRsvpAsync(item, "going");

    [RelayCommand]
    private Task SetRsvpMaybe(EventListItem? item) => SetRsvpAsync(item, "maybe");

    [RelayCommand]
    private Task SetRsvpNotGoing(EventListItem? item) => SetRsvpAsync(item, "not_going");

    /// <summary>Writes then reloads the whole tab, same "write then reload" pattern
    /// GroupExpensesViewModel.Settle already uses rather than patching local state — simpler and
    /// keeps the going-avatars stack and every other row's derived state consistent for free.
    ///
    /// Carries the item's existing CarStatus/CarOfferedSeats through on a going/maybe transition
    /// — a real bug caught in review before it shipped: this method is shared by all three RSVP
    /// buttons, and omitting carStatus here would fall through to UpsertRsvpAsync's "none" default
    /// on *every* call, silently wiping someone's car offer/need just from toggling Going↔Maybe,
    /// not only on an actual switch to Not going (where the repository's own coupling logic
    /// already forces it to "none" regardless of what's passed).
    ///
    /// Downgrading Going→Maybe while "offering" clears the offer specifically (not just carried
    /// through like "needs_ride" is) — per explicit user feedback: a "maybe" can ask for a ride
    /// (costs nothing if they don't show), but shouldn't be trusted to actually provide the car.
    /// A stale offer is cleared rather than just hidden, so it stops counting toward the aggregate
    /// seats-offered total the moment it's no longer trustworthy.</summary>
    private Task SetRsvpAsync(EventListItem? item, string response) => RunSafeAsync(async () =>
    {
        if (item is null || myMemberId is not { } me) return;
        var carStatus = response switch
        {
            "not_going" => "none",
            "maybe" when item.CarStatus == "offering" => "none",
            _ => item.CarStatus
        };
        var carSeats = carStatus == "offering" ? item.CarOfferedSeats : (int?)null;
        await eventsRepository.UpsertRsvpAsync(item.EventId, me, response, carStatus, carSeats);
        await LoadAsync(groupId);
    });

    /// <summary>Tapping the already-active state toggles it back off ("none") — the same
    /// "tap again to deselect" shape a single-select chip needs when there's no separate "clear"
    /// action. Going-only, not Maybe (see EventListItem.CanOfferCar) — a "maybe" can't be trusted
    /// to actually show up with the car. Only reachable when CanOfferCar is true, but re-checked
    /// here too as defense in depth against a stale row.</summary>
    [RelayCommand]
    private Task ToggleCarOffering(EventListItem? item) => RunSafeAsync(async () =>
    {
        if (item is null || myMemberId is not { } me || item.MyResponse != "going") return;
        var newStatus = item.CarStatus == "offering" ? "none" : "offering";
        var seats = newStatus == "offering" ? myCarExtraSeats ?? 0 : (int?)null;
        await eventsRepository.UpsertRsvpAsync(item.EventId, me, item.MyResponse, newStatus, seats);
        await LoadAsync(groupId);
    });

    [RelayCommand]
    private Task ToggleCarNeedsRide(EventListItem? item) => RunSafeAsync(async () =>
    {
        if (item is null || myMemberId is not { } me || item.MyResponse is not ("going" or "maybe")) return;
        var newStatus = item.CarStatus == "needs_ride" ? "none" : "needs_ride";
        await eventsRepository.UpsertRsvpAsync(item.EventId, me, item.MyResponse, newStatus);
        await LoadAsync(groupId);
    });

    [RelayCommand]
    private Task IncrementCarSeats(EventListItem? item) => AdjustCarSeatsAsync(item, +1);

    [RelayCommand]
    private Task DecrementCarSeats(EventListItem? item) => AdjustCarSeatsAsync(item, -1);

    private Task AdjustCarSeatsAsync(EventListItem? item, int delta) => RunSafeAsync(async () =>
    {
        if (item is null || myMemberId is not { } me || item.CarStatus != "offering") return;
        var newSeats = Math.Max(0, item.CarOfferedSeats + delta);
        await eventsRepository.UpsertRsvpAsync(item.EventId, me, item.MyResponse, "offering", newSeats);
        await LoadAsync(groupId);
    });

    [RelayCommand]
    private Task Refresh() => LoadAsync(groupId);
}
