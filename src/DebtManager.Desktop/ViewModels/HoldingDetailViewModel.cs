using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class HoldingDetailViewModel : ObservableObject
{
    private readonly GetHoldingDetailHandler? _detailHandler;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public HoldingDetailViewModel(
        GetHoldingDetailHandler? detailHandler = null,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _detailHandler = detailHandler;
        _toastService = toastService;
        _exportService = exportService;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ExportTransactionsCommand = new AsyncRelayCommand(ExportTransactionsAsync);
        ExportLotsCommand = new AsyncRelayCommand(ExportLotsAsync);
        ExportPnLCommand = new AsyncRelayCommand(ExportPnLAsync);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportTransactionsCommand { get; }
    public ICommand ExportLotsCommand { get; }
    public ICommand ExportPnLCommand { get; }

    private Guid _accountId;
    public Guid AccountId { get => _accountId; set => SetProperty(ref _accountId, value); }

    private Guid _assetId;
    public Guid AssetId { get => _assetId; set => SetProperty(ref _assetId, value); }

    private string _symbol = string.Empty;
    public string Symbol { get => _symbol; set => SetProperty(ref _symbol, value); }

    private decimal _quantity;
    public decimal Quantity { get => _quantity; set => SetProperty(ref _quantity, value); }

    private decimal _avgCost;
    public decimal AvgCost { get => _avgCost; set => SetProperty(ref _avgCost, value); }

    private decimal _totalCost;
    public decimal TotalCost { get => _totalCost; set => SetProperty(ref _totalCost, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public ObservableCollection<InvestmentLotDto> Lots { get; } = new();
    public ObservableCollection<InvestmentTransactionDto> Transactions { get; } = new();
    public ObservableCollection<RealizedPnLEntryDto> RealizedPnLEntries { get; } = new();

    public async Task LoadAsync()
    {
        if (_detailHandler == null) return;
        IsLoading = true;
        try
        {
            var detail = await _detailHandler.HandleAsync(AccountId, AssetId, ct: CancellationToken.None);
            if (detail == null) return;

            Symbol = detail.Symbol;
            Quantity = detail.Quantity;
            AvgCost = detail.AvgCost;
            TotalCost = detail.TotalCost;

            Lots.Clear();
            foreach (var l in detail.Lots) Lots.Add(l);

            Transactions.Clear();
            foreach (var t in detail.Transactions) Transactions.Add(t);

            RealizedPnLEntries.Clear();
            foreach (var r in detail.RealizedPnLEntries) RealizedPnLEntries.Add(r);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load holding detail", ex); }
        finally { IsLoading = false; }
    }

    private async Task ExportTransactionsAsync()
    {
        if (_exportService == null || Transactions.Count == 0) return;
        var headers = new[] { "TransactionId", "Type", "TradeDate", "Quantity", "PricePerUnit", "Fees", "Taxes", "Currency", "Notes", "IsReversed" };
        var rows = Transactions.Select(t => (IReadOnlyList<string?>)new[]
        {
            t.TransactionId.ToString(), t.TransactionType, t.TradeDate.ToString("yyyy-MM-dd"),
            t.Quantity.ToString(), t.PricePerUnit.ToString("F4"), t.Fees.ToString("F2"),
            t.Taxes.ToString("F2"), t.CurrencyCode, t.Notes, t.IsReversed.ToString()
        }).ToList();
        await _exportService.ExportCsvAsync($"Holding_{Symbol}_Transactions.csv", headers, rows);
    }

    private async Task ExportLotsAsync()
    {
        if (_exportService == null || Lots.Count == 0) return;
        var headers = new[] { "TransactionId", "TradeDate", "RemainingQuantity", "CostPerUnit" };
        var rows = Lots.Select(l => (IReadOnlyList<string?>)new[]
        {
            l.TransactionId.ToString(), l.TradeDate.ToString("yyyy-MM-dd"),
            l.RemainingQuantity.ToString(), l.CostPerUnit.ToString("F4")
        }).ToList();
        await _exportService.ExportCsvAsync($"Holding_{Symbol}_Lots.csv", headers, rows);
    }

    private async Task ExportPnLAsync()
    {
        if (_exportService == null || RealizedPnLEntries.Count == 0) return;
        var headers = new[] { "SellTransactionId", "TradeDate", "Symbol", "QuantitySold", "Proceeds", "CostBasis", "RealizedGain" };
        var rows = RealizedPnLEntries.Select(r => (IReadOnlyList<string?>)new[]
        {
            r.SellTransactionId.ToString(), r.TradeDate.ToString("yyyy-MM-dd"), r.Symbol,
            r.QuantitySold.ToString(), r.Proceeds.ToString("F2"), r.CostBasis.ToString("F2"),
            r.RealizedGain.ToString("F2")
        }).ToList();
        await _exportService.ExportCsvAsync($"Holding_{Symbol}_PnL.csv", headers, rows);
    }
}
