using AxisApp.Models;

namespace AxisApp.Services;

public class SupabaseGroupsRepository : IGroupsRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseGroupsRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    /// <summary>No explicit filter needed — RLS's "select groups you belong to" policy already
    /// scopes this to the current account, so a plain Get() returns exactly "my groups".</summary>
    public async Task<List<Group>> GetMyGroupsAsync()
    {
        var result = await client.From<Group>().Get();
        return result.Models;
    }

    /// <summary>Creates the group, then makes the creator an actual member of it — a fresh
    /// Member row plus a GroupMember join row, the same shape redeem_invite's "fresh join" branch
    /// creates for anyone else joining. Without this the group would be invisible to its own
    /// creator afterward: "select groups you belong to" requires a real group_members row, and
    /// creating public.groups alone doesn't imply membership.</summary>
    public async Task<Group> CreateAsync(string name)
    {
        var accountId = authService.RequireAccountId();

        var group = new Group { Name = name, CreatedBy = accountId };
        var insertedGroup = await client.From<Group>().Insert(group);
        var createdGroup = insertedGroup.Model!;

        var member = new Member
        {
            AccountId = accountId,
            DisplayName = authService.CurrentEmail ?? "New member",
            CreatedBy = accountId
        };
        var insertedMember = await client.From<Member>().Insert(member);

        await client.From<GroupMember>().Insert(new GroupMember
        {
            GroupId = createdGroup.Id,
            MemberId = insertedMember.Model!.Id
        });

        return createdGroup;
    }
}
