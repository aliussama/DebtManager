using DebtManager.Application.Simulation;
using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Projections.Installments;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Scenario Simulation (What-If) view.
/// Allows users to simulate hypothetical scenarios without modifying real data.
/// </summary>
public sealed class ScenarioSimulationViewModel : ObservableObject
{
    private readonly SimulateScenarioHandler? _simulateHandler;
    private readonly GetObligationsListHandler? _obligationsHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly string _defaultCurrencyCode;

    public ScenarioSimulationViewModel(
        SimulateScenarioHandler? simulateHandler = null,
        GetObligationsListHandler? obligationsHandler = null,
        Guid actorUserId = default,
        Guid deviceId = default,
        IToastService? toastService = null,
        IExportService? exportService = null,
        string defaultCurrencyCode = "EGP")
    {
        _simulateHandler = simulateHandler;
        _obligationsHandler = obligationsHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;
        _defaultCurrencyCode = defaultCurrencyCode;

        // Initialize commands
        RefreshCommand = new AsyncRelayCommand(LoadObligationsAsync);
        RunSimulationCommand = new AsyncRelayCommand(RunSimulationAsync, CanRunSimulation);
        AddHypothesisCommand = new RelayCommand(AddHypothesis);
        RemoveHypothesisCommand = new RelayCommand<HypothesisItemVm>(RemoveHypothesis);
        ExportCsvCommand = new AsyncRelayCommand(ExportToCsvAsync, CanExportCsv);
        ClearResultsCommand = new RelayCommand(ClearResults);

        // Default dates
        AsOfDate = DateTime.Today;
        HorizonEndDate = DateTime.Today.AddMonths(6);
    }

    // Commands
    public ICommand RefreshCommand { get; }
    public ICommand RunSimulationCommand { get; }
    public ICommand AddHypothesisCommand { get; }
    public ICommand RemoveHypothesisCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ClearResultsCommand { get; }

    // Collections
    public ObservableCollection<ObligationDropdownItem> Obligations { get; } = new();
    public ObservableCollection<HypothesisItemVm> Hypotheses { get; } = new();
    public ObservableCollection<InstallmentDeltaRowItem> Deltas { get; } = new();

    // Hypothesis type options
    public string[] HypothesisTypeOptions { get; } = { "ExtraPayment", "OneTimeExpense", "MissPayment", "DelayedPayment" };

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
                ClearResults();
            }
        }
    }

    public bool HasSelectedObligation => SelectedObligation != null && SelectedObligation.Id.HasValue;

    // Dates
    private DateTime _asOfDate;
    public DateTime AsOfDate
    {
        get => _asOfDate;
        set => SetProperty(ref _asOfDate, value);
    }

    private DateTime _horizonEndDate;
    public DateTime HorizonEndDate
    {
        get => _horizonEndDate;
        set => SetProperty(ref _horizonEndDate, value);
    }

    // Selected hypothesis for removal
    private HypothesisItemVm? _selectedHypothesis;
    public HypothesisItemVm? SelectedHypothesis
    {
        get => _selectedHypothesis;
        set => SetProperty(ref _selectedHypothesis, value);
    }

    // Status
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private bool _isSimulating;
    public bool IsSimulating
    {
        get => _isSimulating;
        set => SetProperty(ref _isSimulating, value);
    }

    private string _statusText = "Select an obligation and add hypotheses to simulate";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    // Result properties
    private bool _hasResult;
    public bool HasResult
    {
        get => _hasResult;
        set => SetProperty(ref _hasResult, value);
    }

    // Baseline summary
    private decimal _baselineTotalPayments;
    public decimal BaselineTotalPayments
    {
        get => _baselineTotalPayments;
        set => SetProperty(ref _baselineTotalPayments, value);
    }

    private int _baselineChargesCount;
    public int BaselineChargesCount
    {
        get => _baselineChargesCount;
        set => SetProperty(ref _baselineChargesCount, value);
    }

    private int _baselineOverdueInstallments;
    public int BaselineOverdueInstallments
    {
        get => _baselineOverdueInstallments;
        set => SetProperty(ref _baselineOverdueInstallments, value);
    }

    // Scenario summary
    private decimal _scenarioTotalPayments;
    public decimal ScenarioTotalPayments
    {
        get => _scenarioTotalPayments;
        set => SetProperty(ref _scenarioTotalPayments, value);
    }

    private int _scenarioChargesCount;
    public int ScenarioChargesCount
    {
        get => _scenarioChargesCount;
        set => SetProperty(ref _scenarioChargesCount, value);
    }

    private int _scenarioOverdueInstallments;
    public int ScenarioOverdueInstallments
    {
        get => _scenarioOverdueInstallments;
        set => SetProperty(ref _scenarioOverdueInstallments, value);
    }

    // Delta counts
    private int _installmentDeltasCount;
    public int InstallmentDeltasCount
    {
        get => _installmentDeltasCount;
        set => SetProperty(ref _installmentDeltasCount, value);
    }

    // Empty state helpers
    public bool HasObligations => Obligations.Count > 0;
    public bool HasNoObligations => Obligations.Count == 0;
    public bool HasHypotheses => Hypotheses.Count > 0;
    public bool HasDeltas => Deltas.Count > 0;

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
            var obligations = await _obligationsHandler.HandleAsync(asOfDate, _defaultCurrencyCode, CancellationToken.None);

            Obligations.Clear();
            Obligations.Add(new ObligationDropdownItem(null, "-- Select Obligation --"));

            foreach (var o in obligations.Where(x => !x.IsClosed))
            {
                Obligations.Add(new ObligationDropdownItem(o.ObligationId, o.Name));
            }

            OnPropertyChanged(nameof(HasObligations));
            OnPropertyChanged(nameof(HasNoObligations));

            StatusText = $"Loaded {Obligations.Count - 1} obligation(s). Select one to simulate.";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading obligations: {ex.Message}";
            _toastService?.Error("Failed to load obligations", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanRunSimulation()
    {
        return HasSelectedObligation && !IsSimulating && Hypotheses.Count > 0;
    }

    private async Task RunSimulationAsync()
    {
        if (_simulateHandler == null || !HasSelectedObligation || SelectedObligation?.Id == null)
        {
            _toastService?.Warning("Please select an obligation first");
            return;
        }

        if (Hypotheses.Count == 0)
        {
            _toastService?.Warning("Please add at least one hypothesis");
            return;
        }

        // Validate hypotheses
        var validationErrors = ValidateHypotheses();
        if (validationErrors.Count > 0)
        {
            _toastService?.Warning($"Invalid hypotheses: {string.Join(", ", validationErrors)}");
            return;
        }

        IsSimulating = true;
        StatusText = "Running simulation...";
        ClearResults();

        try
        {
            var hypotheses = Hypotheses.Select(h => h.ToHypothesis()).ToList();

            var command = new SimulateScenarioCommand(
                ObligationId: SelectedObligation.Id.Value,
                AsOfDate: DateOnly.FromDateTime(AsOfDate),
                HorizonEndDate: DateOnly.FromDateTime(HorizonEndDate),
                Hypotheses: hypotheses
            );

            var result = await _simulateHandler.HandleAsync(
                command,
                _actorUserId,
                _deviceId,
                CancellationToken.None
            );

            // Populate baseline summary
            BaselineTotalPayments = result.Diff.BaselineTotalPayments;
            BaselineChargesCount = result.Diff.BaselineChargesCount;
            BaselineOverdueInstallments = result.Diff.BaselineOverdueInstallments;

            // Populate scenario summary
            ScenarioTotalPayments = result.Diff.ScenarioTotalPayments;
            ScenarioChargesCount = result.Diff.ScenarioChargesCount;
            ScenarioOverdueInstallments = result.Diff.ScenarioOverdueInstallments;

            // Compute installment deltas
            ComputeInstallmentDeltas(result.Baseline.Installments, result.Scenario.Installments);

            HasResult = true;
            StatusText = $"Simulation complete. {Deltas.Count} installment(s) with differences.";
            _toastService?.Success("Simulation completed successfully");
        }
        catch (InvalidOperationException ex)
        {
            StatusText = $"Simulation error: {ex.Message}";
            _toastService?.Warning(ex.Message);
        }
        catch (Exception ex)
        {
            StatusText = $"Simulation failed: {ex.Message}";
            _toastService?.Error("Simulation failed", ex);
        }
        finally
        {
            IsSimulating = false;
        }
    }

    private void ComputeInstallmentDeltas(
        IReadOnlyCollection<InstallmentState> baselineInstallments,
        IReadOnlyCollection<InstallmentState> scenarioInstallments)
    {
        Deltas.Clear();

        var baselineByKey = baselineInstallments.ToDictionary(i => i.InstallmentKey);
        var scenarioByKey = scenarioInstallments.ToDictionary(i => i.InstallmentKey);

        var allKeys = baselineByKey.Keys.Union(scenarioByKey.Keys).ToHashSet();

        foreach (var key in allKeys)
        {
            var baseline = baselineByKey.TryGetValue(key, out var b) ? b : null;
            var scenario = scenarioByKey.TryGetValue(key, out var s) ? s : null;

            var baselineOutstanding = baseline?.Outstanding.Amount ?? 0m;
            var scenarioOutstanding = scenario?.Outstanding.Amount ?? 0m;
            var delta = scenarioOutstanding - baselineOutstanding;

            // Only show rows with differences
            if (Math.Abs(delta) > 0.01m)
            {
                var dueDate = baseline?.DueDate ?? scenario?.DueDate ?? DateOnly.MinValue;

                Deltas.Add(new InstallmentDeltaRowItem(
                    InstallmentKey: key,
                    DueDate: dueDate,
                    BaselineOutstanding: baselineOutstanding,
                    ScenarioOutstanding: scenarioOutstanding,
                    Delta: delta
                ));
            }
        }

        // Sort by delta descending (largest impact first)
        var sorted = Deltas.OrderByDescending(d => Math.Abs(d.Delta)).ToList();
        Deltas.Clear();
        foreach (var item in sorted)
        {
            Deltas.Add(item);
        }

        InstallmentDeltasCount = Deltas.Count;
        OnPropertyChanged(nameof(HasDeltas));
    }

    private List<string> ValidateHypotheses()
    {
        var errors = new List<string>();

        for (int i = 0; i < Hypotheses.Count; i++)
        {
            var h = Hypotheses[i];
            var prefix = $"Hypothesis {i + 1}";

            if (h.Type == "ExtraPayment" || h.Type == "OneTimeExpense")
            {
                if (!h.Amount.HasValue || h.Amount.Value <= 0)
                    errors.Add($"{prefix}: Amount must be > 0");
                if (string.IsNullOrWhiteSpace(h.CurrencyCode))
                    errors.Add($"{prefix}: Currency is required");
            }
            else if (h.Type == "MissPayment")
            {
                if (string.IsNullOrWhiteSpace(h.PaymentReferenceContains))
                    errors.Add($"{prefix}: Payment reference filter is required");
            }
            else if (h.Type == "DelayedPayment")
            {
                if (string.IsNullOrWhiteSpace(h.PaymentReferenceContains))
                    errors.Add($"{prefix}: Payment reference filter is required");
                if (!h.NewEffectiveDate.HasValue)
                    errors.Add($"{prefix}: New effective date is required");
            }
        }

        return errors;
    }

    private void AddHypothesis()
    {
        Hypotheses.Add(new HypothesisItemVm
        {
            Type = "ExtraPayment",
            EffectiveDate = DateTime.Today,
            Amount = 1000m,
            CurrencyCode = _defaultCurrencyCode,
            Reference = $"Scenario payment {Hypotheses.Count + 1}"
        });

        OnPropertyChanged(nameof(HasHypotheses));
    }

    private void RemoveHypothesis(HypothesisItemVm? item)
    {
        if (item != null)
        {
            Hypotheses.Remove(item);
            OnPropertyChanged(nameof(HasHypotheses));
        }
    }

    private bool CanExportCsv()
    {
        return HasResult && Deltas.Count > 0;
    }

    private async Task ExportToCsvAsync()
    {
        if (!CanExportCsv())
        {
            _toastService?.Warning("No results to export");
            return;
        }

        if (_exportService == null)
        {
            _toastService?.Error("Export service not available");
            return;
        }

        var headers = new List<string>
        {
            "InstallmentKey", "DueDate", "BaselineOutstanding", "ScenarioOutstanding", "Delta"
        };

        var rows = Deltas
            .Select(delta => new List<string?>
            {
                delta.InstallmentKey.ToString(),
                delta.DueDate.ToString("yyyy-MM-dd"),
                delta.BaselineOutstanding.ToString("F2"),
                delta.ScenarioOutstanding.ToString("F2"),
                delta.Delta.ToString("F2")
            } as IReadOnlyList<string?>)
            .ToList();

        await _exportService.ExportCsvAsync(
            $"ScenarioDeltas_{SelectedObligation?.Name ?? "unknown"}_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            headers,
            rows,
            CancellationToken.None);
    }

    private void ClearResults()
    {
        HasResult = false;
        BaselineTotalPayments = 0;
        BaselineChargesCount = 0;
        BaselineOverdueInstallments = 0;
        ScenarioTotalPayments = 0;
        ScenarioChargesCount = 0;
        ScenarioOverdueInstallments = 0;
        InstallmentDeltasCount = 0;
        Deltas.Clear();
        OnPropertyChanged(nameof(HasDeltas));
    }
}

/// <summary>
/// ViewModel for a hypothesis item in the list.
/// </summary>
public sealed class HypothesisItemVm : ObservableObject
{
    private string _type = "ExtraPayment";
    public string Type
    {
        get => _type;
        set
        {
            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(ShowAmountFields));
                OnPropertyChanged(nameof(ShowPaymentReferenceField));
                OnPropertyChanged(nameof(ShowNewEffectiveDateField));
            }
        }
    }

    private DateTime _effectiveDate = DateTime.Today;
    public DateTime EffectiveDate
    {
        get => _effectiveDate;
        set => SetProperty(ref _effectiveDate, value);
    }

    private decimal? _amount;
    public decimal? Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }

    private string _currencyCode = "EGP";
    public string CurrencyCode
    {
        get => _currencyCode;
        set => SetProperty(ref _currencyCode, value);
    }

    private string? _reference;
    public string? Reference
    {
        get => _reference;
        set => SetProperty(ref _reference, value);
    }

    private string? _paymentReferenceContains;
    public string? PaymentReferenceContains
    {
        get => _paymentReferenceContains;
        set => SetProperty(ref _paymentReferenceContains, value);
    }

    private DateTime? _newEffectiveDate;
    public DateTime? NewEffectiveDate
    {
        get => _newEffectiveDate;
        set => SetProperty(ref _newEffectiveDate, value);
    }

    // Visibility helpers for different hypothesis types
    public bool ShowAmountFields => Type == "ExtraPayment" || Type == "OneTimeExpense";
    public bool ShowPaymentReferenceField => Type == "MissPayment" || Type == "DelayedPayment";
    public bool ShowNewEffectiveDateField => Type == "DelayedPayment";

    public Hypothesis ToHypothesis()
    {
        return new Hypothesis(
            Type: Enum.Parse<HypothesisType>(Type),
            EffectiveDate: DateOnly.FromDateTime(EffectiveDate),
            Amount: Amount,
            CurrencyCode: CurrencyCode,
            NewEffectiveDate: NewEffectiveDate.HasValue ? DateOnly.FromDateTime(NewEffectiveDate.Value) : null,
            PaymentReferenceContains: PaymentReferenceContains,
            Reference: Reference
        );
    }
}

/// <summary>
/// Row item for displaying installment deltas.
/// </summary>
public sealed record InstallmentDeltaRowItem(
    Guid InstallmentKey,
    DateOnly DueDate,
    decimal BaselineOutstanding,
    decimal ScenarioOutstanding,
    decimal Delta
)
{
    public string DueDateDisplay => DueDate.ToString("MMM dd, yyyy");
    public string InstallmentKeyDisplay => InstallmentKey.ToString()[..8] + "...";
    public string DeltaDisplay => Delta >= 0 ? $"+{Delta:N2}" : Delta.ToString("N2");
    public bool IsPositiveDelta => Delta > 0;
    public bool IsNegativeDelta => Delta < 0;
}
