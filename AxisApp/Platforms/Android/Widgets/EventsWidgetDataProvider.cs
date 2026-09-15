using AxisApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AxisApp.Widgets;

internal record EventRow(Guid GroupId, string GroupName, Guid EventId, string Title, DateTime StartsAt);

internal static class EventsWidgetDataProvider
{
    // Caps how many rows a single-group widget shows — a busy group's full future history isn't
    // useful at widget size, and this stays well under RemoteViews' list-adapter practical limits.
    private const int MaxRowsPerGroup = 15;

    /// <summary>groupFilter set = that group's own upcoming events (ascending, capped). groupFilter
    /// null (All groups) = the single soonest upcoming event per group, one row per group that has
    /// one, so a busy group can't flood the list and crowd out a quieter one's only event.
    /// "Upcoming" = hasn't started yet, same definition GroupEventsViewModel.Rebuild uses.</summary>
    public static async Task<List<EventRow>> GetSnapshotAsync(Guid? groupFilter)
    {
        var services = await WidgetDataAccess.GetReadyServicesAsync();
        if (services is null) return [];

        var authService = services.GetRequiredService<IAuthService>();
        if (!authService.IsAuthenticated) return [];

        var groupsRepository = services.GetRequiredService<IGroupsRepository>();
        var eventsRepository = services.GetRequiredService<IEventsRepository>();

        var groups = await groupsRepository.GetMyGroupsAsync();
        if (groupFilter is not null)
            groups = groups.Where(g => g.Id == groupFilter).ToList();

        var now = DateTime.UtcNow;
        var rows = new List<EventRow>();

        foreach (var group in groups)
        {
            var upcoming = (await eventsRepository.GetForGroupAsync(group.Id))
                .Where(e => e.StartsAt >= now)
                .OrderBy(e => e.StartsAt)
                .ToList();

            if (upcoming.Count == 0) continue;

            var toAdd = groupFilter is null ? upcoming.Take(1) : upcoming.Take(MaxRowsPerGroup);
            rows.AddRange(toAdd.Select(e => new EventRow(group.Id, group.Name, e.Id, e.Title, e.StartsAt)));
        }

        return rows.OrderBy(r => r.StartsAt).ToList();
    }
}
