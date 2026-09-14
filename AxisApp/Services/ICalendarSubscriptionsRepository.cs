using AxisApp.Models;

namespace AxisApp.Services;

public interface ICalendarSubscriptionsRepository
{
    /// <summary>The current account's calendar feed subscription, creating it (and its token) on
    /// first call if no row exists yet. Null only if the account has no member row at all yet —
    /// same precondition as IMembersRepository.GetMyMemberAsync.</summary>
    Task<CalendarSubscription?> GetOrCreateAsync();

    /// <summary>Rotates the token on an already-loaded subscription (from GetOrCreateAsync) — the
    /// previous link stops resolving the instant this returns, no separate revoke step. Same
    /// "start from a fully-loaded row" caveat as IMembersRepository.UpdateAsync.</summary>
    Task<CalendarSubscription> RegenerateAsync(CalendarSubscription subscription);
}
