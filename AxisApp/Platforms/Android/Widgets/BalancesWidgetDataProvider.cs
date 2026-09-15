using AxisApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AxisApp.Widgets;

internal record BalanceRow(Guid GroupId, string GroupName, string Currency, decimal Balance);

internal static class BalancesWidgetDataProvider
{
    /// <summary>groupFilter null = every group the account belongs to, sorted by |balance|
    /// descending so the most actionable rows lead; a specific groupId narrows to that one
    /// group's row (still going through the same "list of rows" shape the RemoteViewsFactory
    /// already expects, rather than a second single-row layout).</summary>
    public static async Task<List<BalanceRow>> GetSnapshotAsync(Guid? groupFilter)
    {
        var services = await WidgetDataAccess.GetReadyServicesAsync();
        if (services is null) return [];

        var authService = services.GetRequiredService<IAuthService>();
        if (!authService.IsAuthenticated) return [];

        var groupsRepository = services.GetRequiredService<IGroupsRepository>();
        var balancesRepository = services.GetRequiredService<IBalancesRepository>();

        var groups = await groupsRepository.GetMyGroupsAsync();
        var balances = await balancesRepository.GetMyBalancesAsync();
        var balanceByGroup = balances.ToDictionary(b => b.GroupId, b => b.Balance);

        var rows = groups
            .Where(g => groupFilter is null || g.Id == groupFilter)
            .Select(g => new BalanceRow(g.Id, g.Name, g.Currency, balanceByGroup.GetValueOrDefault(g.Id, 0m)))
            .OrderByDescending(r => Math.Abs(r.Balance))
            .ToList();

        return rows;
    }
}
