using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseAliasesRepository : IAliasesRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseAliasesRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    public async Task<Dictionary<Guid, string>> GetMyAliasesAsync()
    {
        var result = await client.From<MemberAlias>().Get();
        return result.Models.ToDictionary(a => a.MemberId, a => a.Alias);
    }

    /// <summary>Delete-then-insert rather than .Update(model) — MemberAlias only marks OwnerId as
    /// [PrimaryKey] (same shape as GroupMember/ExpenseShare's real composite keys), so trusting an
    /// implicit PK match would silently update every alias row this account owns instead of just
    /// this member's.</summary>
    public async Task SetAliasAsync(Guid memberId, string alias)
    {
        await ClearAliasAsync(memberId);
        await client.From<MemberAlias>().Insert(new MemberAlias
        {
            OwnerId = authService.RequireAccountId(),
            MemberId = memberId,
            Alias = alias
        });
    }

    public async Task ClearAliasAsync(Guid memberId)
    {
        await client.From<MemberAlias>()
            .Filter("member_id", Constants.Operator.Equals, memberId.ToString())
            .Delete();
    }
}
