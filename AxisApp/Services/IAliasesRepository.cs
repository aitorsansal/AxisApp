namespace AxisApp.Services;

/// <summary>Private, per-account nickname overrides for how a member is displayed — see
/// Services/MemberDisplay.cs, which is the only place that should consume these.</summary>
public interface IAliasesRepository
{
    /// <summary>Every alias the current account has ever set, keyed by member_id. No group_id
    /// filter needed — RLS already scopes member_aliases to owner_id = auth.uid(), and the set is
    /// realistically small (however many people one account has personally renamed).</summary>
    Task<Dictionary<Guid, string>> GetMyAliasesAsync();

    Task SetAliasAsync(Guid memberId, string alias);
    Task ClearAliasAsync(Guid memberId);
}
