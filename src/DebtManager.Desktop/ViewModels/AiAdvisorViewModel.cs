using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DebtManager.Desktop.ViewModels;

public sealed class AiAdvisorViewModel : ObservableObject
{
    private readonly GetAiDashboardHandler _dashboardHandler;
    private readonly RunAiAnalysisHandler _analysisHandler;
    private readonly ApproveAiProposalHandler _approveHandler;
    private readonly RejectAiProposalHandler _rejectHandler;
    private readonly UpdateAiSettingsHandler _settingsHandler;
    private readonly Guid _actorUserId;
    private readonly Guid _deviceId;
    private readonly IToastService? _toastService;
    private readonly IExportService? _exportService;

    public AiAdvisorViewModel(
        GetAiDashboardHandler dashboardHandler,
        RunAiAnalysisHandler analysisHandler,
        ApproveAiProposalHandler approveHandler,
        RejectAiProposalHandler rejectHandler,
        UpdateAiSettingsHandler settingsHandler,
        Guid actorUserId,
        Guid deviceId,
        IToastService? toastService = null,
        IExportService? exportService = null)
    {
        _dashboardHandler = dashboardHandler;
        _analysisHandler = analysisHandler;
        _approveHandler = approveHandler;
        _rejectHandler = rejectHandler;
        _settingsHandler = settingsHandler;
        _actorUserId = actorUserId;
        _deviceId = deviceId;
        _toastService = toastService;
        _exportService = exportService;

        RunAnalysisCommand = new AsyncRelayCommand(RunAnalysisAsync);
        ToggleAiCommand = new AsyncRelayCommand(ToggleAiAsync);
        ApproveProposalCommand = new RelayCommand<AiProposalDto>(p => { if (p != null) _ = ApproveProposalAsync(p); });
        RejectProposalCommand = new RelayCommand<AiProposalDto>(p => { if (p != null) _ = RejectProposalAsync(p); });
        ExportInsightsCsvCommand = new RelayCommand(ExportInsightsCsv);
    }

    public ObservableCollection<AiInsightDto> Insights { get; } = new();
    public ObservableCollection<AiProposalDto> Proposals { get; } = new();

    private bool _isAiEnabled;
    public bool IsAiEnabled
    {
        get => _isAiEnabled;
        set => SetProperty(ref _isAiEnabled, value);
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    private string _rejectionReason = string.Empty;
    public string RejectionReason
    {
        get => _rejectionReason;
        set => SetProperty(ref _rejectionReason, value);
    }

    public ICommand RunAnalysisCommand { get; }
    public ICommand ToggleAiCommand { get; }
    public ICommand ApproveProposalCommand { get; }
    public ICommand RejectProposalCommand { get; }
    public ICommand ExportInsightsCsvCommand { get; }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var dashboard = await _dashboardHandler.HandleAsync(CancellationToken.None);
            IsAiEnabled = dashboard.Settings.Enabled;

            Insights.Clear();
            foreach (var i in dashboard.Insights)
                Insights.Add(i);

            Proposals.Clear();
            foreach (var p in dashboard.Proposals)
                Proposals.Add(p);

            StatusMessage = $"Loaded {dashboard.Insights.Count} insights, {dashboard.Proposals.Count} proposals";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RunAnalysisAsync()
    {
        if (!IsAiEnabled)
        {
            _toastService?.Warning("AI Advisor is disabled. Enable it first.");
            return;
        }

        IsLoading = true;
        StatusMessage = "Running AI analysis...";
        try
        {
            var (insightCount, proposalCount) = await _analysisHandler.HandleAsync(
                new RunAiAnalysisCommand(DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);

            _toastService?.Success($"Analysis complete: {insightCount} new insights, {proposalCount} new proposals");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Analysis failed: {ex.Message}";
            _toastService?.Error($"Analysis failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ToggleAiAsync()
    {
        try
        {
            var newEnabled = !IsAiEnabled;
            await _settingsHandler.HandleAsync(
                new UpdateAiSettingsCommand(newEnabled, false, false, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);

            IsAiEnabled = newEnabled;
            _toastService?.Success(newEnabled ? "AI Advisor enabled" : "AI Advisor disabled");
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Failed to update settings: {ex.Message}");
        }
    }

    private async Task ApproveProposalAsync(AiProposalDto? proposal)
    {
        if (proposal == null) return;
        try
        {
            await _approveHandler.HandleAsync(
                new ApproveAiProposalCommand(proposal.ProposalId, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Proposal approved");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Approval failed: {ex.Message}");
        }
    }

    private async Task RejectProposalAsync(AiProposalDto? proposal)
    {
        if (proposal == null) return;
        var reason = string.IsNullOrWhiteSpace(RejectionReason) ? "Rejected by user" : RejectionReason;
        try
        {
            await _rejectHandler.HandleAsync(
                new RejectAiProposalCommand(proposal.ProposalId, reason, DateOnly.FromDateTime(DateTime.Today)),
                _actorUserId, _deviceId, CancellationToken.None);
            _toastService?.Success("Proposal rejected");
            RejectionReason = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            _toastService?.Error($"Rejection failed: {ex.Message}");
        }
    }

    private void ExportInsightsCsv()
    {
        if (_exportService == null || Insights.Count == 0) return;
        var headers = new List<string> { "InsightCode", "Severity", "Area", "Title", "Message", "Date" };
        var rows = Insights.Select(i => (IReadOnlyList<string?>)new List<string?> { i.InsightCode, i.Severity, i.Area, i.Title, i.Message, i.RecordedDate.ToString("yyyy-MM-dd") }).ToList();
        _ = _exportService.ExportCsvAsync("ai_insights.csv", headers, rows);
    }
}
