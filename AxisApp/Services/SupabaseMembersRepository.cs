using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

/// <summary>
/// Supabase-backed IMembersRepository. Like SupabaseAuthService, the exact Postgrest query
/// call shapes across every Supabase*Repository in this folder (.Filter/.Insert/.Update/.Get/
/// .Single, the Constants.Operator/Ordering enum members) are grounded against the
/// postgrest-csharp source (github.com/supabase-community/postgrest-csharp), not a local build
/// of this exact installed version — report back the exact compiler error if anything doesn't
/// match, same as with SupabaseAuthService.
/// </summary>
public class SupabaseMembersRepository : IMembersRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseMembersRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    public async Task<List<Member>> GetForGroupAsync(Guid groupId)
    {
        var groupMembers = await client.From<GroupMember>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Get();

        var memberIds = groupMembers.Models.Select(gm => gm.MemberId.ToString()).ToList();
        if (memberIds.Count == 0) return [];

        var members = await client.From<Member>()
            .Filter("id", Constants.Operator.In, memberIds)
            .Get();

        return members.Models;
    }

    public async Task<Member?> GetByIdAsync(Guid memberId) =>
        await client.From<Member>()
            .Filter("id", Constants.Operator.Equals, memberId.ToString())
            .Single();

    public async Task<Member> AddPhantomAsync(string displayName)
    {
        var member = new Member
        {
            DisplayName = displayName,
            CreatedBy = authService.RequireAccountId()
        };

        var result = await client.From<Member>().Insert(member);
        return result.Model!;
    }

    public async Task AddToGroupAsync(Guid groupId, Guid memberId)
    {
        await client.From<GroupMember>().Insert(new GroupMember
        {
            GroupId = groupId,
            MemberId = memberId
        });
    }
}
