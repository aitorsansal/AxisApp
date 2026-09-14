using System.Security.Cryptography;
using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

/// <summary>
/// Supabase-backed IInvitesRepository. RedeemAsync's use of client.Rpc(...) is the least-verified
/// call in this codebase's Supabase layer — confirmed only that `client.Rpc(name, params)` exists
/// (from the SDK wiki's `supabase.Rpc("hello_world", null)` example), not its exact parameter-
/// dictionary shape or how a scalar uuid return value comes back on the response (assumed here to
/// be the raw response body as a quoted JSON string, since redeem_invite's SQL declares `returns
/// uuid`). If this doesn't compile or throws at runtime, that's the first place to look — paste
/// back what IntelliSense/the compiler actually says client.Rpc's signature and response type are.
/// </summary>
public class SupabaseInvitesRepository : IInvitesRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseInvitesRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    /// <summary>Looks for a still-usable invite first (avoids flooding the table with a fresh
    /// row every time this group's Invite page or a phantom's Resend button is opened), scoped
    /// by (group, target member) so the group's general QR/code invite (target_member_id null)
    /// is never mixed up with a phantom-claim invite. There's no single PostgREST filter for
    /// "use_count less than max_uses" (that compares two columns, not a column to a literal), so
    /// this fetches the most recent candidate and checks both conditions client-side.</summary>
    public async Task<Invite> GetOrCreateAsync(Guid groupId, Guid? targetMemberId = null)
    {
        var query = client.From<Invite>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString());
        query = targetMemberId is { } tid
            ? query.Filter("target_member_id", Constants.Operator.Equals, tid.ToString())
            : query.Filter("target_member_id", Constants.Operator.Is, "null");

        var result = await query.Order("created_at", Constants.Ordering.Descending).Limit(1).Get();
        var existing = result.Models.FirstOrDefault();
        if (existing is not null && existing.UseCount < existing.MaxUses && existing.ExpiresAt > DateTime.UtcNow)
            return existing;

        var invite = new Invite
        {
            Token = GenerateToken(),
            GroupId = groupId,
            TargetMemberId = targetMemberId,
            CreatedBy = authService.RequireAccountId(),
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        var inserted = await client.From<Invite>().Insert(invite);
        return inserted.Model!;
    }

    public async Task<Invite> UpdateAsync(Invite invite)
    {
        var result = await client.From<Invite>()
            .Filter("id", Constants.Operator.Equals, invite.Id.ToString())
            .Update(invite);
        return result.Model!;
    }

    /// <summary>Plain [Column] properties (Token has no [PrimaryKey]) are always included in the
    /// insert payload at their C# default — here that's "", which silently overrides the table's
    /// `default encode(gen_random_bytes(9), 'base64url')` and made every invite ever created
    /// collide on token = "" past the first. Generate it client-side instead of trusting the
    /// column default to apply. ExpiresAt above has the same shape of bug: its unset C# default
    /// (DateTime.MinValue, 0001-01-01) was overriding the table's `now() + interval '7 days'`
    /// default on every insert, which made redeem_invite()'s `expires_at < now()` check always
    /// true — every invite ever created was "expired" the instant it was made, so no invite could
    /// ever actually be redeemed. Confirmed live: every row in `invites` had expires_at =
    /// 0001-01-01. Set explicitly here for the same reason Token is.</summary>
    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(9))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public async Task<Guid> RedeemAsync(string token)
    {
        var response = await client.Rpc("redeem_invite", new Dictionary<string, object> { { "p_token", token } });
        var raw = response.Content?.Trim('"')
            ?? throw new InvalidOperationException("redeem_invite returned no group id.");
        return Guid.Parse(raw);
    }
}
