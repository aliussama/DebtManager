using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class InvoicesViewModel : ObservableObject
{
    private readonly IssueInvoiceHandler _issueHandler;
    private readonly CancelInvoiceHandler _cancelHandler;
    private readonly RecordInvoicePaymentHandler _payHandler;
    private readonly ReverseInvoicePaymentHandler _reversePayHandler;
    private readonly UnapplyInvoicePaymentHandler _unapplyHandler;
    private readonly AddInvoiceAdjustmentHandler _adjustHandler;
    private readonly DisputeInvoiceHandler _disputeHandler;
    private readonly WriteOffInvoiceHandler _writeOffHandler;
    private readonly GetBillingDashboardHandler _dashboardHandler;
    private readonly GetAgingReportHandler _agingHandler;
    private readonly GetPartiesListHandler _partiesHandler;
    private readonly GetAccountsListHandler _accountsHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly TaggingMixin? _tagging;

    public InvoicesViewModel(
        IssueInvoiceHandler issueHandler,
        CancelInvoiceHandler cancelHandler,
        RecordInvoicePaymentHandler payHandler,
        ReverseInvoicePaymentHandler reversePayHandler,
        UnapplyInvoicePaymentHandler unapplyHandler,
        AddInvoiceAdjustmentHandler adjustHandler,
        DisputeInvoiceHandler disputeHandler,
        WriteOffInvoiceHandler writeOffHandler,
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
        IssueInvoiceCommand = new AsyncRelayCommand(IssueAsync);
        CancelInvoiceCommand = new AsyncRelayCommand(CancelAsync);
        RecordPaymentCommand = new AsyncRelayCommand(RecordPaymentAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportAsync);
    }

    public ICommand RefreshCommand { get; }
    public ICommand IssueInvoiceCommand { get; }
    public ICommand CancelInvoiceCommand { get; }
    public ICommand RecordPaymentCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<InvoiceListItemDto> Invoices { get; } = new();
    public ObservableCollection<PartyListItemDto> Parties { get; } = new();
    public ObservableCollection<AccountListItemDto> Accounts { get; } = new();

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
    public ICommand SaveTagsCommand => new AsyncRelayCommand(async () => { if (_tagging != null && SelectedInvoice != null) await _tagging.SaveTagsAsync(SelectedInvoice.InvoiceId, "Invoice", SelectedEntityTags, TagSuggestions); });

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private decimal _totalOutstanding;
    public decimal TotalOutstanding { get => _totalOutstanding; set => SetProperty(ref _totalOutstanding, value); }

    private int _overdueCount;
    public int OverdueCount { get => _overdueCount; set => SetProperty(ref _overdueCount, value); }

    private InvoiceListItemDto? _selectedInvoice;
    public InvoiceListItemDto? SelectedInvoice { get => _selectedInvoice; set => SetProperty(ref _selectedInvoice, value); }

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

            Invoices.Clear();
            foreach (var i in dash.Invoices)
            {
                if (_tagFilteredIds != null && !_tagFilteredIds.Contains(i.InvoiceId)) continue;
                Invoices.Add(i);
            }
            TotalOutstanding = dash.TotalInvoicesOutstanding;
            OverdueCount = dash.OverdueInvoicesCount;
            IsEmpty = Invoices.Count == 0;

            var parties = await _partiesHandler.HandleAsync(false, CancellationToken.None);
            Parties.Clear();
            foreach (var p in parties) Parties.Add(p);

            var accounts = await _accountsHandler.HandleAsync(CancellationToken.None);
            Accounts.Clear();
            foreach (var a in accounts.Where(x => !x.IsArchived)) Accounts.Add(a);

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load invoices", ex); }
        finally { IsLoading = false; }
    }

    private async Task IssueAsync()
    {
        if (SelectedParty == null) { _toastService?.Error("Select a customer"); return; }
        if (NewAmount <= 0) { _toastService?.Error("Amount must be positive"); return; }

        try
        {
            await _issueHandler.HandleAsync(
                new IssueInvoiceCommand(null, null, SelectedParty.PartyId, NewCurrency,
                    NewAmount, DateOnly.FromDateTime(NewDueDate),
                    string.IsNullOrWhiteSpace(NewCategory) ? "General" : NewCategory.Trim(),
                    NewReference, null, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Invoice issued");
            NewAmount = 0;
            NewReference = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to issue invoice", ex); }
    }

    private async Task CancelAsync()
    {
        if (SelectedInvoice == null) { _toastService?.Error("Select an invoice first"); return; }
        try
        {
            await _cancelHandler.HandleAsync(
                new CancelInvoiceCommand(SelectedInvoice.InvoiceId, "Cancelled by user",
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Invoice cancelled");
            SelectedInvoice = null;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to cancel invoice", ex); }
    }

    private async Task RecordPaymentAsync()
    {
        if (SelectedInvoice == null) { _toastService?.Error("Select an invoice"); return; }
        if (PaymentAccount == null) { _toastService?.Error("Select an account"); return; }
        if (PaymentAmount <= 0) { _toastService?.Error("Amount must be positive"); return; }

        try
        {
            await _payHandler.HandleAsync(
                new RecordInvoicePaymentCommand(SelectedInvoice.InvoiceId, null, PaymentAccount.AccountId,
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
        if (_exportService == null || Invoices.Count == 0) return;
        try
        {
            var headers = new[] { "Reference", "Customer", "Amount", "Currency", "DueDate", "Status", "Outstanding", "Paid" };
            var rows = Invoices.Select(i => (IReadOnlyList<string?>)new[]
            {
                i.Reference, i.PartyName, i.Amount.ToString("F2"), i.CurrencyCode,
                i.DueDate.ToString("yyyy-MM-dd"), i.Status,
                i.Outstanding.ToString("F2"), i.TotalPaid.ToString("F2")
            }).ToList();
            await _exportService.ExportCsvAsync("Invoices", headers, rows);
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "Invoice");
        else
            _tagFilteredIds = null;
        await LoadAsync();
    }

    public async Task LoadTagsForSelectedInvoiceAsync()
    {
        if (_tagging != null && SelectedInvoice != null)
            await _tagging.LoadEntityTagsAsync(SelectedInvoice.InvoiceId, "Invoice", SelectedEntityTags);
        else
            SelectedEntityTags.Clear();
    }
}
