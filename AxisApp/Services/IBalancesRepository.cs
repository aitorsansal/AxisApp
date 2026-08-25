using AxisApp.Models;

namespace AxisApp.Services;

public interface IBalancesRepository
{
    /// <summary>Net balance per member for a group, from the group_balances view.</summary>
    Task<List<GroupBalance>> GetForGroupAsync(Guid groupId);

    /// <summary>The current account's own net balance in every group it belongs to, from the
    /// my_group_balances view. Used by the Groups list to show a balance summary per group.</summary>
    Task<List<MyGroupBalance>> GetMyBalancesAsync();

    /// <summary>The current account's real, two-party balance with every other member it has
    /// actually shared money with in this group, from the my_pairwise_balances view. Used by
    /// Group Detail's "pairwise" balance display mode.</summary>
    Task<List<MyPairwiseBalance>> GetMyPairwiseForGroupAsync(Guid groupId);
}
