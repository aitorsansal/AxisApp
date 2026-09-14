using System.Security.Cryptography;
using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

/// <summary>
/// Supabase-backed ICalendarSubscriptionsRepository. Token/CreatedAt are always set explicitly on
/// insert rather than left at their C# defaults — Postgrest sends every plain [Column] property on
/// insert regardless, which would otherwise silently override the table's
/// `default encode(gen_random_bytes(24), 'base64url')`/`default now()` the same way it did for
/// invites.token/expires_at (see SupabaseInvitesRepository.GenerateToken's remarks for the exact
/// live bug that caused).
/// </summary>
public class SupabaseCalendarSubscriptionsRepository : ICalendarSubscriptionsRepository
{
    private readonly Supabase.Client client;
    private readonly IMembersRepository membersRepository;

    public SupabaseCalendarSubscriptionsRepository(Supabase.Client client, IMembersRepository membersRepository)
    {
        this.client = client;
        this.membersRepository = membersRepository;
    }

    public async Task<CalendarSubscription?> GetOrCreateAsync()
    {
        var member = await membersRepository.GetMyMemberAsync();
        if (member is null) return null;

        var existing = await client.From<CalendarSubscription>()
            .Filter("member_id", Constants.Operator.Equals, member.Id.ToString())
            .Single();
        if (existing is not null) return existing;

        var inserted = await client.From<CalendarSubscription>().Insert(new CalendarSubscription
        {
            MemberId = member.Id,
            Token = GenerateToken(),
            CreatedAt = DateTime.UtcNow
        });

        return inserted.Model!;
    }

    public async Task<CalendarSubscription> RegenerateAsync(CalendarSubscription subscription)
    {
        subscription.Token = GenerateToken();
        var result = await client.From<CalendarSubscription>()
            .Filter("id", Constants.Operator.Equals, subscription.Id.ToString())
            .Update(subscription);

        return result.Model!;
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
