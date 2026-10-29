using System.Collections.ObjectModel;
using System.Globalization;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

public sealed record SettlementMemberOption(Member Member, string Name);

public partial class SettlementViewModel(
    IGroupsRepository groupsRepository,
    IMembersRepository membersRepository,
    IExpensesRepository expensesRepository,
    IBalancesRepository balancesRepository,
    IAliasesRepository aliasesRepository,
    IAuthService authService) : BaseViewModel, IQueryAttributable, IUnsavedChangesEditor
{
    private Guid groupId;
    private readonly UnsavedEditGuard unsavedEdits = new();
    public bool HasPendingEdits => IsBusy && IsReady;
    public bool ShouldConfirmLeaving => unsavedEdits.ShouldBlock(CaptureSnapshot(), HasPendingEdits);
    private string CaptureSnapshot() => FormEditorSnapshots.Settlement(new SettlementEditorState(
        AmountText, Note, OccurredOn, SelectedPayer?.Member.Id, SelectedRecipient?.Member.Id, Currency));
    private Guid myMemberId;
    private Guid? otherMemberId;
    private decimal? expectedBalance;
    private Expense? original;
    private string groupCurrency = "EUR";
    private readonly LocalizationResourceManager loc = LocalizationResourceManager.Instance;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInteract), nameof(CanCancel))]
    private bool isBusy;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInteract), nameof(ShowForm))]
    private bool isReady;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanInteract), nameof(ShowForm), nameof(CloseLabel))]
    private bool hasSaved;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSummary))]
    private bool isEditMode;
    [ObservableProperty] private bool canDelete;
    [ObservableProperty] private string pageTitle = "";
    [ObservableProperty] private string saveLabel = "";
    [ObservableProperty] private string amountText = "";
    [ObservableProperty] private string note = "";
    [ObservableProperty] private DateTime occurredOn = DateTime.Today;
    [ObservableProperty] private string outstandingText = "";
    [ObservableProperty] private string successMessage = "";
    [ObservableProperty] private string remainingMessage = "";
    [ObservableProperty] private ObservableCollection<SettlementMemberOption> memberOptions = [];
    [ObservableProperty] private ObservableCollection<SettlementMemberOption> recipientOptions = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PayerName), nameof(NotePlaceholder))]
    private SettlementMemberOption? selectedPayer;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RecipientName), nameof(NotePlaceholder))]
    private SettlementMemberOption? selectedRecipient;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AmountLabel))]
    private int selectedCurrencyIndex;

    public IReadOnlyList<string> CurrencyOptions { get; } = AppConstants.Currencies.All.Select(c => c.Code).ToList();
    public bool CanInteract => IsReady && !IsBusy && !HasSaved;
    public bool CanCancel => !IsBusy;
    public bool ShowForm => IsReady && !HasSaved;
    public bool ShowSummary => !IsEditMode;
    public string CloseLabel => loc[HasSaved ? "Settlement_Done" : "Common_Cancel"];
    public string PayerName => SelectedPayer?.Name ?? "";
    public string RecipientName => SelectedRecipient?.Name ?? "";
    private string Currency => SelectedCurrencyIndex >= 0 && SelectedCurrencyIndex < CurrencyOptions.Count
        ? CurrencyOptions[SelectedCurrencyIndex] : groupCurrency;
    public string AmountLabel => $"{loc["AddExpense_AmountLabel"]} ({Currency})";
    public string NotePlaceholder => loc.Format("Settlement_DefaultDescription",
        SelectedPayer?.Member.Id == myMemberId ? RecipientName
        : SelectedRecipient?.Member.Id == myMemberId ? PayerName : RecipientName);

    partial void OnSelectedPayerChanged(SettlementMemberOption? value)
    {
        RecipientOptions = new(MemberOptions.Where(m => m.Member.Id != value?.Member.Id));
        if (SelectedRecipient?.Member.Id == value?.Member.Id) SelectedRecipient = null;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("groupId", out var group) || !Guid.TryParse(group?.ToString(), out var id)) return;
        Guid? expenseId = query.TryGetValue("expenseId", out var expense) && Guid.TryParse(expense?.ToString(), out var e) ? e : null;
        Guid? otherId = query.TryGetValue("otherMemberId", out var other) && Guid.TryParse(other?.ToString(), out var o) ? o : null;
        _ = LoadAsync(id, expenseId, otherId);
    }

    public Task LoadAsync(Guid id, Guid? expenseId, Guid? otherId) => RunSafeAsync(async () =>
    {
        groupId = id;
        unsavedEdits.Reset();
        IsBusy = true;
        IsReady = false;
        HasSaved = false;
        IsEditMode = expenseId is not null;
        PageTitle = loc[IsEditMode ? "AddExpense_EditSettlementTitle" : "Settlement_Record"];
        SaveLabel = loc[IsEditMode ? "Settlement_Save" : "Settlement_Record"];
        try
        {
            var loadGroup = groupsRepository.GetByIdAsync(id);
            var loadMembers = membersRepository.GetForGroupAsync(id);
            var loadAliases = aliasesRepository.GetMyAliasesAsync();
            await Task.WhenAll(loadGroup, loadMembers, loadAliases);
            var group = await loadGroup;
            var members = await loadMembers;
            var aliases = await loadAliases;
            if (authService.CurrentAccountId is not { } accountId)
            { ErrorMessage = loc["Settlement_Unavailable"]; return; }
            var me = members.FirstOrDefault(m => m.AccountId == accountId);
            if (me is null) { ErrorMessage = loc["Settlement_Unavailable"]; return; }
            myMemberId = me.Id;
            groupCurrency = group.Currency;
            MemberOptions = new(members.Select(m => new SettlementMemberOption(m, MemberDisplay.Name(m, aliases))));
            expectedBalance = null;
            OutstandingText = "";
            CanDelete = false;
            original = null;

            Guid payerId, recipientId;
            string currency;
            if (expenseId is { } existingId)
            {
                var loadExpense = expensesRepository.GetByIdAsync(existingId);
                var loadShares = expensesRepository.GetSharesAsync(existingId);
                await Task.WhenAll(loadExpense, loadShares);
                var expense = await loadExpense;
                var shares = await loadShares;
                if (expense is null || expense.GroupId != id || !expense.IsSettlement || shares.Count != 1)
                { ErrorMessage = loc["Settlement_Unavailable"]; return; }
                original = expense;
                payerId = expense.PaidByMemberId;
                recipientId = shares[0].MemberId;
                currency = expense.Currency;
                AmountText = expense.Amount.ToString("0.00", CultureInfo.InvariantCulture);
                Note = expense.Description;
                OccurredOn = expense.OccurredAt.ToLocalTime().Date;
                CanDelete = expense.CreatedBy == authService.CurrentAccountId || group.CreatedBy == authService.CurrentAccountId
                    || members.Any(m => m.Id == payerId && m.AccountId == authService.CurrentAccountId);
            }
            else
            {
                var balance = (await balancesRepository.GetMyPairwiseForGroupAsync(id))
                    .FirstOrDefault(b => b.OtherMemberId == otherId)?.Balance ?? 0;
                if (otherId is null || otherId == myMemberId || balance == 0)
                { ErrorMessage = loc["Settlement_Unavailable"]; return; }
                expectedBalance = balance;
                (payerId, recipientId) = SettlementRules.Parties(myMemberId, otherId.Value, balance);
                currency = group.Currency;
                AmountText = Math.Abs(balance).ToString("0.00", CultureInfo.InvariantCulture);
                Note = "";
                OccurredOn = DateTime.Today;
                OutstandingText = loc.Format("Settlement_Outstanding", Money(Math.Abs(balance)));
            }
            SelectedPayer = MemberOptions.FirstOrDefault(m => m.Member.Id == payerId);
            SelectedRecipient = RecipientOptions.FirstOrDefault(m => m.Member.Id == recipientId);
            if (SelectedPayer is null || SelectedRecipient is null)
            { ErrorMessage = loc["Settlement_InvalidParties"]; return; }
            SelectedCurrencyIndex = CurrencyOptions.ToList().IndexOf(currency);
            if (SelectedCurrencyIndex < 0) { ErrorMessage = loc["Settlement_Unavailable"]; return; }
            otherMemberId = otherId ?? (payerId == myMemberId ? recipientId : recipientId == myMemberId ? payerId : null);
            IsReady = true;
            unsavedEdits.Capture(CaptureSnapshot());
        }
        finally { IsBusy = false; }
    });

    private string Money(decimal amount) => $"{amount.ToString("N2", CultureInfo.GetCultureInfo(loc.CurrentLanguage == "es" ? "es-ES" : "en-US"))} {groupCurrency}";

    [RelayCommand]
    private Task Save() => RunSafeAsync(async () =>
    {
        if (!CanInteract) return;
        if (!SettlementRules.TryAmount(AmountText, out var amount))
        { ErrorMessage = loc[AmountInput.MessageKey(AmountText)]; return; }
        if (SelectedPayer is null || SelectedRecipient is null || SelectedPayer.Member.Id == SelectedRecipient.Member.Id)
        { ErrorMessage = loc["Settlement_InvalidParties"]; return; }
        if (expectedBalance is { } maximum && amount > Math.Abs(maximum))
        { ErrorMessage = loc["Settlement_ExceedsBalance"]; return; }

        IsBusy = true;
        try
        {
            if (expectedBalance is { } expected && otherMemberId is { } other)
            {
                var balance = (await balancesRepository.GetMyPairwiseForGroupAsync(groupId))
                    .FirstOrDefault(b => b.OtherMemberId == other)?.Balance ?? 0;
                if (balance != expected) { ErrorMessage = loc["Settlement_BalanceChanged"]; return; }
            }
            var expense = new Expense
            {
                Id = original?.Id ?? Guid.Empty,
                GroupId = groupId,
                PaidByMemberId = SelectedPayer.Member.Id,
                Amount = amount,
                Currency = Currency,
                Description = SettlementRules.Description(Note, NotePlaceholder),
                Category = "",
                OccurredAt = SettlementRules.Timestamp(OccurredOn, original?.OccurredAt),
                IsSettlement = true,
                CreatedBy = original?.CreatedBy,
                CreatedAt = original?.CreatedAt ?? default,
                ReceiptPath = original?.ReceiptPath,
                EventId = original?.EventId,
            };
            var shares = new List<ExpenseShare> { new() { MemberId = SelectedRecipient.Member.Id, ShareAmount = amount } };
            try
            {
                if (original is null) await expensesRepository.AddAsync(expense, shares);
                else await expensesRepository.UpdateAsync(expense, shares);
            }
            catch (ExpenseSaveCommittedException) { }
            HasSaved = true;
            unsavedEdits.MarkCommitted();
            SuccessMessage = loc["Settlement_Recorded"];
            RemainingMessage = "";
            var counterparty = SelectedPayer.Member.Id == myMemberId ? SelectedRecipient : SelectedRecipient.Member.Id == myMemberId ? SelectedPayer : null;
            if (counterparty is not null)
            {
                try
                {
                    var balance = (await balancesRepository.GetMyPairwiseForGroupAsync(groupId))
                        .FirstOrDefault(b => b.OtherMemberId == counterparty.Member.Id)?.Balance ?? 0;
                    RemainingMessage = loc.Format("Settlement_Remaining", counterparty.Name, Money(Math.Abs(balance)));
                }
                catch { }
            }
        }
        catch (Exception ex) { ErrorMessage = GetErrorMessage(ex, loc["Settlement_SaveFailed"]); }
        finally { IsBusy = false; }
    });

    [RelayCommand]
    private Task Delete() => RunSafeAsync(async () =>
    {
        if (!CanInteract || !CanDelete || original is null) return;
        IsBusy = true;
        try
        {
            if (!await Shell.Current.DisplayAlertAsync(loc["Settlement_Delete"], loc["Settlement_DeleteConfirm"], loc["Common_Yes"], loc["Common_Cancel"])) return;
            await expensesRepository.DeleteAsync(original.Id);
            HasSaved = true;
            unsavedEdits.MarkCommitted();
            SuccessMessage = loc["Settlement_Deleted"];
            RemainingMessage = "";
        }
        catch { ErrorMessage = loc["Settlement_DeleteFailed"]; }
        finally { IsBusy = false; }
    });

    [RelayCommand]
    private Task Cancel() => RunSafeAsync(async () =>
    {
        if (CanCancel) await Shell.Current.GoToAsync("..");
    });
}
