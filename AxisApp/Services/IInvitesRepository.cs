using AxisApp.Models;

namespace AxisApp.Services;

public interface IInvitesRepository
{
    /// <summary>Creates an invite for a group. Pass a target member to make this a "claim phantom" invite.</summary>
    Task<Invite> CreateAsync(Guid groupId, Guid? targetMemberId = null);

    /// <summary>
    /// Redeems an invite token as the current account, via the server-side redeem_invite
    /// RPC function (bypasses RLS safely since the redeemer isn't a group member yet).
    /// Returns the joined group's id.
    /// </summary>
    Task<Guid> RedeemAsync(string token);
}
