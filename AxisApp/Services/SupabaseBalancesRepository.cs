using AxisApp.Models;
using Postgrest;

namespace AxisApp.Services;

public class SupabaseBalancesRepository : IBalancesRepository
{
    private readonly Supabase.Client client;

    public SupabaseBalancesRepository(Supabase.Client client)
    {
        this.client = client;
    }

    public async Task<List<GroupBalance>> GetForGroupAsync(Guid groupId)
    {
        var result = await client.From<GroupBalance>()
            .Filter("group_id", Constants.Operator.Equals, groupId.ToString())
            .Get();

        return result.Models;
    }
}
