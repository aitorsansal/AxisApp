using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using AxisApp.Localization;
using AxisApp.Models;
using AxisApp.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AxisApp.ViewModels;

/// <summary>One group member's row in the split: whether they're in on this expense, and what
/// they owe toward it.</summary>
public partial class ExpenseParticipant : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Initials { get; init; } = "";

    [ObservableProperty] private bool isIncluded = true;
    [ObservableProperty] private decimal owes;
    [ObservableProperty] private string owesText = "0.00";

    private bool syncing;

    [RelayCommand]
    private void ToggleIncluded() => IsIncluded = !IsIncluded;

    partial void OnOwesTextChanged(string value)
    {
        if (syncing) return;
        syncing = true;
        try
        {
            Owes = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }
        finally { syncing = false; }
    }

    partial void OnOwesChanged(decimal value)
    {
        if (syncing) return;
        syncing = true;
        try { OwesText = value.ToString("0.00", CultureInfo.InvariantCulture); }
        finally { syncing = false; }
    }
}

/// <summary>One selectable option in the "Paid by" row.</summary>
public partial class PayerOption : ObservableObject
{
    public Member Member { get; init; } = null!;
    public string Initials { get; init; } = "";

    [ObservableProperty] private bool isSelected;
}

/// <summary>One selectable chip in the category row. Key is the stable, language-independent
/// identifier stored on Expense.Category; Name is Key's label in the viewer's current language,
/// resolved once when the chip list is built.</summary>
public partial class CategoryChip : ObservableObject
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";

    [ObservableProperty] private bool isSelected;
}

/// <summary>
/// N-way expense entry: any group member can be the payer (paid_by_member_id on Expense isn't
/// restricted to "the current user" the way DebtTracker's payer selection effectively was — see
/// SCOPE.md's write-up of that app's split logic). Defaults to splitting the amount equally
/// across every group member; toggling a participant off or hand-editing one share switches into
/// manual mode via IsManualSplit, same escape hatch DebtTracker used.
/// </summary>
public partial class AddExpenseViewModel : BaseViewModel, IQueryAttributable
{
    private readonly IMembersRepository membersRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private Guid? editingExpenseId;
    private bool redistributing;

    [ObservableProperty] private ObservableCollection<ExpenseParticipant> participants = [];
    [ObservableProperty] private ObservableCollection<PayerOption> payerOptions = [];
    [ObservableProperty] private ObservableCollection<CategoryChip> categoryChips = [];
    [ObservableProperty] private Member? selectedPayer;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string amountText = "0.00";
    [ObservableProperty] private decimal remaining;
    [ObservableProperty] private string remainingText = "";
    [ObservableProperty] private bool canSave;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string selectedCategory = string.Empty;
    [ObservableProperty] private DateTime occurredOn = DateTime.Today;
    [ObservableProperty] private string occurredOnDisplay = LocalizationResourceManager.Instance["Common_Today"];
    [ObservableProperty] private bool isManualSplit;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isEditMode;
    [ObservableProperty] private string pageTitle = LocalizationResourceManager.Instance["AddExpense_Title"];

    public AddExpenseViewModel(
        IMembersRepository membersRepository,
        IExpensesRepository expensesRepository,
        IAuthService authService)
    {
        this.membersRepository = membersRepository;
        this.expensesRepository = expensesRepository;
        this.authService = authService;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Guid? expenseId = query.TryGetValue("expenseId", out var expenseValue)
            && Guid.TryParse(expenseValue?.ToString(), out var parsedExpenseId)
                ? parsedExpenseId
                : null;

        if (query.TryGetValue("groupId", out var groupValue) && Guid.TryParse(groupValue?.ToString(), out var groupIdValue))
            _ = LoadAsync(groupIdValue, expenseId);
    }

    /// <summary>Loads the group's members as the participant set and its category list. In add
    /// mode (forExpenseId omitted) everyone defaults to an equal split; in edit mode, the existing
    /// expense's amount/description/category/payer/date and per-member shares are loaded on top,
    /// overriding the equal-split defaults built for the member list.</summary>
    public Task LoadAsync(Guid forGroupId, Guid? forExpenseId = null) => RunSafeAsync(async () =>
    {
        groupId = forGroupId;
        editingExpenseId = forExpenseId;
        IsEditMode = forExpenseId is not null;
        PageTitle = LocalizationResourceManager.Instance[IsEditMode ? "AddExpense_EditTitle" : "AddExpense_Title"];

        IsBusy = true;
        try
        {
            var loadMembers = membersRepository.GetForGroupAsync(groupId);
            var loadExpense = forExpenseId is { } id ? expensesRepository.GetByIdAsync(id) : Task.FromResult<Expense?>(null);
            var loadShares = forExpenseId is { } sharesId ? expensesRepository.GetSharesAsync(sharesId) : Task.FromResult(new List<ExpenseShare>());
            await Task.WhenAll(loadMembers, loadExpense, loadShares);

            foreach (var participant in Participants)
                participant.PropertyChanged -= ParticipantChanged;

            Participants = new ObservableCollection<ExpenseParticipant>();
            PayerOptions = new ObservableCollection<PayerOption>();

            foreach (var member in loadMembers.Result)
            {
                var initials = Initials(member.DisplayName);

                var participant = new ExpenseParticipant { Member = member, Initials = initials };
                participant.PropertyChanged += ParticipantChanged;
                Participants.Add(participant);

                PayerOptions.Add(new PayerOption { Member = member, Initials = initials });
            }

            CategoryChips = new ObservableCollection<CategoryChip>(
                AppConstants.Categories.Keys.Select(key => new CategoryChip
                {
                    Key = key,
                    Name = LocalizationResourceManager.Instance[$"Category_{key}"]
                }));

            var existingExpense = loadExpense.Result;
            if (existingExpense is not null)
                LoadExistingExpense(existingExpense, loadShares.Result);
            else
            {
                var myPayerOption = PayerOptions.FirstOrDefault(p => p.Member.AccountId == authService.CurrentAccountId)
                    ?? PayerOptions.FirstOrDefault();
                if (myPayerOption is not null)
                    SelectPayer(myPayerOption);

                RedistributeEqually();
            }
        }
        finally
        {
            IsBusy = false;
        }
    });

    /// <summary>Overlays a previously-saved expense's data onto the freshly-built participant/payer
    /// lists. IsManualSplit is set first so the AmountText assignment below doesn't trigger an
    /// equal-split redistribution that would clobber the real per-member shares being loaded.</summary>
    private void LoadExistingExpense(Expense expense, List<ExpenseShare> shares)
    {
        IsManualSplit = true;

        redistributing = true;
        try
        {
            var sharesByMember = shares.ToDictionary(s => s.MemberId, s => s.ShareAmount);
            foreach (var participant in Participants)
            {
                if (sharesByMember.TryGetValue(participant.Member.Id, out var shareAmount))
                {
                    participant.IsIncluded = true;
                    participant.Owes = shareAmount;
                }
                else
                {
                    participant.IsIncluded = false;
                    participant.Owes = 0;
                }
            }
        }
        finally
        {
            redistributing = false;
        }

        Description = expense.Description;
        OccurredOn = expense.OccurredAt;
        AmountText = expense.Amount.ToString("0.00", CultureInfo.InvariantCulture);

        SelectedCategory = expense.Category;
        var matchingChip = CategoryChips.FirstOrDefault(c => c.Key == expense.Category);
        if (matchingChip is not null)
            matchingChip.IsSelected = true;

        var payerOption = PayerOptions.FirstOrDefault(p => p.Member.Id == expense.PaidByMemberId);
        if (payerOption is not null)
            SelectPayer(payerOption);

        RecalcRemaining();
    }

    partial void OnAmountTextChanged(string value) =>
        Amount = decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    partial void OnAmountChanged(decimal value)
    {
        if (!IsManualSplit) RedistributeEqually();
        else RecalcRemaining();
    }

    partial void OnOccurredOnChanged(DateTime value) =>
        OccurredOnDisplay = value.Date == DateTime.Today
            ? LocalizationResourceManager.Instance["Common_Today"]
            : value.ToString("MMM d, yyyy");

    [RelayCommand]
    private void SelectPayer(PayerOption? option)
    {
        if (option is null) return;
        SelectedPayer = option.Member;
        foreach (var p in PayerOptions)
            p.IsSelected = p == option;
        RecalcRemaining();
    }

    private void ParticipantChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExpenseParticipant.IsIncluded))
        {
            if (sender is ExpenseParticipant { IsIncluded: false } excluded)
            {
                redistributing = true;
                try { excluded.Owes = 0; }
                finally { redistributing = false; }
            }

            if (!IsManualSplit) RedistributeEqually();
            else RecalcRemaining();
            return;
        }

        if (e.PropertyName != nameof(ExpenseParticipant.Owes)) return;
        if (!redistributing)
            IsManualSplit = true;
        RecalcRemaining();
    }

    [RelayCommand]
    private void SplitEqually()
    {
        IsManualSplit = false;
        RedistributeEqually();
    }

    [RelayCommand]
    private void SplitManually() => IsManualSplit = true;

    [RelayCommand]
    private void SelectCategory(CategoryChip? chip)
    {
        if (chip is null) return;
        SelectedCategory = chip.IsSelected ? string.Empty : chip.Key;
        foreach (var c in CategoryChips)
            c.IsSelected = c == chip && !string.IsNullOrEmpty(SelectedCategory);
    }

    /// <summary>Equal split with the classic penny-rounding fix (ported from DebtTracker's
    /// AddPaymentViewModel): assign every included participant an equal rounded share, then dump
    /// whatever's left over onto the last one, so shares always sum to exactly Amount instead of
    /// landing a cent short or over.</summary>
    private void RedistributeEqually()
    {
        redistributing = true;
        try
        {
            foreach (var excluded in Participants.Where(p => !p.IsIncluded))
                excluded.Owes = 0;

            var included = Participants.Where(p => p.IsIncluded).ToList();
            if (included.Count == 0)
            {
                RecalcRemaining();
                return;
            }

            var share = decimal.Round(Amount / included.Count, 2);
            for (var i = 0; i < included.Count - 1; i++)
                included[i].Owes = share;
            included[^1].Owes = decimal.Round(Amount - share * (included.Count - 1), 2);

            RecalcRemaining();
        }
        finally
        {
            redistributing = false;
        }
    }

    private void RecalcRemaining()
    {
        var included = Participants.Where(p => p.IsIncluded).ToList();
        var sum = included.Sum(p => p.Owes);
        Remaining = decimal.Round(Amount - sum, 2);
        RemainingText = LocalizationResourceManager.Instance.Format("AddExpense_Remaining", Remaining);
        CanSave = SelectedPayer is not null
                  && Amount > 0
                  && included.Count > 0
                  && Remaining == 0
                  && included.All(p => p.Owes > 0);
    }

    [RelayCommand]
    private Task Save() => RunSafeAsync(async () =>
    {
        if (!CanSave || SelectedPayer is null) return;

        IsBusy = true;
        try
        {
            var expense = new Expense
            {
                GroupId = groupId,
                PaidByMemberId = SelectedPayer.Id,
                Amount = Amount,
                Description = Description,
                Category = SelectedCategory,
                OccurredAt = OccurredOn.ToUniversalTime()
            };

            var shares = Participants
                .Where(p => p.IsIncluded)
                .Select(p => new ExpenseShare { MemberId = p.Member.Id, ShareAmount = p.Owes })
                .ToList();

            if (IsEditMode && editingExpenseId is { } id)
            {
                expense.Id = id;
                await expensesRepository.UpdateAsync(expense, shares);
            }
            else
            {
                await expensesRepository.AddAsync(expense, shares);
            }

            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task Delete() => RunSafeAsync(async () =>
    {
        if (!IsEditMode || editingExpenseId is not { } id) return;

        IsBusy = true;
        try
        {
            await expensesRepository.DeleteAsync(id);
            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
        }
    });

    [RelayCommand]
    private Task Cancel() => RunSafeAsync(() => Shell.Current.GoToAsync(".."));

    private static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }
}
