using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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

    [ObservableProperty] private bool isIncluded = true;
    [ObservableProperty] private decimal owes;
    [ObservableProperty] private string owesText = "0.00";

    private bool syncing;

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

/// <summary>
/// N-way expense entry: any group member can be the payer (paid_by_member_id on Expense isn't
/// restricted to "the current user" the way DebtTracker's payer selection effectively was — see
/// SCOPE.md's write-up of that app's split logic). Defaults to splitting the amount equally
/// across every group member; toggling a participant off or hand-editing one share switches into
/// manual mode via IsManualSplit, same escape hatch DebtTracker used.
/// </summary>
public partial class AddExpenseViewModel : ObservableObject
{
    private readonly IMembersRepository membersRepository;
    private readonly IExpensesRepository expensesRepository;
    private readonly ICategoriesRepository categoriesRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private bool redistributing;

    [ObservableProperty] private ObservableCollection<ExpenseParticipant> participants = [];
    [ObservableProperty] private Member? selectedPayer;
    [ObservableProperty] private decimal amount;
    [ObservableProperty] private string amountText = "0.00";
    [ObservableProperty] private decimal remaining;
    [ObservableProperty] private bool canSave;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string selectedCategory = string.Empty;
    [ObservableProperty] private DateTime occurredOn = DateTime.Today;
    [ObservableProperty] private bool isManualSplit;
    [ObservableProperty] private bool isBusy;

    public ObservableCollection<Member> PayerOptions { get; } = [];
    public ObservableCollection<string> CategoryOptions { get; } = [];

    public AddExpenseViewModel(
        IMembersRepository membersRepository,
        IExpensesRepository expensesRepository,
        ICategoriesRepository categoriesRepository,
        IAuthService authService)
    {
        this.membersRepository = membersRepository;
        this.expensesRepository = expensesRepository;
        this.categoriesRepository = categoriesRepository;
        this.authService = authService;
    }

    /// <summary>Call before the page shows. Loads the group's members as the default participant
    /// set (everyone included, equal split) and its category list.</summary>
    public async Task LoadAsync(Guid forGroupId)
    {
        groupId = forGroupId;
        IsBusy = true;
        try
        {
            var loadMembers = membersRepository.GetForGroupAsync(groupId);
            var loadCategories = categoriesRepository.GetAllAsync();
            await Task.WhenAll(loadMembers, loadCategories);

            foreach (var participant in Participants)
                participant.PropertyChanged -= ParticipantChanged;
            Participants.Clear();
            PayerOptions.Clear();

            foreach (var member in loadMembers.Result)
            {
                var participant = new ExpenseParticipant { Member = member };
                participant.PropertyChanged += ParticipantChanged;
                Participants.Add(participant);
                PayerOptions.Add(member);
            }

            SelectedPayer = loadMembers.Result.FirstOrDefault(m => m.AccountId == authService.CurrentAccountId)
                ?? loadMembers.Result.FirstOrDefault();

            CategoryOptions.Clear();
            CategoryOptions.Add(string.Empty);
            foreach (var category in loadCategories.Result)
                CategoryOptions.Add(category.Name);

            RedistributeEqually();
        }
        finally
        {
            IsBusy = false;
        }
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
        CanSave = SelectedPayer is not null
                  && Amount > 0
                  && included.Count > 0
                  && Remaining == 0
                  && included.All(p => p.Owes > 0);
    }

    [RelayCommand]
    private async Task Save()
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

            await expensesRepository.AddAsync(expense, shares);
            ResetForm();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetForm()
    {
        AmountText = "0.00";
        Description = string.Empty;
        SelectedCategory = string.Empty;
        OccurredOn = DateTime.Today;
        IsManualSplit = false;
        foreach (var p in Participants)
            p.IsIncluded = true;
        RedistributeEqually();
    }
}
