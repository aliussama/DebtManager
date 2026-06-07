using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class GoalsViewModel : ObservableObject
{
    private readonly CreateFinancialGoalHandler _createHandler;
    private readonly ModifyFinancialGoalHandler _modifyHandler;
    private readonly ArchiveFinancialGoalHandler _archiveHandler;
    private readonly RecordGoalContributionHandler _contribHandler;
    private readonly ReverseGoalContributionHandler _reverseHandler;
    private readonly GetGoalsDashboardHandler _dashboardHandler;
    private readonly GetAccountsListHandler _accountsHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;
    private readonly TaggingMixin? _tagging;

    public GoalsViewModel(
        CreateFinancialGoalHandler createHandler,
        ModifyFinancialGoalHandler modifyHandler,
        ArchiveFinancialGoalHandler archiveHandler,
        RecordGoalContributionHandler contribHandler,
        ReverseGoalContributionHandler reverseHandler,
        GetGoalsDashboardHandler dashboardHandler,
        GetAccountsListHandler accountsHandler,
        Guid actorUserId, Guid deviceId,
        IToastService? toastService = null,
        IExportService? exportService = null,
        TaggingMixin? tagging = null)
    {
        _createHandler = createHandler;
        _modifyHandler = modifyHandler;
        _archiveHandler = archiveHandler;
        _contribHandler = contribHandler;
        _reverseHandler = reverseHandler;
        _dashboardHandler = dashboardHandler;
        _accountsHandler = accountsHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;
        _tagging = tagging;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateGoalCommand = new AsyncRelayCommand(CreateGoalAsync);
        ArchiveGoalCommand = new AsyncRelayCommand(ArchiveGoalAsync);
        RecordContributionCommand = new AsyncRelayCommand(RecordContributionAsync);
        ExportCsvCommand = new AsyncRelayCommand(ExportAsync);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateGoalCommand { get; }
    public ICommand ArchiveGoalCommand { get; }
    public ICommand RecordContributionCommand { get; }
    public ICommand ExportCsvCommand { get; }

    public ObservableCollection<GoalSummaryDto> Goals { get; } = new();
    public ObservableCollection<GoalContributionDto> Contributions { get; } = new();
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
    public ICommand SaveTagsCommand => new AsyncRelayCommand(async () => { if (_tagging != null && SelectedGoal != null) await _tagging.SaveTagsAsync(SelectedGoal.GoalId, "Goal", SelectedEntityTags, TagSuggestions); });

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set => SetProperty(ref _isEmpty, value); }

    private bool _includeArchived;
    public bool IncludeArchived
    {
        get => _includeArchived;
        set { if (SetProperty(ref _includeArchived, value)) _ = LoadAsync(); }
    }

    // Summary
    private decimal _totalTarget;
    public decimal TotalTarget { get => _totalTarget; set => SetProperty(ref _totalTarget, value); }

    private decimal _totalContributed;
    public decimal TotalContributed { get => _totalContributed; set => SetProperty(ref _totalContributed, value); }

    private decimal _overallProgress;
    public decimal OverallProgress { get => _overallProgress; set => SetProperty(ref _overallProgress, value); }

    // Create form
    private string _newGoalName = string.Empty;
    public string NewGoalName { get => _newGoalName; set => SetProperty(ref _newGoalName, value); }

    private string _newGoalType = "EmergencyFund";
    public string NewGoalType { get => _newGoalType; set => SetProperty(ref _newGoalType, value); }

    private decimal _newGoalTarget;
    public decimal NewGoalTarget { get => _newGoalTarget; set => SetProperty(ref _newGoalTarget, value); }

    private string _newGoalCurrency = "EGP";
    public string NewGoalCurrency { get => _newGoalCurrency; set => SetProperty(ref _newGoalCurrency, value); }

    private DateTime _newGoalTargetDate = DateTime.Today.AddYears(1);
    public DateTime NewGoalTargetDate { get => _newGoalTargetDate; set => SetProperty(ref _newGoalTargetDate, value); }

    private GoalSummaryDto? _selectedGoal;
    public GoalSummaryDto? SelectedGoal { get => _selectedGoal; set => SetProperty(ref _selectedGoal, value); }

    // Contribution form
    private decimal _contributionAmount;
    public decimal ContributionAmount { get => _contributionAmount; set => SetProperty(ref _contributionAmount, value); }

    private string _contributionCurrency = "EGP";
    public string ContributionCurrency { get => _contributionCurrency; set => SetProperty(ref _contributionCurrency, value); }

    private string _contributionReference = string.Empty;
    public string ContributionReference { get => _contributionReference; set => SetProperty(ref _contributionReference, value); }

    private AccountListItemDto? _selectedAccount;
    public AccountListItemDto? SelectedAccount { get => _selectedAccount; set => SetProperty(ref _selectedAccount, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var query = new GoalsDashboardQuery(
                AsOfDate: DateOnly.FromDateTime(DateTime.Today),
                IncludeArchived: IncludeArchived);
            var result = await _dashboardHandler.HandleAsync(query, CancellationToken.None);

            Goals.Clear();
            foreach (var g in result.Goals)
            {
                if (_tagFilteredIds != null && !_tagFilteredIds.Contains(g.GoalId)) continue;
                Goals.Add(g);
            }

            Contributions.Clear();
            foreach (var c in result.RecentContributions) Contributions.Add(c);

            TotalTarget = result.TotalTargetAmount;
            TotalContributed = result.TotalContributed;
            OverallProgress = result.OverallProgressPercent;
            IsEmpty = Goals.Count == 0;

            // Load accounts
            var accounts = await _accountsHandler.HandleAsync(CancellationToken.None);
            Accounts.Clear();
            foreach (var a in accounts.Where(a => !a.IsArchived)) Accounts.Add(a);

            if (_tagging != null) await _tagging.LoadSuggestionsAsync(TagSuggestions);
        }
        catch (Exception ex) { _toastService?.Error("Failed to load goals", ex); }
        finally { IsLoading = false; }
    }

    private async Task CreateGoalAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGoalName)) { _toastService?.Error("Goal name required"); return; }
        if (NewGoalTarget <= 0) { _toastService?.Error("Target amount must be positive"); return; }

        try
        {
            await _createHandler.HandleAsync(
                new CreateFinancialGoalCommand(null, NewGoalName.Trim(), NewGoalType,
                    NewGoalTarget, NewGoalCurrency,
                    DateOnly.FromDateTime(NewGoalTargetDate), null, [],
                    DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Goal created");
            NewGoalName = string.Empty;
            NewGoalTarget = 0;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to create goal", ex); }
    }

    private async Task ArchiveGoalAsync()
    {
        if (SelectedGoal == null) { _toastService?.Error("Select a goal first"); return; }
        try
        {
            await _archiveHandler.HandleAsync(
                new ArchiveFinancialGoalCommand(SelectedGoal.GoalId,
                    DateOnly.FromDateTime(DateTime.Today), "Archived by user"),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Goal archived");
            SelectedGoal = null;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to archive goal", ex); }
    }

    private async Task RecordContributionAsync()
    {
        if (SelectedGoal == null) { _toastService?.Error("Select a goal first"); return; }
        if (SelectedAccount == null) { _toastService?.Error("Select an account"); return; }
        if (ContributionAmount <= 0) { _toastService?.Error("Amount must be positive"); return; }

        try
        {
            await _contribHandler.HandleAsync(
                new RecordGoalContributionCommand(SelectedGoal.GoalId, null,
                    SelectedAccount.AccountId, ContributionAmount, ContributionCurrency,
                    DateOnly.FromDateTime(DateTime.Today), ContributionReference),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Contribution recorded");
            ContributionAmount = 0;
            ContributionReference = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex) { _toastService?.Error("Failed to record contribution", ex); }
    }

    private async Task ExportAsync()
    {
        if (_exportService == null || Goals.Count == 0) return;
        try
        {
            var headers = new[] { "Name", "Type", "TargetAmount", "TargetCurrency", "TargetDate",
                "Contributed", "Remaining", "ProgressPercent", "EstimatedCompletionDate", "Status", "Tags" };
            var rows = Goals.Select(g => (IReadOnlyList<string?>)new[]
            {
                g.Name, g.GoalType, g.TargetAmount.ToString("F2"), g.CurrencyCode,
                g.TargetDate.ToString("yyyy-MM-dd"), g.Contributed.ToString("F2"),
                g.Remaining.ToString("F2"), g.ProgressPercent.ToString("F2"),
                g.EstimatedCompletionDate?.ToString("yyyy-MM-dd") ?? "",
                g.Status, string.Join(";", g.Tags)
            }).ToList();
            await _exportService.ExportCsvAsync("Goals", headers, rows);
        }
        catch (Exception ex) { _toastService?.Error("Export failed", ex); }
    }

    private async Task ApplyTagFilterAsync()
    {
        if (_tagging != null && !string.IsNullOrEmpty(SelectedTagFilter))
            _tagFilteredIds = await _tagging.GetEntityIdsByTagAsync(SelectedTagFilter, "Goal");
        else
            _tagFilteredIds = null;
        await LoadAsync();
    }

    public async Task LoadTagsForSelectedGoalAsync()
    {
        if (_tagging != null && SelectedGoal != null)
            await _tagging.LoadEntityTagsAsync(SelectedGoal.GoalId, "Goal", SelectedEntityTags);
        else
            SelectedEntityTags.Clear();
    }
}
