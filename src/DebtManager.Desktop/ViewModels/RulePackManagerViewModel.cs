using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Domain.Events;
using DebtManager.Domain.Services.Rules;
using DebtManager.Domain.ValueObjects;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Rule Pack Manager view.
/// Allows installing sample packs and assigning packs to obligations.
/// </summary>
public sealed class RulePackManagerViewModel : ObservableObject
{
    private readonly IEventStore _eventStore;
    private readonly InstallRulePackHandler _installHandler;
    private readonly AssignRulePackToObligationHandler _assignHandler;
    private readonly GetInstalledRulePacksHandler _getPacksHandler;
    private readonly GetRulePackAssignmentHandler _getAssignmentHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;

    public RulePackManagerViewModel(
        IEventStore eventStore,
        InstallRulePackHandler installHandler,
        AssignRulePackToObligationHandler assignHandler,
        GetInstalledRulePacksHandler getPacksHandler,
        GetRulePackAssignmentHandler getAssignmentHandler,
        Guid actorUserId,
        Guid deviceId,
        IToastService? toastService = null)
    {
        _eventStore = eventStore;
        _installHandler = installHandler;
        _assignHandler = assignHandler;
        _getPacksHandler = getPacksHandler;
        _getAssignmentHandler = getAssignmentHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;

        InstallSamplePacksCommand = new AsyncRelayCommand(InstallSamplePacksAsync, () => !IsBusy);
        AssignRulePackCommand = new AsyncRelayCommand(AssignRulePackAsync, CanAssignRulePack);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);

        EffectiveDate = DateOnly.FromDateTime(DateTime.Today);
    }

    // Commands
    public ICommand InstallSamplePacksCommand { get; }
    public ICommand AssignRulePackCommand { get; }
    public ICommand RefreshCommand { get; }

    // Collections
    public ObservableCollection<ObligationOption> Obligations { get; } = new();
    public ObservableCollection<RulePackItem> RulePacks { get; } = new();

    // Selected items
    private ObligationOption? _selectedObligation;
    public ObligationOption? SelectedObligation
    {
        get => _selectedObligation;
        set
        {
            if (SetProperty(ref _selectedObligation, value))
            {
                _ = LoadCurrentAssignmentAsync();
            }
        }
    }

    private RulePackItem? _selectedRulePack;
    public RulePackItem? SelectedRulePack
    {
        get => _selectedRulePack;
        set => SetProperty(ref _selectedRulePack, value);
    }

    private DateOnly _effectiveDate;
    public DateOnly EffectiveDate
    {
        get => _effectiveDate;
        set => SetProperty(ref _effectiveDate, value);
    }

    // Status
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private string _currentAssignmentText = "No rule pack assigned";
    public string CurrentAssignmentText
    {
        get => _currentAssignmentText;
        set => SetProperty(ref _currentAssignmentText, value);
    }

    // Empty state helpers
    public bool HasRulePacks => RulePacks.Count > 0;
    public bool HasNoRulePacks => RulePacks.Count == 0;

    public async Task LoadAsync()
    {
        await LoadObligationsAsync();
        await LoadRulePacksAsync();
    }

    private async Task RefreshAsync()
    {
        await LoadAsync();
    }

    private async Task LoadObligationsAsync()
    {
        try
        {
            Obligations.Clear();

            var allEvents = await _eventStore.ReadAllAsync(
                new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
                CancellationToken.None);

            var obligationCreatedEvents = allEvents
                .Where(e => e.EventType == nameof(ObligationCreated))
                .ToList();

            foreach (var envelope in obligationCreatedEvents)
            {
                var created = JsonSerializer.Deserialize<ObligationCreated>(
                    envelope.PayloadJson, DomainJson.Options);

                if (created == null) continue;

                // Check if closed
                var obligationEvents = await _eventStore.ReadStreamAsync(
                    new StreamId(created.ObligationId),
                    upTo: null,
                    CancellationToken.None);

                var isClosed = obligationEvents.Any(e => e.EventType == nameof(ObligationClosed));

                if (!isClosed)
                {
                    Obligations.Add(new ObligationOption(
                        created.ObligationId,
                        created.Name,
                        created.CurrencyCode,
                        created.Principal.Amount
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading obligations: {ex.Message}";
        }
    }

    private async Task LoadRulePacksAsync()
    {
        try
        {
            RulePacks.Clear();

            var packs = await _getPacksHandler.HandleAsync(CancellationToken.None);

            foreach (var pack in packs)
            {
                RulePacks.Add(new RulePackItem(
                    PackId: pack.PackId,
                    Name: pack.Name,
                    VersionLabel: pack.VersionLabel,
                    EffectiveFrom: pack.EffectiveFrom,
                    Status: pack.Status,
                    RulesCount: pack.RulesCount
                ));
            }

            OnPropertyChanged(nameof(HasRulePacks));
            OnPropertyChanged(nameof(HasNoRulePacks));

            StatusText = $"Loaded {RulePacks.Count} rule pack(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error loading rule packs: {ex.Message}";
        }
    }

    private async Task LoadCurrentAssignmentAsync()
    {
        if (SelectedObligation == null)
        {
            CurrentAssignmentText = "Select an obligation to view assignment";
            return;
        }

        try
        {
            var assignment = await _getAssignmentHandler.HandleAsync(
                SelectedObligation.Id, CancellationToken.None);

            if (assignment != null)
            {
                var packName = assignment.PackName ?? assignment.PackId;
                CurrentAssignmentText = $"Assigned: {packName} (effective {assignment.EffectiveDate:yyyy-MM-dd})";
            }
            else
            {
                CurrentAssignmentText = "No rule pack assigned";
            }
        }
        catch
        {
            CurrentAssignmentText = "No rule pack assigned";
        }
    }

    private async Task InstallSamplePacksAsync()
    {
        IsBusy = true;
        StatusText = "Installing sample rule packs...";

        try
        {
            var loader = new RulePackLoader();
            var installed = 0;

            foreach (var (packId, json) in SampleRulePacks.All)
            {
                try
                {
                    var pack = loader.Load(json);
                    var version = pack.Versions.First();

                    await _installHandler.HandleAsync(
                        new InstallRulePackCommand(
                            RulePackId: pack.PackId,
                            Name: pack.DisplayName,
                            Description: $"Sample {pack.DisplayName} rule pack",
                            VersionLabel: version.VersionLabel,
                            EffectiveFrom: version.EffectiveFrom,
                            EffectiveTo: version.EffectiveTo,
                            Status: version.Status,
                            RulesJson: json
                        ),
                        CancellationToken.None);

                    installed++;
                }
                catch (Exception ex)
                {
                    // Pack might already exist, continue
                    StatusText = $"Warning: {packId} - {ex.Message}";
                }
            }

            await LoadRulePacksAsync();

            StatusText = $"Installed {installed} sample rule pack(s)";
            _toastService?.Success($"Installed {installed} sample rule packs");
        }
        catch (Exception ex)
        {
            StatusText = $"Error installing packs: {ex.Message}";
            _toastService?.Error("Failed to install sample packs", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanAssignRulePack()
    {
        return !IsBusy && SelectedObligation != null && SelectedRulePack != null;
    }

    private async Task AssignRulePackAsync()
    {
        if (SelectedObligation == null || SelectedRulePack == null)
        {
            _toastService?.Error("Please select an obligation and rule pack");
            return;
        }

        IsBusy = true;
        StatusText = "Assigning rule pack...";

        try
        {
            await _assignHandler.HandleAsync(
                new AssignRulePackToObligationCommand(
                    ObligationId: SelectedObligation.Id,
                    RulePackId: SelectedRulePack.PackId,
                    EffectiveDate: EffectiveDate
                ),
                _actorUserId,
                _deviceId,
                CancellationToken.None);

            await LoadCurrentAssignmentAsync();

            StatusText = $"Assigned {SelectedRulePack.Name} to {SelectedObligation.Name}";
            _toastService?.Success($"Rule pack assigned to {SelectedObligation.Name}");
        }
        catch (Exception ex)
        {
            StatusText = $"Error assigning pack: {ex.Message}";
            _toastService?.Error("Failed to assign rule pack", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// Item for displaying a rule pack in the UI.
/// </summary>
public sealed record RulePackItem(
    string PackId,
    string Name,
    string VersionLabel,
    DateOnly EffectiveFrom,
    string Status,
    int RulesCount
)
{
    public string EffectiveFromDisplay => EffectiveFrom.ToString("yyyy-MM-dd");
    public override string ToString() => $"{Name} (v{VersionLabel})";
}
