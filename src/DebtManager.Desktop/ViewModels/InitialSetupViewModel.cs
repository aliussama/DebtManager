using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class InitialSetupViewModel : ObservableObject
{
    private readonly GetSetupStateHandler _getSetupState;
    private readonly CompleteInitialSetupHandler _completeSetup;
    private readonly CreateDefaultAccountsHandler _createAccounts;
    private readonly CreateDefaultCategoriesHandler _createCategories;
    private readonly SeedDemoDataHandler _seedDemo;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly Action _onCompleted;

    public const int StepCount = 3;

    public InitialSetupViewModel(
        GetSetupStateHandler getSetupState,
        CompleteInitialSetupHandler completeSetup,
        CreateDefaultAccountsHandler createAccounts,
        CreateDefaultCategoriesHandler createCategories,
        SeedDemoDataHandler seedDemo,
        Guid actorUserId,
        Guid deviceId,
        Action onCompleted,
        IToastService? toastService = null)
    {
        _getSetupState = getSetupState;
        _completeSetup = completeSetup;
        _createAccounts = createAccounts;
        _createCategories = createCategories;
        _seedDemo = seedDemo;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _onCompleted = onCompleted;
        _toastService = toastService;

        NextCommand = new RelayCommand(Next, () => CanNext);
        BackCommand = new RelayCommand(Back, () => CanBack);
        FinishCommand = new AsyncRelayCommand(FinishAsync, () => CanFinish);

        AvailableCurrencies.Add("EGP");
        AvailableCurrencies.Add("USD");
        AvailableCurrencies.Add("EUR");
        AvailableCurrencies.Add("GBP");
        AvailableCurrencies.Add("SAR");
        AvailableCurrencies.Add("AED");

        UpdateStepState();
    }

    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand FinishCommand { get; }

    public ObservableCollection<string> AvailableCurrencies { get; } = new();

    private int _currentStepIndex;
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        private set
        {
            if (SetProperty(ref _currentStepIndex, value))
                UpdateStepState();
        }
    }

    private string _stepTitle = string.Empty;
    public string StepTitle
    {
        get => _stepTitle;
        private set => SetProperty(ref _stepTitle, value);
    }

    private string _stepDescription = string.Empty;
    public string StepDescription
    {
        get => _stepDescription;
        private set => SetProperty(ref _stepDescription, value);
    }

    public bool CanNext => CurrentStepIndex < StepCount - 1 && !string.IsNullOrWhiteSpace(ReportingCurrency);
    public bool CanBack => CurrentStepIndex > 0;
    public bool CanFinish => CurrentStepIndex == StepCount - 1;
    public bool IsLastStep => CurrentStepIndex == StepCount - 1;

    // Step 1: Base config
    private string _reportingCurrency = "EGP";
    public string ReportingCurrency
    {
        get => _reportingCurrency;
        set
        {
            if (SetProperty(ref _reportingCurrency, value))
                NotifyNav();
        }
    }

    private int _fiscalYearStartMonth = 1;
    public int FiscalYearStartMonth
    {
        get => _fiscalYearStartMonth;
        set => SetProperty(ref _fiscalYearStartMonth, value);
    }

    // Step 2: Defaults + Demo
    private bool _createDefaultAccounts = true;
    public bool CreateDefaultAccounts
    {
        get => _createDefaultAccounts;
        set => SetProperty(ref _createDefaultAccounts, value);
    }

    private bool _createDefaultCategories = true;
    public bool CreateDefaultCategories
    {
        get => _createDefaultCategories;
        set => SetProperty(ref _createDefaultCategories, value);
    }

    private bool _seedDemoData;
    public bool SeedDemoData
    {
        get => _seedDemoData;
        set => SetProperty(ref _seedDemoData, value);
    }

    // Status
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    // Summary (for step 3)
    public string SummaryCurrency => ReportingCurrency;
    public string SummaryFiscalMonth => FiscalYearStartMonth switch
    {
        1 => "January", 2 => "February", 3 => "March", 4 => "April",
        5 => "May", 6 => "June", 7 => "July", 8 => "August",
        9 => "September", 10 => "October", 11 => "November", 12 => "December",
        _ => FiscalYearStartMonth.ToString()
    };
    public string SummaryAccounts => CreateDefaultAccounts ? "Yes — Cash, Bank, Savings" : "No";
    public string SummaryCategories => CreateDefaultCategories ? "Yes — Standard hierarchy" : "No";
    public string SummaryDemo => SeedDemoData ? "Yes — Sample data" : "No";

    private void Next()
    {
        if (CurrentStepIndex < StepCount - 1)
            CurrentStepIndex++;
    }

    private void Back()
    {
        if (CurrentStepIndex > 0)
            CurrentStepIndex--;
    }

    private async Task FinishAsync()
    {
        IsBusy = true;
        StatusText = "Setting up your workspace...";

        try
        {
            var setupId = Guid.NewGuid();

            if (CreateDefaultAccounts)
            {
                StatusText = "Creating default accounts...";
                await _createAccounts.HandleAsync(ReportingCurrency, setupId, _actorUserId, _deviceId, CancellationToken.None);
            }

            if (CreateDefaultCategories)
            {
                StatusText = "Creating default categories...";
                await _createCategories.HandleAsync(setupId, _actorUserId, _deviceId, CancellationToken.None);
            }

            if (SeedDemoData)
            {
                StatusText = "Seeding demo data...";
                await _seedDemo.HandleAsync(ReportingCurrency, _actorUserId, _deviceId, CancellationToken.None);
            }

            StatusText = "Completing setup...";
            await _completeSetup.HandleAsync(
                ReportingCurrency, FiscalYearStartMonth,
                CreateDefaultAccounts, CreateDefaultCategories, SeedDemoData,
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success("Initial setup complete!");
            _onCompleted();
        }
        catch (Exception ex)
        {
            StatusText = $"Setup failed: {ex.Message}";
            _toastService?.Error("Setup failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateStepState()
    {
        (StepTitle, StepDescription) = CurrentStepIndex switch
        {
            0 => ("Base Configuration", "Choose your reporting currency and fiscal year start month."),
            1 => ("Defaults & Demo", "Select which defaults to create and whether to seed demo data."),
            2 => ("Confirmation", "Review your selections and finish setup."),
            _ => ("", "")
        };

        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanBack));
        OnPropertyChanged(nameof(CanFinish));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(SummaryCurrency));
        OnPropertyChanged(nameof(SummaryFiscalMonth));
        OnPropertyChanged(nameof(SummaryAccounts));
        OnPropertyChanged(nameof(SummaryCategories));
        OnPropertyChanged(nameof(SummaryDemo));
    }

    private void NotifyNav()
    {
        OnPropertyChanged(nameof(CanNext));
        OnPropertyChanged(nameof(CanFinish));
    }
}
