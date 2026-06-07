using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class BillsViewModel : ObservableObject
{
    private readonly IssueBillHandler _issueHandler;
    private readonly CancelBillHandler _cancelHandler;
    private readonly RecordBillPaymentHandler _payHandler;
    private readonly ReverseBillPaymentHandler _reversePayHandler;
    private readonly UnapplyBillPaymentHandler _unapplyHandler;
    private readonly AddBillAdjustmentHandler _adjustHandler;
    private readonly DisputeBillHandler _disputeHandler;
    private readonly WriteOffBillHandler _writeOffHandler;
    private readonly GetBillingDashboardHandler _dashboardHandler;
    private readonly GetAgingReportHandler _agingHandler;
    private readonly GetPartiesListHandler _partiesHandler;
    private readonly GetAccountsListHandler _accountsHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly TaggingMixin? _tagging;

    public BillsViewModel(
        IssueBillHandler issueHandler,
        CancelBillHandler cancelHandler,
        RecordBillPaymentHandler payHandler,
        ReverseBillPaymentHandler reversePayHandler,
        UnapplyBillPaymentHandler unapplyHandler,
        AddBillAdjustmentHandler adjustHandler,
        DisputeBillHandler disputeHandler,
        WriteOffBillHandler writeOffHandler,
        GetBillingDashboardHandler dashboardHandler,
        GetAgingReportHandler agingHandler,
        GetPartiesListHandler partiesHandler,
        GetAccountsListHandler accountsHandler,
        Guid actorUserId, Guid deviceId,
        IToastService? toastService = null,
        IExportService? exportService = null,
        TaggingMixin? tagging = null)
    {
        _issueHandler = issueHandler;
        _cancelHandler = cancelHandler;
        _payHandler = payHandler;
        _reversePayHandler = reversePayHandler;
        _unapplyHandler = unapplyHandler;
        _adjustHandler = adjustHandler;
        _disputeHandler = disputeHandler;
        _writeOffHandler = writeOffHandler;
        _dashboardHandler = dashboardHandler;
        _agingHandler = agingHandler;
        _partiesHandler = partiesHandler;
        _accountsHandler = accountsHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;
        _tagging = tagging;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        IssueBillCommand = new AsyncRelayCommand(IssueAsync);
        CancelBillCommand = new AsyncRelayCommand(CancelAsync);
        RecordPaymentCommand = new AsyncRelayCommand(RecordPaymentAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportAsync);
    }

    public ICommand RefreshCommand { get; }
    public ICommand IssueBillCommand { get; }
    public ICommand CancelBillCommand { get; }
    public ICommand RecordPaymentCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<BillListItemDto> Bills { get; } = new();
    public ObservableCollection<PartyListItemDto> Parties { get; } = new();
    public ObservableCollection<AccountListItemDto> Accounts { get; } = new();
    public ObservableCollection<AgingReportRowDto> AgingReport { get; } = new();

    // Tag filter
    public ObservableCollection<string> TagSuggestions { get; } = new();
    private string _selectedTagFilter = string.Empty;
    public string SelectedTagFilter
    {
        get => _selectedTagFilter;
        set { if (SetProperty(ref _selectedTagFilter, value)) _ = ApplyTagFilterAsync(); }
    }
    private HashSet<Guid>? _tagFilteredIds;

    // Selected entity tags
    public ObservableCollection<string> SelectedEntityTags { get; } = new();
    private string _newTagText = string.Empty;
    public string NewTagText { get => _newTagText; set => SetProperty(ref _newTagText, value); }
    public ICommand AddTagCommand => new RelayCommand(() => { _tagging?.AddTag(NewTagText, SelectedEntityTags); NewTagText = string.Empty; });
    public ICommand RemoveTagCommand => new RelayCommand<string>(tag => _tagging?.RemoveTag(tag, SelectedEntityTags));
    public ICommand SaveTagsCommand => new AsyncRelayCommand(async () => { if (_tagging != null && SelectedBill != null) await _tagging.SaveTagsAsync(SelectedBill.BillId, "Bill", SelectedEntityTags, TagSuggestions); });

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private decimal _totalOutstanding;
    public decimal TotalOutstanding { get => _totalOutstanding; set => SetProperty(ref _totalOutstanding, value); }

    private int _overdueCount;
    public int OverdueCount { get => _overdueCount; set => SetProperty(ref _overdueCount, value); }

    private BillListItemDto? _selectedBill;
    public BillListItemDto? SelectedBill { get => _selectedBill; set => SetProperty(ref _selectedBill, value); }

    // Issue form
    private PartyListItemDto? _selectedParty;
    public PartyListItemDto? SelectedParty { get => _selectedParty; set => SetProperty(ref _selectedParty, value); }

    private decimal _newAmount;
    public decimal NewAmount { get => _newAmount; set => SetProperty(ref _newAmount, value); }

    private string _newCurrency = "EGP";
    public string NewCurrency { get => _newCurrency; set => SetProperty(ref _newCurrency, value); }

    private string _newCategory = string.Empty;
    public string NewCategory { get => _newCategory; set => SetProperty(ref _newCategory, value); }

    private string _newReference = string.Empty;
    public string NewReference { get => _newReference; set => SetProperty(ref _newReference, value); }

    private DateTime _newDueDate = DateTime.Today.AddDays(30);
    public DateTime NewDueDate { get => _newDueDate; set => SetProperty(ref _newDueDate, value); }

    // Payment form
    private AccountListItemDto? _paymentAccount;
    public AccountListItemDto? PaymentAccount { get => _paymentAccount; set => SetProperty(ref _paymentAccount, value); }

    private decimal _paymentAmount;
    public decimal PaymentAmount { get => _paymentAmount; set => SetProperty(ref _paymentAmount, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var asOf = DateOnly.FromDateTime(DateTime.Today);
            var dash = await _dashboardHandler.HandleAsync(asOf, false, CancellationToken.None);

            Bills.Clear();
            foreach (var b in dash.Bills)
            {
                if (_tagFilteredIds != null && !_tagFilteredIds.Contains(b.BillId)) continue;
                Bills.Add(b);
            }
            TotalOutstanding = dash.TotalBillsOutstanding;
            OverdueCount = dash.OverdueBillsCount;
            IsEmpty = Bills.Count == 0;

            var parties = await _partiesHandler.HandleAsync(false, CancellationToken.None);
            Parties.Clear();
            foreach (var p in parties) Parties.Add(p);

            var accounts = await _accountsHandler.HandleAsync(CancellationToken.None);
            Accounts.Clear();
            foreach (var a in accounts.Where(x => !x.IsArchived)) Accounts.Add(a);

            var aging = await _agingHandler.HandleAsync(asOf, CancellationToken.None);
            AgingReport.Clear();
            foreach (var r in aging) AgingReport.Add(r);

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load bills", ex); }
        finally { IsLoading = false; }
    }

    private async Task IssueAsync()
    {
        if (SelectedParty == null) { _toastService?.Error("Select a vendor"); return; }
        if (NewAmount <= 0) { _toastService?.Error("Amount must be positive"); return; }

        try
        {
            await _issueHandler.HandleAsync(
                new IssueBillCommand(null, null, SelectedParty.PartyId, NewCurrency,
                    NewAmount, DateOnly.FromDateTime(NewDueDate),
                    string.IsNullOrWhiteSpace(NewCategory) ? "General" : NewCategory.Trim(),
                    NewReference, null, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Bill issued");
            NewAmount = 0;
            NewReference = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to issue bill", ex); }
    }

    private async Task CancelAsync()
    {
        if (SelectedBill == null) { _toastService?.Error("Select a bill first"); return; }
        try
        {
            await _cancelHandler.HandleAsync(
                new CancelBillCommand(SelectedBill.BillId, "Cancelled by user",
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Bill cancelled");
            SelectedBill = null;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to cancel bill", ex); }
    }

    private async Task RecordPaymentAsync()
    {
        if (SelectedBill == null) { _toastService?.Error("Select a bill"); return; }
        if (PaymentAccount == null) { _toastService?.Error("Select an account"); return; }
        if (PaymentAmount <= 0) { _toastService?.Error("Amount must be positive"); return; }

        try
        {
            await _payHandler.HandleAsync(
                new RecordBillPaymentCommand(SelectedBill.BillId, null, PaymentAccount.AccountId,
                    PaymentAmount, NewCurrency, DateOnly.FromDateTime(DateTime.Today),
                    "Manual", null, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Payment recorded");
            PaymentAmount = 0;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to record payment", ex); }
    }

    private async Task ExportAsync()
    {
        if (_exportService == null || Bills.Count == 0) return;
        try
        {
            var headers = new[] { "Reference", "Vendor", "Amount", "Currency", "DueDate", "Status", "Outstanding", "Paid" };
            var rows = Bills.Select(b => (IReadOnlyList<string?>)new[]
            {
                b.Reference, b.PartyName, b.Amount.ToString("F2"), b.CurrencyCode,
                b.DueDate.ToString("yyyy-MM-dd"), b.Status,
                b.Outstanding.ToString("F2"), b.TotalPaid.ToString("F2")
            }).ToList();
            await _exportService.ExportCsvAsync("Bills", headers, rows);
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "Bill");
        else
            _tagFilteredIds = null;
        await LoadAsync();
    }

    public async Task LoadTagsForSelectedBillAsync()
    {
        if (_tagging != null && SelectedBill != null)
            await _tagging.LoadEntityTagsAsync(SelectedBill.BillId, "Bill", SelectedEntityTags);
        else
            SelectedEntityTags.Clear();
    }
}
