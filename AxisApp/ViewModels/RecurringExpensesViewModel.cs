using System.Collections.ObjectModel;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One row in the repeating-expenses list. NextDueText is simple client-side date math
/// off StartDate/LastProcessedDate/Frequency — informational only, since nothing materializes a
/// template yet (the pg_cron job is a deliberate follow-up, not built as part of this feature —
/// see CLAUDE.md's "Recurring expenses" remarks).</summary>
public partial class RecurringExpenseRowItem : ObservableObject
{
    public RecurringExpense Template { get; init; } = null!;
    public string Description { get; init; } = "";
    public string AmountText { get; init; } = "";
    public string PayerName { get; init; } = "";
    public string FrequencyLabel { get; init; } = "";
    public string NextDueText { get; init; } = "";

    [ObservableProperty] private bool isActive;
}

public partial class RecurringExpensesViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IRecurringExpensesRepository recurringExpensesRepository;
    private readonly IMembersRepository membersRepository;
    private readonly IAliasesRepository aliasesRepository;

    private Guid groupId;

    [ObservableProperty] private string groupName = "";
    [ObservableProperty] private ObservableCollection<RecurringExpenseRowItem> templates = [];
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isEmpty;

    public RecurringExpensesViewModel(
        IRecurringExpensesRepository recurringExpensesRepository,
        IMembersRepository membersRepository,
        IAliasesRepository aliasesRepository)
    {
        this.recurringExpensesRepository = recurringExpensesRepository;
        this.membersRepository = membersRepository;
        this.aliasesRepository = aliasesRepository;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("groupName", out var nameValue))
            GroupName = Uri.UnescapeDataString(nameValue?.ToString() ?? "");

        if (query.TryGetValue("groupId", out var idValue) && Guid.TryParse(idValue?.ToString(), out var id))
        {
            groupId = id;
            _ = LoadAsync();
        }
    }

    public Task LoadAsync() => RunSafeAsync(async () =>
    {
        IsBusy = true;
        try
        {
            var loadTemplates = recurringExpensesRepository.GetForGroupAsync(groupId);
            var loadMembers = membersRepository.GetForGroupAsync(groupId);
            var loadAliases = aliasesRepository.GetMyAliasesAsync();
            await Task.WhenAll(loadTemplates, loadMembers, loadAliases);

            var membersById = loadMembers.Result.ToDictionary(m => m.Id);
            var aliases = loadAliases.Result;
            var loc = LocalizationResourceManager.Instance;

            Templates = new ObservableCollection<RecurringExpenseRowItem>(
                loadTemplates.Result.Select(t => new RecurringExpenseRowItem
                {
                    Template = t,
                    Description = string.IsNullOrWhiteSpace(t.Description)
                        ? (string.IsNullOrEmpty(t.Category) ? "" : loc[$"Category_{t.Category}"])
                        : t.Description,
                    AmountText = $"€{t.Amount:0.00}",
                    PayerName = membersById.TryGetValue(t.PaidByMemberId, out var payer)
                        ? MemberDisplay.Name(payer, aliases) : "",
                    FrequencyLabel = loc[$"Recurring_Frequency_{CapitalizeFirst(t.Frequency)}"],
                    NextDueText = FormatNextDue(t),
                    IsActive = t.IsActive
                }));

            IsEmpty = Templates.Count == 0;
        }
        finally
        {
            IsBusy = false;
        }
    });

    private static string CapitalizeFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>Purely informational client-side estimate (no cron consumes this yet) — the day
    /// after LastProcessedDate stepped by Frequency once, or StartDate itself if nothing has ever
    /// materialized.</summary>
    private static string FormatNextDue(RecurringExpense template)
    {
        var basis = template.LastProcessedDate ?? template.StartDate.AddDays(-1);
        var next = template.Frequency switch
        {
            "daily" => basis.AddDays(1),
            "weekly" => basis.AddDays(7),
            "monthly" => basis.AddMonths(1),
            "yearly" => basis.AddYears(1),
            _ => basis
        };
        if (next < template.StartDate) next = template.StartDate;
        return next.ToString("MMM d, yyyy");
    }

    [RelayCommand]
    private Task AddRecurring() => RunSafeAsync(() =>
        Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}&recurring=true"));

    [RelayCommand]
    private Task EditRecurring(RecurringExpenseRowItem? item) => RunSafeAsync(() =>
    {
        if (item is null) return Task.CompletedTask;
        return Shell.Current.GoToAsync($"{AppConstants.Routes.AddExpense}?groupId={groupId}&recurringExpenseId={item.Template.Id}");
    });

    [RelayCommand]
    private Task ToggleActive(RecurringExpenseRowItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;
        var newValue = !item.IsActive;
        await recurringExpensesRepository.SetActiveAsync(item.Template.Id, newValue);
        item.IsActive = newValue;
    });

    [RelayCommand]
    private Task DeleteRecurring(RecurringExpenseRowItem? item) => RunSafeAsync(async () =>
    {
        if (item is null) return;

        var loc = LocalizationResourceManager.Instance;
        var confirmed = await Shell.Current.DisplayAlert(
            loc["RecurringExpenses_DeleteConfirmTitle"],
            loc["RecurringExpenses_DeleteConfirmMessage"],
            loc["Common_Yes"],
            loc["Common_Cancel"]);
        if (!confirmed) return;

        await recurringExpensesRepository.DeleteAsync(item.Template.Id);
        await LoadAsync();
    });

    [RelayCommand]
    private Task Refresh() => LoadAsync();
}
