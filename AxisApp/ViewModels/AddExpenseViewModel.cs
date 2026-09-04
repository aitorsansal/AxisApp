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
    public string Name { get; init; } = "";
    public string Initials { get; init; } = "";
    public string? AvatarUrl { get; init; }

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
    public string Name { get; init; } = "";
    public string Initials { get; init; } = "";
    public string? AvatarUrl { get; init; }

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

/// <summary>One selectable chip in the recurring-expense frequency row.</summary>
public partial class RecurringFrequencyOption : ObservableObject
{
    public RecurringFrequency Value { get; init; }
    public string Label { get; init; } = "";

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
    private readonly IRecurringExpensesRepository recurringExpensesRepository;
    private readonly IAliasesRepository aliasesRepository;
    private readonly IReceiptsRepository receiptsRepository;
    private readonly IAuthService authService;

    private Guid groupId;
    private Guid? editingExpenseId;
    private Guid? editingCreatedBy;
    private DateTime editingCreatedAt;
    private bool editingIsSettlement;
    private Guid? editingRecurringExpenseId;
    private Guid? editingRecurringCreatedBy;
    private DateTime editingRecurringCreatedAt;
    private DateTime? editingLastProcessedDate;
    private bool editingRecurringIsActive = true;
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

    /// <summary>Set once, from the loaded Expense, when editing a settle-up — never toggled by the
    /// user (there's no path to convert a settlement into a regular expense or vice versa, same
    /// "fixed at creation" treatment CanToggleRecurring already gives Recurring vs. one-off).
    /// Category and receipt don't apply to a settlement, so ShowMoneyExtras hides both.</summary>
    [ObservableProperty] private bool isSettlement;
    [ObservableProperty] private bool showMoneyExtras = true;
    [ObservableProperty] private string? receiptPath;
    [ObservableProperty] private string? receiptPreviewUrl;
    [ObservableProperty] private bool isReceiptBusy;
    [ObservableProperty] private bool isReceiptPreviewOpen;
    [ObservableProperty] private bool isRecurringMode;
    [ObservableProperty] private bool canToggleRecurring;
    [ObservableProperty] private ObservableCollection<RecurringFrequencyOption> frequencyOptions = [];
    [ObservableProperty] private RecurringFrequency selectedFrequency = RecurringFrequency.Monthly;
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private string startDateDisplay = LocalizationResourceManager.Instance["Common_Today"];

    public AddExpenseViewModel(
        IMembersRepository membersRepository,
        IExpensesRepository expensesRepository,
        IRecurringExpensesRepository recurringExpensesRepository,
        IAliasesRepository aliasesRepository,
        IReceiptsRepository receiptsRepository,
        IAuthService authService)
    {
        this.membersRepository = membersRepository;
        this.expensesRepository = expensesRepository;
        this.recurringExpensesRepository = recurringExpensesRepository;
        this.aliasesRepository = aliasesRepository;
        this.receiptsRepository = receiptsRepository;
        this.authService = authService;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Guid? expenseId = query.TryGetValue("expenseId", out var expenseValue)
            && Guid.TryParse(expenseValue?.ToString(), out var parsedExpenseId)
                ? parsedExpenseId
                : null;

        Guid? recurringExpenseId = query.TryGetValue("recurringExpenseId", out var recurringValue)
            && Guid.TryParse(recurringValue?.ToString(), out var parsedRecurringId)
                ? parsedRecurringId
                : null;

        bool startAsRecurring = query.TryGetValue("recurring", out var recurringFlag)
            && recurringFlag?.ToString() == "true";

        if (query.TryGetValue("groupId", out var groupValue) && Guid.TryParse(groupValue?.ToString(), out var groupIdValue))
            _ = LoadAsync(groupIdValue, expenseId, recurringExpenseId, startAsRecurring);
    }

    /// <summary>Loads the group's members as the participant set and its category list. In plain
    /// add mode (no expenseId/recurringExpenseId) everyone defaults to an equal split; editing an
    /// existing one-off Expense or an existing RecurringExpense template overlays its saved data
    /// on top, overriding the equal-split defaults built for the member list. startAsRecurring
    /// starts a fresh add already in recurring mode (the "Repeat" toggle can also flip this on
    /// manually — see CanToggleRecurring).</summary>
    public Task LoadAsync(Guid forGroupId, Guid? forExpenseId = null, Guid? forRecurringExpenseId = null, bool startAsRecurring = false) => RunSafeAsync(async () =>
    {
        groupId = forGroupId;
        editingExpenseId = forExpenseId;
        editingRecurringExpenseId = forRecurringExpenseId;
        IsEditMode = forExpenseId is not null || forRecurringExpenseId is not null;
        IsRecurringMode = forRecurringExpenseId is not null || startAsRecurring;
        CanToggleRecurring = forExpenseId is null && forRecurringExpenseId is null;
        PageTitle = LocalizationResourceManager.Instance[
            forRecurringExpenseId is not null ? "AddExpense_EditRecurringTitle"
            : forExpenseId is not null ? "AddExpense_EditTitle"
            : startAsRecurring ? "AddExpense_RecurringTitle"
            : "AddExpense_Title"];
        IsSettlement = false;

        IsBusy = true;
        try
        {
            var loadMembers = membersRepository.GetForGroupAsync(groupId);
            var loadExpense = forExpenseId is { } id ? expensesRepository.GetByIdAsync(id) : Task.FromResult<Expense?>(null);
            var loadShares = forExpenseId is { } sharesId ? expensesRepository.GetSharesAsync(sharesId) : Task.FromResult(new List<ExpenseShare>());
            var loadRecurring = forRecurringExpenseId is { } rid ? recurringExpensesRepository.GetByIdAsync(rid) : Task.FromResult<RecurringExpense?>(null);
            var loadRecurringShares = forRecurringExpenseId is { } rsid ? recurringExpensesRepository.GetSharesAsync(rsid) : Task.FromResult(new List<RecurringExpenseShare>());
            var loadAliases = aliasesRepository.GetMyAliasesAsync();
            await Task.WhenAll(loadMembers, loadExpense, loadShares, loadRecurring, loadRecurringShares, loadAliases);

            var aliases = loadAliases.Result;

            foreach (var participant in Participants)
                participant.PropertyChanged -= ParticipantChanged;

            Participants = new ObservableCollection<ExpenseParticipant>();
            PayerOptions = new ObservableCollection<PayerOption>();

            foreach (var member in loadMembers.Result)
            {
                var name = MemberDisplay.Name(member, aliases);
                var initials = MemberDisplay.Initials(member, aliases);
                var avatarUrl = MemberDisplay.AvatarUrl(member);

                var participant = new ExpenseParticipant { Member = member, Name = name, Initials = initials, AvatarUrl = avatarUrl };
                participant.PropertyChanged += ParticipantChanged;
                Participants.Add(participant);

                PayerOptions.Add(new PayerOption { Member = member, Name = name, Initials = initials, AvatarUrl = avatarUrl });
            }

            CategoryChips = new ObservableCollection<CategoryChip>(
                AppConstants.Categories.Keys.Select(key => new CategoryChip
                {
                    Key = key,
                    Name = LocalizationResourceManager.Instance[$"Category_{key}"]
                }));

            FrequencyOptions = new ObservableCollection<RecurringFrequencyOption>(
                Enum.GetValues<RecurringFrequency>().Select(f => new RecurringFrequencyOption
                {
                    Value = f,
                    Label = LocalizationResourceManager.Instance[$"Recurring_Frequency_{f}"],
                    IsSelected = f == RecurringFrequency.Monthly
                }));
            SelectedFrequency = RecurringFrequency.Monthly;

            var existingExpense = loadExpense.Result;
            var existingRecurring = loadRecurring.Result;
            if (existingExpense is not null)
            {
                LoadExistingExpense(existingExpense, loadShares.Result);
                if (ReceiptPath is not null)
                    ReceiptPreviewUrl = await receiptsRepository.GetSignedUrlAsync(ReceiptPath);
            }
            else if (existingRecurring is not null)
            {
                LoadExistingRecurringExpense(existingRecurring, loadRecurringShares.Result);
            }
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
        editingCreatedBy = expense.CreatedBy;
        editingCreatedAt = expense.CreatedAt;
        editingIsSettlement = expense.IsSettlement;
        IsSettlement = expense.IsSettlement;
        if (expense.IsSettlement)
            PageTitle = LocalizationResourceManager.Instance["AddExpense_EditSettlementTitle"];

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
        ReceiptPath = expense.ReceiptPath;

        SelectedCategory = expense.Category;
        var matchingChip = CategoryChips.FirstOrDefault(c => c.Key == expense.Category);
        if (matchingChip is not null)
            matchingChip.IsSelected = true;

        var payerOption = PayerOptions.FirstOrDefault(p => p.Member.Id == expense.PaidByMemberId);
        if (payerOption is not null)
            SelectPayer(payerOption);

        RecalcRemaining();
    }

    /// <summary>Same shape as LoadExistingExpense, overlaying a saved RecurringExpense template
    /// instead. Stashes CreatedBy/CreatedAt/LastProcessedDate/IsActive so Save() can carry them
    /// through unchanged on update — editing a template's amount/split/category must never reset
    /// its materialization schedule or silently reactivate a paused one.</summary>
    private void LoadExistingRecurringExpense(RecurringExpense template, List<RecurringExpenseShare> shares)
    {
        IsManualSplit = true;
        editingRecurringCreatedBy = template.CreatedBy;
        editingRecurringCreatedAt = template.CreatedAt;
        editingLastProcessedDate = template.LastProcessedDate;
        editingRecurringIsActive = template.IsActive;

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

        Description = template.Description;
        StartDate = template.StartDate;
        AmountText = template.Amount.ToString("0.00", CultureInfo.InvariantCulture);

        SelectedCategory = template.Category;
        var matchingChip = CategoryChips.FirstOrDefault(c => c.Key == template.Category);
        if (matchingChip is not null)
            matchingChip.IsSelected = true;

        if (Enum.TryParse<RecurringFrequency>(template.Frequency, ignoreCase: true, out var frequency))
        {
            SelectedFrequency = frequency;
            foreach (var option in FrequencyOptions)
                option.IsSelected = option.Value == frequency;
        }

        var payerOption = PayerOptions.FirstOrDefault(p => p.Member.Id == template.PaidByMemberId);
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

    partial void OnStartDateChanged(DateTime value) =>
        StartDateDisplay = value.Date == DateTime.Today
            ? LocalizationResourceManager.Instance["Common_Today"]
            : value.ToString("MMM d, yyyy");

    partial void OnIsRecurringModeChanged(bool value) => ShowMoneyExtras = !value && !IsSettlement;
    partial void OnIsSettlementChanged(bool value) => ShowMoneyExtras = !IsRecurringMode && !value;

    /// <summary>Only meaningful in pure-add mode — see CanToggleRecurring. Editing an existing
    /// one-off Expense or an existing RecurringExpense template can't convert one into the other
    /// after the fact; the toggle is hidden in both those cases (AddExpensePage.xaml).</summary>
    [RelayCommand]
    private void ToggleRecurring() => IsRecurringMode = !IsRecurringMode;

    [RelayCommand]
    private void SelectFrequency(RecurringFrequencyOption? option)
    {
        if (option is null) return;
        SelectedFrequency = option.Value;
        foreach (var o in FrequencyOptions)
            o.IsSelected = o == option;
    }

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
            if (IsRecurringMode)
            {
                var template = new RecurringExpense
                {
                    GroupId = groupId,
                    PaidByMemberId = SelectedPayer.Id,
                    Amount = Amount,
                    Description = Description,
                    Category = SelectedCategory,
                    Frequency = SelectedFrequency.ToString().ToLowerInvariant(),
                    StartDate = StartDate.Date,
                    LastProcessedDate = editingLastProcessedDate,
                    IsActive = editingRecurringExpenseId is null || editingRecurringIsActive
                };

                var recurringShares = Participants
                    .Where(p => p.IsIncluded)
                    .Select(p => new RecurringExpenseShare { MemberId = p.Member.Id, ShareAmount = p.Owes })
                    .ToList();

                if (editingRecurringExpenseId is { } recurringId)
                {
                    template.Id = recurringId;
                    template.CreatedBy = editingRecurringCreatedBy;
                    template.CreatedAt = editingRecurringCreatedAt;
                    await recurringExpensesRepository.UpdateAsync(template, recurringShares);
                }
                else
                {
                    await recurringExpensesRepository.AddAsync(template, recurringShares);
                }
            }
            else
            {
                var expense = new Expense
                {
                    GroupId = groupId,
                    PaidByMemberId = SelectedPayer.Id,
                    Amount = Amount,
                    Description = Description,
                    Category = SelectedCategory,
                    OccurredAt = OccurredOn.ToUniversalTime(),
                    ReceiptPath = ReceiptPath
                };

                var shares = Participants
                    .Where(p => p.IsIncluded)
                    .Select(p => new ExpenseShare { MemberId = p.Member.Id, ShareAmount = p.Owes })
                    .ToList();

                if (IsEditMode && editingExpenseId is { } id)
                {
                    expense.Id = id;
                    expense.CreatedBy = editingCreatedBy;
                    expense.CreatedAt = editingCreatedAt;
                    expense.IsSettlement = editingIsSettlement;
                    await expensesRepository.UpdateAsync(expense, shares);
                }
                else
                {
                    await expensesRepository.AddAsync(expense, shares);
                }
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
        if (editingRecurringExpenseId is null && (!IsEditMode || editingExpenseId is null)) return;

        IsBusy = true;
        try
        {
            if (editingRecurringExpenseId is { } recurringId)
                await recurringExpensesRepository.DeleteAsync(recurringId);
            else if (editingExpenseId is { } id)
                await expensesRepository.DeleteAsync(id);

            await Shell.Current.GoToAsync("..");
        }
        finally
        {
            IsBusy = false;
        }
    });

    /// <summary>Tapping the drop zone opens the bigger preview overlay if a receipt already exists
    /// (so you can actually see it before deciding to change/remove it), or goes straight to the
    /// source picker if there's nothing to preview yet.</summary>
    [RelayCommand]
    private Task PickReceipt() => RunSafeAsync(async () =>
    {
        if (ReceiptPath is not null)
        {
            IsReceiptPreviewOpen = true;
            return;
        }

        await ChooseReceiptSourceAsync();
    });

    [RelayCommand]
    private void CloseReceiptPreview() => IsReceiptPreviewOpen = false;

    [RelayCommand]
    private Task ChangeReceipt() => RunSafeAsync(async () =>
    {
        IsReceiptPreviewOpen = false;
        await ChooseReceiptSourceAsync();
    });

    [RelayCommand]
    private Task RemoveReceipt() => RunSafeAsync(async () =>
    {
        IsReceiptPreviewOpen = false;
        await RemoveReceiptAsync();
    });

    /// <summary>Uploaded eagerly to the `receipts` bucket (Services/IReceiptsRepository.cs) as soon
    /// as a photo is picked, rather than deferred until Save — ReceiptPath just rides along as a
    /// plain field on the Expense being inserted/updated, same as Description/Category. Works even
    /// in Add mode, before the expense itself exists, because the bucket's RLS scopes by group
    /// membership rather than by expense id (see schema.sql's "Receipts" remarks) — an upload that
    /// never gets attached (Cancel is tapped afterward) becomes an orphan the cleanup Edge Function
    /// purges later (SCOPE.md), not a correctness problem here.</summary>
    private async Task ChooseReceiptSourceAsync()
    {
        var loc = LocalizationResourceManager.Instance;
        var choice = await Shell.Current.DisplayActionSheet(
            loc["AddExpense_ReceiptPhoto"], loc["Common_Cancel"], null,
            loc["AddExpense_TakePhoto"], loc["AddExpense_ChooseFromGallery"]);

        if (choice == loc["AddExpense_TakePhoto"])
            await CaptureReceiptAsync(useCamera: true);
        else if (choice == loc["AddExpense_ChooseFromGallery"])
            await CaptureReceiptAsync(useCamera: false);
    }

    private async Task CaptureReceiptAsync(bool useCamera)
    {
        var photo = useCamera
            ? await MediaPicker.Default.CapturePhotoAsync()
            : await MediaPicker.Default.PickPhotoAsync();
        if (photo is null) return;

        await using var stream = await photo.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);

        var webp = ImageResizer.ToReceiptWebp(memory.ToArray());

        IsReceiptBusy = true;
        try
        {
            var newPath = await receiptsRepository.UploadAsync(groupId, webp, ReceiptPath);
            ReceiptPath = newPath;
            ReceiptPreviewUrl = await receiptsRepository.GetSignedUrlAsync(newPath);
        }
        finally
        {
            IsReceiptBusy = false;
        }
    }

    private async Task RemoveReceiptAsync()
    {
        if (ReceiptPath is null) return;

        await receiptsRepository.RemoveAsync(ReceiptPath);
        ReceiptPath = null;
        ReceiptPreviewUrl = null;
    }

    [RelayCommand]
    private Task Cancel() => RunSafeAsync(() => Shell.Current.GoToAsync(".."));
}
