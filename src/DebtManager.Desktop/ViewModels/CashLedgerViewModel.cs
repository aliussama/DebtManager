using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class SplitExpenseLineVm : ObservableObject
{
    private string _category = string.Empty;
    public string Category { get => _category; set => SetProperty(ref _category, value); }

    private decimal _amount;
    public decimal Amount { get => _amount; set => SetProperty(ref _amount, value); }

    private string _notes = string.Empty;
    public string Notes { get => _notes; set => SetProperty(ref _notes, value); }
}

public sealed class SplitIncomeLineVm : ObservableObject
{
    private string _source = string.Empty;
    public string Source { get => _source; set => SetProperty(ref _source, value); }

    private decimal _amount;
    public decimal Amount { get => _amount; set => SetProperty(ref _amount, value); }
}

public sealed class CashLedgerViewModel : ObservableObject
{
    private readonly GetCashLedgerHandler? _ledgerHandler;
    private readonly GetAccountsListHandler? _accountsHandler;
    private readonly GetCategoriesListHandler? _categoriesHandler;
    private readonly RecordSplitExpenseHandler? _splitExpenseHandler;
    private readonly RecordSplitIncomeHandler? _splitIncomeHandler;
    private readonly GetIncomeSourcesHandler? _incomeSourcesHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    private readonly List<CashLedgerRowDto> _allRows = new();

    public CashLedgerViewModel(
        GetCashLedgerHandler? ledgerHandler = null,
        GetAccountsListHandler? accountsHandler = null,
        IToastService? toastService = null,
        IExportService? exportService = null,
        GetCategoriesListHandler? categoriesHandler = null,
        RecordSplitExpenseHandler? splitExpenseHandler = null,
        RecordSplitIncomeHandler? splitIncomeHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        GetIncomeSourcesHandler? incomeSourcesHandler = null,
        Action? onManageIncomeSources = null)
    {
        _ledgerHandler = ledgerHandler;
        _accountsHandler = accountsHandler;
        _categoriesHandler = categoriesHandler;
        _splitExpenseHandler = splitExpenseHandler;
        _splitIncomeHandler = splitIncomeHandler;
        _incomeSourcesHandler = incomeSourcesHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportToCsvAsync, () => Rows.Count > 0);
        ClearFiltersCommand = new RelayCommand(ClearFilters);

        StartSplitExpenseCommand = new RelayCommand(StartSplitExpense);
        CancelSplitExpenseCommand = new RelayCommand(CancelSplitExpense);
        AddSplitExpenseLineCommand = new RelayCommand(AddSplitExpenseLine);
        RemoveSplitExpenseLineCommand = new RelayCommand<SplitExpenseLineVm>(RemoveSplitExpenseLine);
        SubmitSplitExpenseCommand = new AsyncRelayCommand(SubmitSplitExpenseAsync);

        StartSplitIncomeCommand = new RelayCommand(StartSplitIncome);
        CancelSplitIncomeCommand = new RelayCommand(CancelSplitIncome);
        AddSplitIncomeLineCommand = new RelayCommand(AddSplitIncomeLine);
        RemoveSplitIncomeLineCommand = new RelayCommand<SplitIncomeLineVm>(RemoveSplitIncomeLine);
        SubmitSplitIncomeCommand = new AsyncRelayCommand(SubmitSplitIncomeAsync);

        ManageIncomeSourcesCommand = new RelayCommand(() => onManageIncomeSources?.Invoke());

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;

        SelectedAccountFilter = "All";
        IncludeTransfers = true;
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    // Split Expense commands
    public ICommand StartSplitExpenseCommand { get; }
    public ICommand CancelSplitExpenseCommand { get; }
    public ICommand AddSplitExpenseLineCommand { get; }
    public ICommand RemoveSplitExpenseLineCommand { get; }
    public ICommand SubmitSplitExpenseCommand { get; }

    // Split Income commands
    public ICommand StartSplitIncomeCommand { get; }
    public ICommand CancelSplitIncomeCommand { get; }
    public ICommand AddSplitIncomeLineCommand { get; }
    public ICommand RemoveSplitIncomeLineCommand { get; }
    public ICommand SubmitSplitIncomeCommand { get; }

    // Income sources
    public ICommand ManageIncomeSourcesCommand { get; }
    public ObservableCollection<IncomeSourceDto> AvailableIncomeSources { get; } = new();

    private Guid? _selectedIncomeSourceId;
    public Guid? SelectedIncomeSourceId { get => _selectedIncomeSourceId; set => SetProperty(ref _selectedIncomeSourceId, value); }

    public ObservableCollection<CashLedgerRowDto> Rows { get; } = new();
    public ObservableCollection<string> AccountFilterOptions { get; } = new() { "All" };
    public ICollectionView RowsView { get; }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isEmpty;
    public bool IsEmpty
    {
        get => _isEmpty;
        set => SetProperty(ref _isEmpty, value);
    }

    private string _selectedAccountFilter = "All";
    public string SelectedAccountFilter
    {
        get => _selectedAccountFilter;
        set
        {
            if (SetProperty(ref _selectedAccountFilter, value))
                RowsView.Refresh();
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RowsView.Refresh();
        }
    }

    private bool _includeTransfers = true;
    public bool IncludeTransfers
    {
        get => _includeTransfers;
        set
        {
            if (SetProperty(ref _includeTransfers, value))
                RowsView.Refresh();
        }
    }

    private DateTime? _filterFromDate;
    public DateTime? FilterFromDate
    {
        get => _filterFromDate;
        set
        {
            if (SetProperty(ref _filterFromDate, value))
                RowsView.Refresh();
        }
    }

    private DateTime? _filterToDate;
    public DateTime? FilterToDate
    {
        get => _filterToDate;
        set
        {
            if (SetProperty(ref _filterToDate, value))
                RowsView.Refresh();
        }
    }

    private decimal _totalIncome;
    public decimal TotalIncome
    {
        get => _totalIncome;
        set => SetProperty(ref _totalIncome, value);
    }

    private decimal _totalExpense;
    public decimal TotalExpense
    {
        get => _totalExpense;
        set => SetProperty(ref _totalExpense, value);
    }

    private decimal _netCashflow;
    public decimal NetCashflow
    {
        get => _netCashflow;
        set => SetProperty(ref _netCashflow, value);
    }

    private CashLedgerRowDto? _selectedRow;
    public CashLedgerRowDto? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    // --- Split Expense state ---
    private bool _isSplitExpenseMode;
    public bool IsSplitExpenseMode { get => _isSplitExpenseMode; set => SetProperty(ref _isSplitExpenseMode, value); }

    public ObservableCollection<SplitExpenseLineVm> SplitExpenseLines { get; } = new();
    public ObservableCollection<string> AvailableCategories { get; } = new();

    private string _splitExpenseNotes = string.Empty;
    public string SplitExpenseNotes { get => _splitExpenseNotes; set => SetProperty(ref _splitExpenseNotes, value); }

    private DateTime _splitExpenseDate = DateTime.Today;
    public DateTime SplitExpenseDate { get => _splitExpenseDate; set => SetProperty(ref _splitExpenseDate, value); }

    private string _splitExpenseCurrency = "EGP";
    public string SplitExpenseCurrency { get => _splitExpenseCurrency; set => SetProperty(ref _splitExpenseCurrency, value); }

    public decimal SplitExpenseTotal => SplitExpenseLines.Sum(l => l.Amount);

    private string? _splitExpenseError;
    public string? SplitExpenseError { get => _splitExpenseError; set => SetProperty(ref _splitExpenseError, value); }

    // --- Split Income state ---
    private bool _isSplitIncomeMode;
    public bool IsSplitIncomeMode { get => _isSplitIncomeMode; set => SetProperty(ref _isSplitIncomeMode, value); }

    public ObservableCollection<SplitIncomeLineVm> SplitIncomeLines { get; } = new();

    private string _splitIncomeNotes = string.Empty;
    public string SplitIncomeNotes { get => _splitIncomeNotes; set => SetProperty(ref _splitIncomeNotes, value); }

    private DateTime _splitIncomeDate = DateTime.Today;
    public DateTime SplitIncomeDate { get => _splitIncomeDate; set => SetProperty(ref _splitIncomeDate, value); }

    private string _splitIncomeCurrency = "EGP";
    public string SplitIncomeCurrency { get => _splitIncomeCurrency; set => SetProperty(ref _splitIncomeCurrency, value); }

    public decimal SplitIncomeTotal => SplitIncomeLines.Sum(l => l.Amount);

    private string? _splitIncomeError;
    public string? SplitIncomeError { get => _splitIncomeError; set => SetProperty(ref _splitIncomeError, value); }

    // --- Split Expense methods ---
    private void StartSplitExpense()
    {
        IsSplitExpenseMode = true;
        IsSplitIncomeMode = false;
        SplitExpenseLines.Clear();
        SplitExpenseLines.Add(new SplitExpenseLineVm());
        SplitExpenseLines.Add(new SplitExpenseLineVm());
        SplitExpenseNotes = string.Empty;
        SplitExpenseDate = DateTime.Today;
        SplitExpenseError = null;
        _ = LoadCategoriesAsync();
    }

    private void CancelSplitExpense()
    {
        IsSplitExpenseMode = false;
        SplitExpenseLines.Clear();
        SplitExpenseError = null;
    }

    private void AddSplitExpenseLine() => SplitExpenseLines.Add(new SplitExpenseLineVm());

    private void RemoveSplitExpenseLine(SplitExpenseLineVm? line)
    {
        if (line != null && SplitExpenseLines.Count > 2)
            SplitExpenseLines.Remove(line);
    }

    private async Task SubmitSplitExpenseAsync()
    {
        if (_splitExpenseHandler == null) return;

        var selectedAccount = AccountFilterOptions.FirstOrDefault(a => a != "All" && SelectedAccountFilter == a);
        var accountId = _selectedAccountId ?? _allAccountIds.FirstOrDefault();
        if (accountId == Guid.Empty)
        {
            SplitExpenseError = "No account available.";
            return;
        }

        try
        {
            var lines = SplitExpenseLines.Select(l => new SplitLineDto(l.Category, l.Amount, string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes)).ToList();
            var total = lines.Sum(l => l.Amount);

            await _splitExpenseHandler.HandleAsync(new RecordSplitExpenseCommand(
                accountId,
                DateOnly.FromDateTime(SplitExpenseDate),
                total,
                SplitExpenseCurrency,
                lines,
                string.IsNullOrWhiteSpace(SplitExpenseNotes) ? null : SplitExpenseNotes),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Split expense recorded");
            CancelSplitExpense();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SplitExpenseError = ex.Message;
            _toastService?.Error("Split expense failed", ex);
        }
    }

    // --- Split Income methods ---
    private void StartSplitIncome()
    {
        IsSplitIncomeMode = true;
        IsSplitExpenseMode = false;
        SplitIncomeLines.Clear();
        SplitIncomeLines.Add(new SplitIncomeLineVm());
        SplitIncomeLines.Add(new SplitIncomeLineVm());
        SplitIncomeNotes = string.Empty;
        SplitIncomeDate = DateTime.Today;
        SplitIncomeError = null;
    }

    private void CancelSplitIncome()
    {
        IsSplitIncomeMode = false;
        SplitIncomeLines.Clear();
        SplitIncomeError = null;
    }

    private void AddSplitIncomeLine() => SplitIncomeLines.Add(new SplitIncomeLineVm());

    private void RemoveSplitIncomeLine(SplitIncomeLineVm? line)
    {
        if (line != null && SplitIncomeLines.Count > 2)
            SplitIncomeLines.Remove(line);
    }

    private async Task SubmitSplitIncomeAsync()
    {
        if (_splitIncomeHandler == null) return;

        var accountId = _selectedAccountId ?? _allAccountIds.FirstOrDefault();
        if (accountId == Guid.Empty)
        {
            SplitIncomeError = "No account available.";
            return;
        }

        try
        {
            var lines = SplitIncomeLines.Select(l => new IncomeSplitLineDto(l.Source, l.Amount)).ToList();
            var total = lines.Sum(l => l.Amount);

            await _splitIncomeHandler.HandleAsync(new RecordSplitIncomeCommand(
                accountId,
                DateOnly.FromDateTime(SplitIncomeDate),
                total,
                SplitIncomeCurrency,
                lines,
                string.IsNullOrWhiteSpace(SplitIncomeNotes) ? null : SplitIncomeNotes),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Split income recorded");
            CancelSplitIncome();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SplitIncomeError = ex.Message;
            _toastService?.Error("Split income failed", ex);
        }
    }

    private async Task LoadCategoriesAsync()
    {
        if (_categoriesHandler == null) return;
        try
        {
            var cats = await _categoriesHandler.HandleAsync(CancellationToken.None);
            AvailableCategories.Clear();
            foreach (var c in cats.Where(c => !c.IsArchived && c.Kind == "expense"))
                AvailableCategories.Add(c.Name);
        }
        catch { /* Non-critical */ }
    }

    // Account tracking for split commands
    private Guid? _selectedAccountId;
    private readonly List<Guid> _allAccountIds = new();

    public async Task LoadAsync()
    {
        if (_ledgerHandler == null) return;
        IsLoading = true;

        try
        {
            var result = await _ledgerHandler.HandleAsync(new CashLedgerQuery(), CancellationToken.None);

            _allRows.Clear();
            _allRows.AddRange(result.Rows);

            Rows.Clear();
            foreach (var row in result.Rows)
                Rows.Add(row);

            TotalIncome = result.TotalIncome;
            TotalExpense = result.TotalExpense;
            NetCashflow = result.NetCashflow;

            // Refresh account filter options
            await LoadAccountFiltersAsync();
            await LoadIncomeSourcesAsync();

            // Refresh category list for split expense
            if (IsSplitExpenseMode)
                await LoadCategoriesAsync();

            IsEmpty = Rows.Count == 0;
            RowsView.Refresh();
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load cash ledger", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadAccountFiltersAsync()
    {
        if (_accountsHandler == null) return;

        try
        {
            var accounts = await _accountsHandler.HandleAsync(CancellationToken.None);
            AccountFilterOptions.Clear();
            AccountFilterOptions.Add("All");
            _allAccountIds.Clear();
            foreach (var a in accounts.Where(a => !a.IsArchived))
            {
                AccountFilterOptions.Add(a.Name);
                _allAccountIds.Add(a.AccountId);
            }
            // Track selected account ID for split commands
            if (SelectedAccountFilter != "All")
            {
                var match = accounts.FirstOrDefault(a => a.Name == SelectedAccountFilter);
                _selectedAccountId = match?.AccountId;
            }
            else
            {
                _selectedAccountId = _allAccountIds.FirstOrDefault();
            }
        }
        catch
        {
            // Non-critical
        }
    }

    private bool FilterRow(object obj)
    {
        if (obj is not CashLedgerRowDto row) return false;

        if (!IncludeTransfers && row.Direction == "Transfer") return false;

        if (SelectedAccountFilter != "All" && row.AccountName != SelectedAccountFilter) return false;

        if (FilterFromDate.HasValue)
        {
            var from = DateOnly.FromDateTime(FilterFromDate.Value);
            if (row.EffectiveDate < from) return false;
        }

        if (FilterToDate.HasValue)
        {
            var to = DateOnly.FromDateTime(FilterToDate.Value);
            if (row.EffectiveDate > to) return false;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            return row.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Category.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Reference.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.Notes.Contains(term, StringComparison.OrdinalIgnoreCase)
                || row.RelatedAccountName.Contains(term, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private async Task LoadIncomeSourcesAsync()
    {
        if (_incomeSourcesHandler == null) return;
        try
        {
            var sources = await _incomeSourcesHandler.HandleAsync(CancellationToken.None);
            AvailableIncomeSources.Clear();
            foreach (var s in sources.Where(s => !s.IsArchived))
                AvailableIncomeSources.Add(s);
        }
        catch { /* Non-critical */ }
    }

    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedAccountFilter = "All";
        IncludeTransfers = true;
        FilterFromDate = null;
        FilterToDate = null;
    }

    private async Task ExportToCsvAsync()
    {
        if (_exportService == null || Rows.Count == 0) return;

        try
        {
            var headers = new[] { "Date", "Account", "Direction", "Amount", "Currency", "Category", "Reference", "Notes", "Related Account" };
            var rows = Rows.Select(r => (IReadOnlyList<string?>)new[]
            {
                r.EffectiveDate.ToString("yyyy-MM-dd"),
                r.AccountName,
                r.Direction,
                r.Amount.ToString("F2"),
                r.CurrencyCode,
                r.Category,
                r.Reference,
                r.Notes,
                r.RelatedAccountName
            }).ToList();

            await _exportService.ExportCsvAsync("CashLedger", headers, rows);
            _toastService?.Success("Cash ledger exported");
        }
        catch (Exception ex)
        {
            _toastService?.Error("Export failed", ex);
        }
    }
}
