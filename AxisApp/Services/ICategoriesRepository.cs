using AxisApp.Models;

namespace AxisApp.Services;

public interface ICategoriesRepository
{
    Task<List<Category>> GetAllAsync();
    Task<Category> EnsureByNameAsync(string name);
}
