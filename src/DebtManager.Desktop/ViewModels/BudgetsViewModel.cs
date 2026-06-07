using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Projections;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class BudgetsViewModel : ObservableObject
{
    private readonly GetBudgetDashboardHandler? _dashboardHandler;
    private readonly DefineBudgetHandler? _defineHandler;
    private readonly ArchiveBudgetHandler? _archiveHandler;
    private readonly GetCategoriesListHandler? _categoriesHandler;
    private readonly GetAccountsListHandler? _accountsHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public BudgetsViewModel(
        GetBudgetDashboardHandler? dashboardHandler = null,
        DefineBudgetHandler? defineHandler = null,
        ArchiveBudgetHandler? archiveHandler = null,
        GetCategoriesListHandler? categoriesHandler = null,
        GetAccountsListHandler? accountsHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _dashboardHandler = dashboardHandler;
        _defineHandler = defineHandler;
        _archiveHandler = archiveHandler;
        _categoriesHandler = categoriesHandler;
        _accountsHandler = accountsHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ShowDefineCommand = new RelayCommand(() => IsDefineVisible = true);
        CancelDefineCommand = new RelayCommand(CancelDefine);
        ConfirmDefineCommand = new AsyncRelayCommand(ConfirmDefineAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => Utilizations.Count > 0);

        var now = DateTime.Today;
        SelectedYear = now.Year;
        SelectedMonth = now.Month;
        NewCurrency = "EGP";
        NewScopeType = "category";
        NewCarryPolicy = "None";
    }

    public ICommand RefreshCommand { get; }
    public ICommand ShowDefineCommand { get; }
    public ICommand CancelDefineCommand { get; }
    public ICommand ConfirmDefineCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<BudgetUtilizationRow> Utilizations { get; } = new();
    public ObservableCollection<CategoryListItemDto> AvailableCategories { get; } = new();
    public ObservableCollection<AccountListItemDto> AvailableAccounts { get; } = new();

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private int _selectedYear;
    public int SelectedYear { get => _selectedYear; set => SetProperty(ref _selectedYear, value); }

    private int _selectedMonth;
    public int SelectedMonth { get => _selectedMonth; set => SetProperty(ref _selectedMonth, value); }

    public ObservableCollection<int> YearOptions { get; } = new(Enumerable.Range(2024, 5));
    public ObservableCollection<int> MonthOptions { get; } = new(Enumerable.Range(1, 12));

    private decimal _totalLimit;
    public decimal TotalLimit { get => _totalLimit; set => SetProperty(ref _totalLimit, value); }

    private decimal _totalActual;
    public decimal TotalActual { get => _totalActual; set => SetProperty(ref _totalActual, value); }

    private decimal _totalRemaining;
    public decimal TotalRemaining { get => _totalRemaining; set => SetProperty(ref _totalRemaining, value); }

    // Define form
    private bool _isDefineVisible;
    public bool IsDefineVisible { get => _isDefineVisible; set => SetProperty(ref _isDefineVisible, value); }

    private string _newScopeType = "category";
    public string NewScopeType { get => _newScopeType; set => SetProperty(ref _newScopeType, value); }

    public ObservableCollection<string> ScopeTypes { get; } = new() { "category", "account", "category+account" };

    private CategoryListItemDto? _newCategory;
    public CategoryListItemDto? NewCategory { get => _newCategory; set => SetProperty(ref _newCategory, value); }

    private AccountListItemDto? _newAccount;
    public AccountListItemDto? NewAccount { get => _newAccount; set => SetProperty(ref _newAccount, value); }

    private decimal _newLimit;
    public decimal NewLimit { get => _newLimit; set => SetProperty(ref _newLimit, value); }

    private string _newCurrency = "EGP";
    public string NewCurrency { get => _newCurrency; set => SetProperty(ref _newCurrency, value); }

    private string _newCarryPolicy = "None";
    public string NewCarryPolicy { get => _newCarryPolicy; set => SetProperty(ref _newCarryPolicy, value); }

    public ObservableCollection<string> CarryPolicies { get; } = new() { "None", "CarryUnused", "CarryOverspend" };
    public ObservableCollection<string> Currencies { get; } = new() { "EGP", "USD", "EUR" };

    public async Task LoadAsync()
    {
        if (_dashboardHandler == null) return;
        IsLoading = true;
        try
        {
            var result = await _dashboardHandler.HandleAsync(
                new BudgetDashboardQuery(SelectedYear, SelectedMonth), CancellationToken.None);
            Utilizations.Clear();
            foreach (var u in result.Utilizations) Utilizations.Add(u);
            TotalLimit = result.TotalLimit;
            TotalActual = result.TotalActual;
            TotalRemaining = result.TotalRemaining;
            IsEmpty = Utilizations.Count == 0;

            await LoadDropdownsAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to load budgets", ex); }
        finally { IsLoading = false; }
    }

    private async Task LoadDropdownsAsync()
    {
        if (_categoriesHandler != null)
        {
            var cats = await _categoriesHandler.HandleAsync(CancellationToken.None);
            AvailableCategories.Clear();
            foreach (var c in cats.Where(c => !c.IsArchived && c.Kind == "expense"))
                AvailableCategories.Add(c);
        }
        if (_accountsHandler != null)
        {
            var accts = await _accountsHandler.HandleAsync(CancellationToken.None);
            AvailableAccounts.Clear();
            foreach (var a in accts.Where(a => !a.IsArchived))
                AvailableAccounts.Add(a);
        }
    }

    private void CancelDefine()
    {
        IsDefineVisible = false;
        NewLimit = 0;
        NewCategory = null;
        NewAccount = null;
        NewScopeType = "category";
        NewCarryPolicy = "None";
    }

    private async Task ConfirmDefineAsync()
    {
        if (_defineHandler == null || NewLimit <= 0) return;
        try
        {
            await _defineHandler.HandleAsync(
                new DefineBudgetCommand(null, SelectedYear, SelectedMonth, NewCurrency,
                    NewScopeType, NewCategory?.CategoryId, NewAccount?.AccountId,
                    NewLimit, NewCarryPolicy),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Budget defined");
            CancelDefine();
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to define budget", ex); }
    }

    private async Task ExportCsvAsync()
    {
        if (_exportService == null || Utilizations.Count == 0) return;
        try
        {
            var headers = new[] { "Scope", "Currency", "Limit", "Actual", "Remaining", "% Used", "Status" };
            var rows = Utilizations.Select(u => (IReadOnlyList<string?>)new[]
            {
                u.ScopeLabel, u.CurrencyCode,
                u.LimitAmount.ToString("F2"), u.ActualAmount.ToString("F2"),
                u.RemainingAmount.ToString("F2"), u.PercentUsed.ToString("F1"), u.Status
            }).ToList();
            await _exportService.ExportCsvAsync("BudgetUtilization", headers, rows);
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }
}
