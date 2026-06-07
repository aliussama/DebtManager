using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Charge Breakdown report view.
/// Shows charge summary and detail per obligation with export support.
/// </summary>
public sealed class ChargeBreakdownViewModel : ObservableObject
{
    private readonly GetChargeBreakdownReportHandler? _reportHandler;
    private readonly GetObligationsListHandler? _obligationsHandler;
    private readonly IExportService? _exportService;
    private readonly IToastService? _toastService;
    private readonly Action? _onCreateObligation;

    public ChargeBreakdownViewModel(
        GetChargeBreakdownReportHandler? reportHandler = null,
        GetObligationsListHandler? obligationsHandler = null,
        IExportService? exportService = null,
        IToastService? toastService = null,
        Action? onCreateObligation = null)
    {
        _reportHandler = reportHandler;
        _obligationsHandler = obligationsHandler;
        _exportService = exportService;
        _toastService = toastService;
        _onCreateObligation = onCreateObligation;

        // Initialize commands
        RefreshCommand = new AsyncRelayCommand(LoadReportAsync, CanRefresh);
        ExportCsvCommand = new AsyncRelayCommand(ExportToCsvAsync, CanExportCsv);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        CreateObligationCommand = new RelayCommand(() => _onCreateObligation?.Invoke());

        // Default date
        AsOfDate = DateTime.Today;
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand CreateObligationCommand { get; }

    // Collections
    public ObservableCollection<ObligationDropdownItem> Obligations { get; } = new();
    public ObservableCollection<ChargeTypeSummaryRowItem> SummaryRows { get; } = new();
    public ObservableCollection<ChargeItemRowItem> Items { get; } = new();

    // Selected obligation
    private ObligationDropdownItem? _selectedObligation;
    public ObligationDropdownItem? SelectedObligation
    {
        get => _selectedObligation;
        set
        {
            if (SetProperty(ref _selectedObligation, value))
            {
                OnPropertyChanged(nameof(HasSelectedObligation));
                if (value?.Id != null)
                {
                    _ = LoadReportAsync();
                }
            }
        }
    }

    public bool HasSelectedObligation => SelectedObligation?.Id != null;

    // As-of date
    private DateTime _asOfDate;
    public DateTime AsOfDate
    {
        get => _asOfDate;
        set => SetProperty(ref _asOfDate, value);
    }

    // Selected detail item
    private ChargeItemRowItem? _selectedItem;
    public ChargeItemRowItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(HasSelectedItem));
            }
        }
    }

    public bool HasSelectedItem => SelectedItem != null;

    // Status
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusText = "Select an obligation and click Refresh";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // Report metadata
    private string? _reportObligationName;
    public string? ReportObligationName
    {
        get => _reportObligationName;
        set => SetProperty(ref _reportObligationName, value);
    }

    // State helpers
    public bool HasObligations => Obligations.Count > 0;
    public bool HasNoObligations => Obligations.Count == 0 && !IsLoading;
    public bool HasCharges => Items.Count > 0;
    public bool HasNoCharges => Items.Count == 0 && HasSelectedObligation && !IsLoading;
    public bool HasReport => SummaryRows.Count > 0 || Items.Count > 0;

    /// <summary>
    /// Load obligations for the dropdown.
    /// </summary>
    public async Task LoadObligationsAsync()
    {
        if (_obligationsHandler == null) return;

        IsLoading = true;
        StatusText = "Loading obligations...";

        try
        {
            var asOfDate = DateOnly.FromDateTime(DateTime.Today);
            var obligations = await _obligationsHandler.HandleAsync(asOfDate, "EGP", CancellationToken.None);

            Obligations.Clear();
            Obligations.Add(new ObligationDropdownItem(null, "-- Select Obligation --"));
            foreach (var o in obligations)
            {
                Obligations.Add(new ObligationDropdownItem(o.ObligationId, o.Name));
            }

            OnPropertyChanged(nameof(HasObligations));
            OnPropertyChanged(nameof(HasNoObligations));

            if (Obligations.Count > 1)
            {
                StatusText = $"{Obligations.Count - 1} obligation(s) available. Select one.";
            }
            else
            {
                StatusText = "No obligations found. Create one first.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _toastService?.Error("Failed to load obligations", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRefresh()
    {
        return HasSelectedObligation && !IsLoading;
    }

    /// <summary>
    /// Load the charge breakdown report for the selected obligation.
    /// </summary>
    public async Task LoadReportAsync()
    {
        if (_reportHandler == null || SelectedObligation?.Id == null)
            return;

        IsLoading = true;
        StatusText = "Loading charge breakdown...";
        SummaryRows.Clear();
        Items.Clear();
        SelectedItem = null;
        ReportObligationName = null;

        try
        {
            var query = new GetChargeBreakdownReportQuery(
                ObligationId: SelectedObligation.Id.Value,
                AsOfDate: DateOnly.FromDateTime(AsOfDate)
            );

            var report = await _reportHandler.HandleAsync(query, CancellationToken.None);

            ReportObligationName = report.ObligationName;

            // Populate summaries
            foreach (var summary in report.Summaries)
            {
                SummaryRows.Add(new ChargeTypeSummaryRowItem(
                    ChargeType: summary.ChargeType,
                    TotalAssessed: summary.TotalAssessed,
                    TotalPaid: summary.TotalPaid,
                    Outstanding: summary.Outstanding,
                    Count: summary.Count,
                    CurrencyCode: summary.CurrencyCode
                ));
            }

            // Populate detail items
            foreach (var item in report.Items)
            {
                Items.Add(new ChargeItemRowItem(
                    ChargeId: item.ChargeId,
                    ChargeType: item.ChargeType,
                    EffectiveDate: item.EffectiveDate,
                    AssessedAmount: item.AssessedAmount,
                    PaidAmount: item.PaidAmount,
                    OutstandingAmount: item.OutstandingAmount,
                    CurrencyCode: item.CurrencyCode,
                    RelatedEventId: item.RelatedEventId,
                    Notes: item.Notes
                ));
            }

            OnPropertyChanged(nameof(HasCharges));
            OnPropertyChanged(nameof(HasNoCharges));
            OnPropertyChanged(nameof(HasReport));

            StatusText = report.Items.Count > 0
                ? $"{report.Items.Count} charge(s) across {report.Summaries.Count} type(s)"
                : "No charges as of selected date";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
            _toastService?.Error("Failed to load charge breakdown", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanExportCsv()
    {
        return HasReport;
    }

    private async Task ExportToCsvAsync()
    {
        if (_exportService == null)
        {
            _toastService?.Error("Export service not available");
            return;
        }

        if (SummaryRows.Count == 0 && Items.Count == 0)
        {
            _toastService?.Warning("No data to export");
            return;
        }

        var summaryHeaders = new List<string>
        {
            "ChargeType", "TotalAssessed", "TotalPaid", "Outstanding", "Count", "Currency"
        };

        var summaryRows = SummaryRows
            .Select(s => new List<string?>
            {
                s.ChargeType,
                s.TotalAssessed.ToString("F2"),
                s.TotalPaid.ToString("F2"),
                s.Outstanding.ToString("F2"),
                s.Count.ToString(),
                s.CurrencyCode
            } as IReadOnlyList<string?>)
            .ToList();

        var detailHeaders = new List<string>
        {
            "ChargeId", "ChargeType", "EffectiveDate", "AssessedAmount", "PaidAmount",
            "OutstandingAmount", "Currency", "RelatedEventId", "Notes"
        };

        var detailRows = Items
            .Select(item => new List<string?>
            {
                item.ChargeId.ToString(),
                item.ChargeType,
                item.EffectiveDate.ToString("yyyy-MM-dd"),
                item.AssessedAmount.ToString("F2"),
                item.PaidAmount.ToString("F2"),
                item.OutstandingAmount.ToString("F2"),
                item.CurrencyCode,
                item.RelatedEventId?.ToString(),
                item.Notes
            } as IReadOnlyList<string?>)
            .ToList();

        var obligationName = ReportObligationName ?? "unknown";

        await _exportService.ExportCustomCsvAsync(
            $"ChargeBreakdown_{obligationName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            writer =>
            {
                // Section 1: SUMMARY
                writer.Write("SUMMARY\r\n");
                CsvWriter.Write(writer, summaryHeaders, summaryRows);
                writer.Write("\r\n");

                // Section 2: DETAILS
                writer.Write("DETAILS\r\n");
                CsvWriter.Write(writer, detailHeaders, detailRows);
            },
            CancellationToken.None);
    }

    private void ClearSelection()
    {
        SelectedObligation = Obligations.FirstOrDefault();
        SummaryRows.Clear();
        Items.Clear();
        SelectedItem = null;
        ReportObligationName = null;
        OnPropertyChanged(nameof(HasCharges));
        OnPropertyChanged(nameof(HasNoCharges));
        OnPropertyChanged(nameof(HasReport));
        StatusText = "Select an obligation and click Refresh";
    }
}

/// <summary>
/// Row item for charge type summary display.
/// </summary>
public sealed record ChargeTypeSummaryRowItem(
    string ChargeType,
    decimal TotalAssessed,
    decimal TotalPaid,
    decimal Outstanding,
    int Count,
    string CurrencyCode
);

/// <summary>
/// Row item for individual charge detail display.
/// </summary>
public sealed record ChargeItemRowItem(
    Guid ChargeId,
    string ChargeType,
    DateOnly EffectiveDate,
    decimal AssessedAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    string CurrencyCode,
    Guid? RelatedEventId,
    string? Notes
)
{
    public string EffectiveDateDisplay => EffectiveDate.ToString("MMM dd, yyyy");
    public string ChargeIdDisplay => ChargeId.ToString()[..8] + "...";
    public string RelatedEventIdDisplay => RelatedEventId?.ToString()[..8] + "..." ?? "—";
}
