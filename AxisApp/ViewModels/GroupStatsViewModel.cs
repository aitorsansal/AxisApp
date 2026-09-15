using System.Collections.ObjectModel;
using System.Globalization;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace AxisApp.ViewModels;

/// <summary>How far back the Stats tab's charts look — a page-level filter (one control feeds
/// every chart, not a per-chart range) per the v2 design discussion.</summary>
public enum StatsDateRange { AllTime, Last3Months, Last6Months, Last12Months }

/// <summary>Stats tab of GroupDetailPage — pure read-only aggregation over a group's whole expense
/// history (see POSSIBLE_FEATURES.md's "Spending insights"): spend by category, category usage
/// frequency, spend by member (Paid/Share toggle), and settle-up cadence. No new schema/view — the
/// group's full Expense/ExpenseShare set is fetched once (IExpensesRepository.GetAllForGroupAsync)
/// and every chart is a LINQ aggregation over the same in-memory set, matching the "pure read,
/// cheapest item on the list" framing in POSSIBLE_FEATURES.md.
///
/// Lazily loaded — unlike ExpensesVm/EventsVm (which GroupDetailViewModel loads eagerly on every
/// group open), this only fetches on the Stats tab's first selection (GroupDetailViewModel calls
/// EnsureLoadedAsync from its tab-change handler), since most group visits never open this tab.
///
/// All-time only for v1 (no date-range picker) — see the chat design discussion this was built
/// from; revisit if a specific group's full history turns out to make the charts unreadable.</summary>
public partial class GroupStatsViewModel : BaseViewModel
{
    private readonly IExpensesRepository expensesRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IAliasesRepository aliasesRepository;

    /// <summary>Axis's own Primary accent (Colors.xaml) — these are single-measure magnitude
    /// charts (one bar per category/member/month), not multi-series identity charts, so a single
    /// consistent hue is the right call per the dataviz skill's form guidance (categorical color
    /// is for distinguishing series, not for decorating every bar of one series) — direct axis
    /// labels already carry the identity.</summary>
    private static readonly SKColor PrimaryColor = new(0x3D, 0x6B, 0xFF);

    /// <summary>Fixed-order categorical palette for the one genuinely multi-series chart on this
    /// tab (the category trend line) — validated with the dataviz skill's validate_palette.js
    /// against Axis's actual dark surface (BgPrimary #0B1220, not the skill's generic default):
    /// all 8 slots pass lightness/chroma/CVD-separation/normal-vision/contrast checks in that
    /// exact order. Never reassign slots per-chart-instance or cycle them — a category's color
    /// must stay the same slot every time it appears, so index by position in
    /// AppConstants.Categories.Keys, not by sort order within a single render.</summary>
    private static readonly SKColor[] CategoricalPalette =
    [
        new(0x39, 0x87, 0xE5), new(0xD9, 0x59, 0x26), new(0x19, 0x9E, 0x70), new(0xC9, 0x85, 0x00),
        new(0xD5, 0x51, 0x81), new(0x00, 0x83, 0x00), new(0x90, 0x85, 0xE9), new(0xE6, 0x67, 0x67)
    ];

    private static SKColor CategoryColor(string key)
    {
        var index = Array.IndexOf(AppConstants.Categories.Keys.ToArray(), string.IsNullOrEmpty(key) ? "other" : key);
        return CategoricalPalette[Math.Max(0, index) % CategoricalPalette.Length];
    }

    private Guid groupId;
    private bool hasLoadedOnce;
    private List<Expense> allExpenses = [];
    private List<ExpenseShare> allShares = [];
    private Dictionary<Guid, Member> membersById = new();
    private Dictionary<Guid, string> aliases = new();

    [ObservableProperty] private bool isBusy;
    /// <summary>True only while the very first load (for this tab instance) is in flight — same
    /// skeleton/minimum-visible-duration split GroupEventsViewModel uses.</summary>
    [ObservableProperty] private bool isInitialLoading;
    [ObservableProperty] private bool hasData;

    [ObservableProperty] private ObservableCollection<ISeries> categorySpendSeries = [];
    [ObservableProperty] private ObservableCollection<Axis> categorySpendYAxes = [];
    [ObservableProperty] private ObservableCollection<ISeries> categoryFrequencySeries = [];
    [ObservableProperty] private ObservableCollection<Axis> categoryFrequencyYAxes = [];
    [ObservableProperty] private ObservableCollection<ISeries> memberSpendSeries = [];
    [ObservableProperty] private ObservableCollection<Axis> memberSpendYAxes = [];
    [ObservableProperty] private ObservableCollection<ISeries> settlementCadenceSeries = [];
    [ObservableProperty] private ObservableCollection<Axis> settlementCadenceXAxes = [];
    [ObservableProperty] private ObservableCollection<ISeries> categoryTrendSeries = [];
    [ObservableProperty] private ObservableCollection<Axis> categoryTrendXAxes = [];

    /// <summary>Page-level filter feeding every chart on this tab. All-time by default — matches
    /// v1's original scope, still the right default for a group whose history fits on one screen
    /// without needing to narrow it.</summary>
    [ObservableProperty] private StatsDateRange selectedDateRange = StatsDateRange.AllTime;

    partial void OnSelectedDateRangeChanged(StatsDateRange value) => RebuildAll();

    /// <summary>false = Paid (default, "who's fronted the most"), true = Share ("who actually owes
    /// the most, regardless of who paid") — see the chat design discussion for why both framings
    /// matter. A view-only toggle, not persisted (unlike the Simplified/Pairwise balance mode,
    /// which is a deliberate long-lived per-group preference) — cheap enough to just default back
    /// to Paid on every visit.</summary>
    [ObservableProperty] private bool isShareModeSelected;

    partial void OnIsShareModeSelectedChanged(bool value) => RebuildMemberSpend();

    public GroupStatsViewModel(
        IExpensesRepository expensesRepository,
        IMembersRepository membersRepository,
        IAliasesRepository aliasesRepository)
    {
        this.expensesRepository = expensesRepository;
        this.membersRepository = membersRepository;
        this.aliasesRepository = aliasesRepository;
    }

    /// <summary>No-ops if this group's data is already loaded — called from GroupDetailViewModel
    /// on the Stats tab's first selection rather than eagerly alongside the other tabs.</summary>
    public Task EnsureLoadedAsync(Guid groupId) =>
        hasLoadedOnce && this.groupId == groupId ? Task.CompletedTask : LoadAsync(groupId);

    public Task LoadAsync(Guid groupId) => RunSafeAsync(async () =>
    {
        this.groupId = groupId;
        IsBusy = true;
        var isFirstLoad = !hasLoadedOnce;
        IsInitialLoading = isFirstLoad;
        try
        {
            async Task DoLoad()
            {
                var loadExpenses = expensesRepository.GetAllForGroupAsync(groupId);
                var loadMembers = membersRepository.GetForGroupAsync(groupId);
                var loadAliases = aliasesRepository.GetMyAliasesAsync();
                await Task.WhenAll(loadExpenses, loadMembers, loadAliases);

                allExpenses = loadExpenses.Result;
                var members = loadMembers.Result;
                membersById = members.ToDictionary(m => m.Id);
                aliases = loadAliases.Result;

                var expenseIds = allExpenses.Select(e => e.Id).ToList();
                allShares = await expensesRepository.GetSharesForExpensesAsync(expenseIds);

                HasData = allExpenses.Count > 0;
                RebuildAll();
            }

            if (isFirstLoad)
                await WithMinimumDurationAsync(TimeSpan.FromMilliseconds(400), DoLoad);
            else
                await DoLoad();
        }
        finally
        {
            IsBusy = false;
            IsInitialLoading = false;
            hasLoadedOnce = true;
        }
    });

    [RelayCommand]
    private Task Refresh() => LoadAsync(groupId);

    [RelayCommand]
    private void SelectPaidMode() => IsShareModeSelected = false;

    [RelayCommand]
    private void SelectShareMode() => IsShareModeSelected = true;

    [RelayCommand]
    private void SelectAllTime() => SelectedDateRange = StatsDateRange.AllTime;

    [RelayCommand]
    private void SelectLast3Months() => SelectedDateRange = StatsDateRange.Last3Months;

    [RelayCommand]
    private void SelectLast6Months() => SelectedDateRange = StatsDateRange.Last6Months;

    [RelayCommand]
    private void SelectLast12Months() => SelectedDateRange = StatsDateRange.Last12Months;

    private void RebuildAll()
    {
        RebuildCategorySpend();
        RebuildCategoryFrequency();
        RebuildMemberSpend();
        RebuildSettlementCadence();
        RebuildCategoryTrend();
    }

    /// <summary>allExpenses narrowed to SelectedDateRange — every Rebuild* method below reads from
    /// this (via NonSettlementExpenses), not allExpenses directly, so the date-range picker feeds
    /// every chart on the tab at once.</summary>
    private List<Expense> FilteredExpenses
    {
        get
        {
            if (SelectedDateRange == StatsDateRange.AllTime) return allExpenses;

            var months = SelectedDateRange switch
            {
                StatsDateRange.Last3Months => 3,
                StatsDateRange.Last6Months => 6,
                StatsDateRange.Last12Months => 12,
                _ => 0
            };
            var cutoff = DateTime.UtcNow.AddMonths(-months);
            return allExpenses.Where(e => e.OccurredAt >= cutoff).ToList();
        }
    }

    /// <summary>Excludes settlements (IsSettlement) from every "spend"/"category" stat below — a
    /// settle-up is a transfer between members, not money spent on anything, same treatment
    /// group_balances/pairwise_balances give it server-side.</summary>
    private List<Expense> NonSettlementExpenses => FilteredExpenses.Where(e => !e.IsSettlement).ToList();

    private void RebuildCategorySpend()
    {
        var byCategory = NonSettlementExpenses
            .GroupBy(e => e.Category)
            .Select(g => new { Label = CategoryDisplay.Label(g.Key), Amount = (double)g.Sum(e => e.AmountInGroupCurrency) })
            .OrderBy(x => x.Amount)
            .ToList();

        CategorySpendYAxes = new ObservableCollection<Axis> { new() { Labels = byCategory.Select(x => x.Label).ToArray() } };
        CategorySpendSeries = new ObservableCollection<ISeries>
        {
            new RowSeries<double> { Values = byCategory.Select(x => x.Amount).ToArray(), Fill = new SolidColorPaint(PrimaryColor) }
        };
    }

    private void RebuildCategoryFrequency()
    {
        var byCategory = NonSettlementExpenses
            .GroupBy(e => e.Category)
            .Select(g => new { Label = CategoryDisplay.Label(g.Key), Count = (double)g.Count() })
            .OrderBy(x => x.Count)
            .ToList();

        CategoryFrequencyYAxes = new ObservableCollection<Axis> { new() { Labels = byCategory.Select(x => x.Label).ToArray() } };
        CategoryFrequencySeries = new ObservableCollection<ISeries>
        {
            new RowSeries<double> { Values = byCategory.Select(x => x.Count).ToArray(), Fill = new SolidColorPaint(PrimaryColor) }
        };
    }

    private void RebuildMemberSpend()
    {
        var nonSettlementIds = NonSettlementExpenses.Select(e => e.Id).ToHashSet();

        List<(string Label, double Value)> byMember;
        if (IsShareModeSelected)
        {
            byMember = allShares
                .Where(s => nonSettlementIds.Contains(s.ExpenseId))
                .GroupBy(s => s.MemberId)
                .Select(g => (Label: MemberName(g.Key), Value: (double)g.Sum(s => s.ShareAmountInGroupCurrency)))
                .OrderBy(x => x.Value)
                .ToList();
        }
        else
        {
            byMember = NonSettlementExpenses
                .GroupBy(e => e.PaidByMemberId)
                .Select(g => (Label: MemberName(g.Key), Value: (double)g.Sum(e => e.AmountInGroupCurrency)))
                .OrderBy(x => x.Value)
                .ToList();
        }

        MemberSpendYAxes = new ObservableCollection<Axis> { new() { Labels = byMember.Select(x => x.Label).ToArray() } };
        MemberSpendSeries = new ObservableCollection<ISeries>
        {
            new RowSeries<double> { Values = byMember.Select(x => x.Value).ToArray(), Fill = new SolidColorPaint(PrimaryColor) }
        };
    }

    /// <summary>Chart axis labels have no room for a long name — a member added by their email
    /// (no display name/alias set) would otherwise take up most of the chart's width and squeeze
    /// the bars themselves. 11 chars + ellipsis matches what the chart area can comfortably show
    /// alongside the value.</summary>
    private const int MaxChartLabelLength = 11;

    private string MemberName(Guid memberId)
    {
        var name = membersById.TryGetValue(memberId, out var member) ? MemberDisplay.Name(member, aliases) : "?";
        return name.Length > MaxChartLabelLength ? name[..MaxChartLabelLength] + "…" : name;
    }

    private void RebuildSettlementCadence()
    {
        var byMonth = FilteredExpenses
            .Where(e => e.IsSettlement)
            .GroupBy(e => new DateTime(e.OccurredAt.ToLocalTime().Year, e.OccurredAt.ToLocalTime().Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new { Label = g.Key.ToString("MMM yyyy", CultureInfo.CurrentUICulture), Count = (double)g.Count() })
            .ToList();

        SettlementCadenceXAxes = new ObservableCollection<Axis> { new() { Labels = byMonth.Select(x => x.Label).ToArray() } };
        SettlementCadenceSeries = new ObservableCollection<ISeries>
        {
            new ColumnSeries<double> { Values = byMonth.Select(x => x.Count).ToArray(), Fill = new SolidColorPaint(PrimaryColor) }
        };
    }

    /// <summary>Spend by category, per month — the one genuinely multi-series chart on this tab
    /// (each category is its own LineSeries, sharing one month axis), so it's the one place the
    /// validated categorical palette applies. A category keeps the same palette slot every time it
    /// appears (CategoryColor indexes by position in AppConstants.Categories.Keys, not by this
    /// chart's own sort order) — "color follows the entity, never its rank" per the dataviz skill.</summary>
    private void RebuildCategoryTrend()
    {
        var nonSettlement = NonSettlementExpenses;
        var months = nonSettlement
            .Select(e => new DateTime(e.OccurredAt.ToLocalTime().Year, e.OccurredAt.ToLocalTime().Month, 1))
            .Distinct()
            .OrderBy(m => m)
            .ToList();

        var monthLabels = months.Select(m => m.ToString("MMM yyyy", CultureInfo.CurrentUICulture)).ToArray();
        var monthIndex = months.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);

        var series = new List<ISeries>();
        foreach (var categoryGroup in nonSettlement.GroupBy(e => e.Category).OrderBy(g => Array.IndexOf(AppConstants.Categories.Keys.ToArray(), string.IsNullOrEmpty(g.Key) ? "other" : g.Key)))
        {
            var values = new double[months.Count];
            foreach (var monthGroup in categoryGroup.GroupBy(e => new DateTime(e.OccurredAt.ToLocalTime().Year, e.OccurredAt.ToLocalTime().Month, 1)))
                values[monthIndex[monthGroup.Key]] = (double)monthGroup.Sum(e => e.AmountInGroupCurrency);

            var color = CategoryColor(categoryGroup.Key);
            series.Add(new LineSeries<double>
            {
                Name = CategoryDisplay.Label(categoryGroup.Key),
                Values = values,
                Stroke = new SolidColorPaint(color, 2),
                Fill = null,
                GeometryFill = new SolidColorPaint(color),
                GeometryStroke = new SolidColorPaint(color, 2),
                GeometrySize = 6
            });
        }

        CategoryTrendXAxes = new ObservableCollection<Axis> { new() { Labels = monthLabels } };
        CategoryTrendSeries = new ObservableCollection<ISeries>(series);
    }
}
