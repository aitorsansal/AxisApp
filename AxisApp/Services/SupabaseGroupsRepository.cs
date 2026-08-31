using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseGroupsRepository : IGroupsRepository
{
    private readonly Supabase.Client client;

    public SupabaseGroupsRepository(Supabase.Client client)
    {
        this.client = client;
    }

    /// <summary>No explicit filter needed — RLS's "select groups you belong to" policy already
    /// scopes this to the current account, so a plain Get() returns exactly "my groups".</summary>
    public async Task<List<Group>> GetMyGroupsAsync()
    {
        var result = await client.From<Group>().Get();
        return result.Models;
    }

    /// <summary>Creates the group and makes the creator an actual member of it (a fresh Member
    /// row plus a GroupMember join row, the same shape redeem_invite's "fresh join" branch
    /// creates for anyone else joining) via the create_group() Postgres function rather than
    /// three separate client-side inserts — Postgrest has no client-side transaction API, so
    /// three separate calls could fail partway through and leave an orphaned group with no
    /// members. create_group() runs as one Postgres transaction: if any step fails, all of it
    /// rolls back. See schema.sql's create_group remarks for why it doesn't need
    /// `security definer` the way redeem_invite() does.
    /// Returns just the new group's id (same "least-assuming" RPC response shape as
    /// SupabaseInvitesRepository.RedeemAsync — a scalar uuid, not a composite row, since that's
    /// the one Rpc response shape already confirmed against a real build in this codebase),
    /// followed by a normal typed fetch through the already-proven .Filter(...).Single() path
    /// every other repository uses.</summary>
    public async Task<Group> CreateAsync(string name)
    {
        var response = await client.Rpc("create_group", new Dictionary<string, object> { { "p_name", name } });
        var raw = response.Content?.Trim('"')
            ?? throw new InvalidOperationException("create_group returned no group id.");
        var groupId = Guid.Parse(raw);

        return await client.From<Group>()
            .Filter("id", Constants.Operator.Equals, groupId.ToString())
            .Single()
            ?? throw new InvalidOperationException("create_group succeeded but the group could not be re-fetched.");
    }

    public async Task<Group> GetByIdAsync(Guid groupId)
    {
        return await client.From<Group>()
            .Filter("id", Constants.Operator.Equals, groupId.ToString())
            .Single()
            ?? throw new InvalidOperationException("Group not found.");
    }

    /// <summary>Same "least-assuming" Rpc call shape as CreateAsync/RedeemAsync — parameters
    /// passed as strings even for a uuid column, since that's the one shape already confirmed
    /// against a real build in this codebase. leave_group() returns void, so there's no response
    /// body to parse; a thrown Postgrest exception (guard failures inside the function raise a
    /// plain SQL exception) surfaces as-is to the caller.</summary>
    public async Task LeaveAsync(Guid groupId)
    {
        await client.Rpc("leave_group", new Dictionary<string, object> { { "p_group_id", groupId.ToString() } });
    }

    public async Task TransferOwnershipAsync(Guid groupId, Guid newOwnerMemberId)
    {
        await client.Rpc("transfer_group_ownership", new Dictionary<string, object>
        {
            { "p_group_id", groupId.ToString() },
            { "p_new_owner_member_id", newOwnerMemberId.ToString() }
        });
    }

    /// <summary>No RPC needed — the "delete own groups" RLS policy already permits this for the
    /// creator, and the FK cascade shape (group_members/invites cascade, payments/expenses/
    /// recurring_expenses set group_id null) already does the right thing. See schema.sql.</summary>
    public async Task DeleteAsync(Guid groupId)
    {
        await client.From<Group>()
            .Filter("id", Constants.Operator.Equals, groupId.ToString())
            .Delete();
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid memberId)
    {
        await client.Rpc("remove_group_member", new Dictionary<string, object>
        {
            { "p_group_id", groupId.ToString() },
            { "p_member_id", memberId.ToString() }
        });
    }
}
