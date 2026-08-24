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

    public async Task<Group> CreateAsync(string name)
    {
        var group = new Group
        {
            Name = name,
            CreatedBy = authService.RequireAccountId()
        };

        var result = await client.From<Group>().Insert(group);
        return result.Model!;
    }
}
