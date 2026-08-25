using AxisApp.Models;
using Supabase.Postgrest;

namespace AxisApp.Services;

public class SupabaseCategoriesRepository : ICategoriesRepository
{
    private readonly Supabase.Client client;
    private readonly IAuthService authService;

    public SupabaseCategoriesRepository(Supabase.Client client, IAuthService authService)
    {
        this.client = client;
        this.authService = authService;
    }

    public async Task<List<Category>> GetAllAsync()
    {
        var result = await client.From<Category>().Get();
        return result.Models;
    }

    public async Task<Category> EnsureByNameAsync(string name)
    {
        var existing = await client.From<Category>()
            .Filter("name", Constants.Operator.Equals, name)
            .Single();
        if (existing is not null) return existing;

        var category = new Category { Name = name, CreatedBy = authService.RequireAccountId() };
        var result = await client.From<Category>().Insert(category);
        return result.Model!;
    }
}
