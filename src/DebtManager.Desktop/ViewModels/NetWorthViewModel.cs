using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Reporting.Models;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class NetWorthViewModel : ObservableObject
{
    private readonly GetNetWorthReportHandler? _reportHandler;
    private readonly GetBalanceSheetHandler? _balanceSheetHandler;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public NetWorthViewModel(
        GetNetWorthReportHandler? reportHandler = null,
        IToastService? toastService = null,
        IExportService? exportService = null,
        GetBalanceSheetHandler? balanceSheetHandler = null)
    {
        _reportHandler = reportHandler;
        _balanceSheetHandler = balanceSheetHandler;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ToggleBalanceSheetCommand = new AsyncRelayCommand(ToggleBalanceSheetAsync);

        AsOfDate = DateTime.Today;
        SelectedCurrency = "EGP";
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ToggleBalanceSheetCommand { get; }

    public ObservableCollection<NetWorthBreakdownRowDto> Rows { get; } = new();

    // B6 Balance Sheet properties
    private bool _isBalanceSheetMode;
    public bool IsBalanceSheetMode
    {
        get => _isBalanceSheetMode;
        set => SetProperty(ref _isBalanceSheetMode, value);
    }

    public ObservableCollection<BalanceSheetItemDto> BalanceSheetAssets { get; } = new();
    public ObservableCollection<BalanceSheetItemDto> BalanceSheetLiabilities { get; } = new();

    private decimal _balanceSheetTotalAssets;
    public decimal BalanceSheetTotalAssets
    {
        get => _balanceSheetTotalAssets;
        set => SetProperty(ref _balanceSheetTotalAssets, value);
    }

    private decimal _balanceSheetTotalLiabilities;
    public decimal BalanceSheetTotalLiabilities
    {
        get => _balanceSheetTotalLiabilities;
        set => SetProperty(ref _balanceSheetTotalLiabilities, value);
    }

    private decimal _balanceSheetEquity;
    public decimal BalanceSheetEquity
    {
        get => _balanceSheetEquity;
        set => SetProperty(ref _balanceSheetEquity, value);
    }

    private int _balanceSheetUnknownCount;
    public int BalanceSheetUnknownCount
    {
        get => _balanceSheetUnknownCount;
        set => SetProperty(ref _balanceSheetUnknownCount, value);
    }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private DateTime _asOfDate;
    public DateTime AsOfDate
    {
        get => _asOfDate;
        set
        {
            if (SetProperty(ref _asOfDate, value))
                _ = LoadAsync();
        }
    }

    private string _selectedCurrency = "EGP";
    public string SelectedCurrency
    {
        get => _selectedCurrency;
        set
        {
            if (SetProperty(ref _selectedCurrency, value))
                _ = LoadAsync();
        }
    }

    public ObservableCollection<string> Currencies { get; } = new()
    {
        "EGP", "USD", "EUR", "GBP", "SAR", "AED"
    };

    private decimal _totalAssets;
    public decimal TotalAssets { get => _totalAssets; set => SetProperty(ref _totalAssets, value); }

    private decimal _totalCash;
    public decimal TotalCash { get => _totalCash; set => SetProperty(ref _totalCash, value); }

    private decimal _totalInvestmentAssets;
    public decimal TotalInvestmentAssets { get => _totalInvestmentAssets; set => SetProperty(ref _totalInvestmentAssets, value); }

    private decimal _totalLiabilities;
    public decimal TotalLiabilities { get => _totalLiabilities; set => SetProperty(ref _totalLiabilities, value); }

    private decimal _knownNetWorth;
    public decimal KnownNetWorth { get => _knownNetWorth; set => SetProperty(ref _knownNetWorth, value); }

    private int _unknownValueCount;
    public int UnknownValueCount { get => _unknownValueCount; set => SetProperty(ref _unknownValueCount, value); }

    public async Task LoadAsync()
    {
        if (_reportHandler == null) return;
        IsLoading = true;
        try
        {
            var query = new GetNetWorthReportQuery(
                DateOnly.FromDateTime(AsOfDate),
                SelectedCurrency);
            var result = await _reportHandler.HandleAsync(query, CancellationToken.None);

            Rows.Clear();
            foreach (var row in result.Rows) Rows.Add(row);

            TotalAssets = result.TotalAssets;
            TotalCash = result.TotalCash;
            TotalInvestmentAssets = result.TotalInvestmentAssets;
            TotalLiabilities = result.TotalLiabilities;
            KnownNetWorth = result.KnownNetWorth;
            UnknownValueCount = result.UnknownValueCount;
            IsEmpty = Rows.Count == 0;

            // Load balance sheet if in balance sheet mode
            if (IsBalanceSheetMode)
            {
                await LoadBalanceSheetAsync();
            }
        }
        catch (Exception ex) { _toastService?.Error("Failed to load net worth report", ex); }
        finally { IsLoading = false; }
    }

    private async Task ToggleBalanceSheetAsync()
    {
        IsBalanceSheetMode = !IsBalanceSheetMode;
        if (IsBalanceSheetMode)
        {
            await LoadBalanceSheetAsync();
        }
    }

    private async Task LoadBalanceSheetAsync()
    {
        if (_balanceSheetHandler == null) return;

        try
        {
            var report = await _balanceSheetHandler.HandleAsync(
                DateOnly.FromDateTime(AsOfDate),
                SelectedCurrency,
                CancellationToken.None);

            BalanceSheetAssets.Clear();
            BalanceSheetLiabilities.Clear();

            foreach (var asset in report.Assets)
            {
                BalanceSheetAssets.Add(new BalanceSheetItemDto(
                    asset.EntityId,
                    asset.Name,
                    asset.Category,
                    asset.Amount,
                    asset.CurrencyCode));
            }

            foreach (var liability in report.Liabilities)
            {
                BalanceSheetLiabilities.Add(new BalanceSheetItemDto(
                    liability.EntityId,
                    liability.Name,
                    liability.Category,
                    liability.Amount,
                    liability.CurrencyCode));
            }

            BalanceSheetTotalAssets = report.TotalAssets;
            BalanceSheetTotalLiabilities = report.TotalLiabilities;
            BalanceSheetEquity = report.Equity;
            BalanceSheetUnknownCount = report.UnknownExcludedCount;
        }
        catch (Exception ex)
        {
            _toastService?.Error("Failed to load balance sheet", ex);
        }
    }

    private async Task ExportAsync()
    {
        if (_exportService == null || Rows.Count == 0) return;
        try
        {
            var headers = new[] { "Category", "SubCategory", "Name", "NativeCurrency", "NativeAmount", "ReportingCurrency", "ReportingAmount", "Valued", "Note" };
            var rows = Rows.Select(r => new[]
            {
                r.Category, r.SubCategory, r.Name, r.NativeCurrencyCode,
                r.NativeAmount.ToString("N2"), r.ReportingCurrencyCode,
                r.ReportingAmount.ToString("N2"), r.IsValued ? "Yes" : "No", r.ValuationNote
            }).ToList();
            await _exportService.ExportCsvAsync($"NetWorth_{AsOfDate:yyyy-MM-dd}", headers, rows);
            _toastService?.Success("Net worth report exported");
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }
}

/// <summary>
/// B6: DTO for balance sheet item display.
/// </summary>
public sealed record BalanceSheetItemDto(
    Guid? EntityId,
    string Name,
    string Category,
    decimal Amount,
    string CurrencyCode
)
{
    public string AmountDisplay => $"{Amount:N2}";
}
