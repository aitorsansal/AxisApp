using AxisApp.Models;

namespace AxisApp.Services;

public interface IInvitesRepository
{
    /// <summary>Returns the most recent still-usable invite for this group/target (not expired,
    /// not used up) if one exists, otherwise creates a fresh one. Pass a target member to scope
    /// this to a "claim phantom" invite instead of the group's general join invite.</summary>
    Task<Invite> GetOrCreateAsync(Guid groupId, Guid? targetMemberId = null);

    /// <summary>Updates an existing invite's max_uses/expires_at. Pass the invite as returned by
    /// GetOrCreateAsync (with the desired fields changed) so server-set columns like CreatedBy/
    /// CreatedAt/UseCount round-trip unchanged.</summary>
    Task<Invite> UpdateAsync(Invite invite);

    /// <summary>
    /// Redeems an invite token as the current account, via the server-side redeem_invite
    /// RPC function (bypasses RLS safely since the redeemer isn't a group member yet).
    /// Returns the joined group's id.
    /// </summary>
    Task<Guid> RedeemAsync(string token);
}
