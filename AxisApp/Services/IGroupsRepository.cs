using AxisApp.Models;

namespace AxisApp.Services;

public interface IGroupsRepository
{
    /// <summary>Groups the current account is a member of.</summary>
    Task<List<Group>> GetMyGroupsAsync();
    Task<Group> CreateAsync(string name);
}
