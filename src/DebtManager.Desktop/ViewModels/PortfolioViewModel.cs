using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class PortfolioViewModel : ObservableObject
{
    private readonly GetInvestmentPortfolioDashboardHandler? _dashboardHandler;
    private readonly RecordInvestmentTransactionHandler? _txnHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public PortfolioViewModel(
        GetInvestmentPortfolioDashboardHandler? dashboardHandler = null,
        RecordInvestmentTransactionHandler? txnHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _dashboardHandler = dashboardHandler;
        _txnHandler = txnHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }

    public ObservableCollection<InvestmentPositionDto> Positions { get; } = new();
    public ObservableCollection<InvestmentAccountDto> Accounts { get; } = new();

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private decimal _totalRealizedPnL;
    public decimal TotalRealizedPnL { get => _totalRealizedPnL; set => SetProperty(ref _totalRealizedPnL, value); }

    private decimal _totalUnrealizedPnL;
    public decimal TotalUnrealizedPnL { get => _totalUnrealizedPnL; set => SetProperty(ref _totalUnrealizedPnL, value); }

    private decimal _totalMarketValue;
    public decimal TotalMarketValue { get => _totalMarketValue; set => SetProperty(ref _totalMarketValue, value); }

    private decimal _totalCostBasis;
    public decimal TotalCostBasis { get => _totalCostBasis; set => SetProperty(ref _totalCostBasis, value); }

    private int _unvaluedPositionCount;
    public int UnvaluedPositionCount { get => _unvaluedPositionCount; set => SetProperty(ref _unvaluedPositionCount, value); }

    public async Task LoadAsync()
    {
        if (_dashboardHandler == null) return;
        IsLoading = true;
        try
        {
            var dashboard = await _dashboardHandler.HandleAsync(ct: CancellationToken.None);
            Positions.Clear();
            Accounts.Clear();
            foreach (var p in dashboard.Positions) Positions.Add(p);
            foreach (var a in dashboard.Accounts) Accounts.Add(a);
            TotalRealizedPnL = dashboard.TotalRealizedPnL;
            TotalUnrealizedPnL = dashboard.TotalUnrealizedPnL;
            TotalMarketValue = dashboard.TotalMarketValue;
            TotalCostBasis = dashboard.TotalCostBasis;
            UnvaluedPositionCount = dashboard.UnvaluedPositionCount;
            IsEmpty = Positions.Count == 0;
        }
        catch (Exception ex) { _toastService?.Error("Failed to load portfolio", ex); }
        finally { IsLoading = false; }
    }

    private async Task ExportAsync()
    {
        if (_exportService == null || Positions.Count == 0) return;
        var headers = new[] { "AccountId", "AssetId", "Symbol", "Quantity", "AvgCost", "TotalCost", "MarketPrice", "MarketValue", "UnrealizedPnL", "IsValued" };
        var rows = Positions.Select(p => (IReadOnlyList<string?>)new[]
        {
            p.AccountId.ToString(), p.AssetId.ToString(), p.Symbol,
            p.Quantity.ToString(), p.AvgCost.ToString("F4"), p.TotalCost.ToString("F2"),
            p.MarketPrice?.ToString("F2") ?? "", p.MarketValue?.ToString("F2") ?? "",
            p.UnrealizedPnL?.ToString("F2") ?? "", p.IsValued.ToString()
        }).ToList();
        await _exportService.ExportCsvAsync("Portfolio.csv", headers, rows);
    }
}
