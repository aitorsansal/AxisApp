using AxisApp.Models;

namespace AxisApp.Services;

public interface IBalancesRepository
{
    /// <summary>Net balance per member for a group, from the group_balances view.</summary>
    Task<List<GroupBalance>> GetForGroupAsync(Guid groupId);
}
