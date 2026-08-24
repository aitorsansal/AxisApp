using AxisApp.Models;

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

    public async Task<Invite> CreateAsync(Guid groupId, Guid? targetMemberId = null)
    {
        var invite = new Invite
        {
            GroupId = groupId,
            TargetMemberId = targetMemberId,
            CreatedBy = authService.RequireAccountId()
        };

        var result = await client.From<Invite>().Insert(invite);
        return result.Model!;
    }

    public async Task<Guid> RedeemAsync(string token)
    {
        var response = await client.Rpc("redeem_invite", new Dictionary<string, object> { { "p_token", token } });
        var raw = response.Content?.Trim('"')
            ?? throw new InvalidOperationException("redeem_invite returned no group id.");
        return Guid.Parse(raw);
    }
}
