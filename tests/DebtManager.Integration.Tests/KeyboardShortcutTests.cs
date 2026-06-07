using DebtManager.Application.UseCases;
using DebtManager.Desktop.Services;
using DebtManager.Desktop.ViewModels;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;

namespace DebtManager.Integration.Tests;

public sealed class KeyboardShortcutTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteEventStore _eventStore;

    public KeyboardShortcutTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"KeyboardShortcutTests_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(factory);
    }

    public void Dispose()
    {
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
                if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                break;
            }
            catch (IOException) when (i < 29)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public void FocusRequestService_RaisesEvent_WithCorrectKey()
    {
        var service = new FocusRequestService();
        string? receivedKey = null;
        service.FocusRequested += (_, args) => receivedKey = args.TargetKey;

        service.RequestFocus(FocusTargets.PaymentsSearch);

        Assert.Equal(FocusTargets.PaymentsSearch, receivedKey);
    }

    [Fact]
    public void ShellViewModel_FocusSearchCommand_RequestsCorrectKey()
    {
        var focusService = new FocusRequestService();
        var shell = CreateShellViewModel(focusService);

        string? lastKey = null;
        focusService.FocusRequested += (_, args) => lastKey = args.TargetKey;

        // Obligations
        shell.CurrentView = "Obligations";
        shell.FocusSearchCommand.Execute(null);
        Assert.Equal(FocusTargets.ObligationsSearch, lastKey);

        // Payments
        lastKey = null;
        shell.CurrentView = "Payments";
        shell.FocusSearchCommand.Execute(null);
        Assert.Equal(FocusTargets.PaymentsSearch, lastKey);

        // Audit
        lastKey = null;
        shell.CurrentView = "Audit";
        shell.FocusSearchCommand.Execute(null);
        Assert.Equal(FocusTargets.AuditSearch, lastKey);

        // Dashboard ? no focus request
        lastKey = null;
        shell.CurrentView = "Dashboard";
        shell.FocusSearchCommand.Execute(null);
        Assert.Null(lastKey);
    }

    [Fact]
    public void ShellViewModel_CloseActiveDialogCommand_ClosesDialogsInPriorityOrder()
    {
        var shell = CreateShellViewModel();

        // Open all three dialogs
        shell.ShowCreateObligationDialog = true;
        shell.ShowRecordPaymentDialog = true;
        shell.ShowDefineScheduleDialog = true;

        // Priority 1: CreateObligation closed first
        shell.CloseActiveDialogCommand.Execute(null);
        Assert.False(shell.ShowCreateObligationDialog);
        Assert.True(shell.ShowRecordPaymentDialog);
        Assert.True(shell.ShowDefineScheduleDialog);

        // Priority 2: RecordPayment closed next
        shell.CloseActiveDialogCommand.Execute(null);
        Assert.False(shell.ShowRecordPaymentDialog);
        Assert.True(shell.ShowDefineScheduleDialog);

        // Priority 3: DefineSchedule closed next
        shell.CloseActiveDialogCommand.Execute(null);
        Assert.False(shell.ShowDefineScheduleDialog);

        // Priority 4: Settings view navigates away
        shell.CurrentView = "Settings";
        shell.CloseActiveDialogCommand.Execute(null);
        Assert.Equal("Dashboard", shell.CurrentView);

        // No-op when nothing is open
        shell.CurrentView = "Dashboard";
        shell.CloseActiveDialogCommand.Execute(null);
        Assert.Equal("Dashboard", shell.CurrentView);
    }

    private ShellViewModel CreateShellViewModel(IFocusRequestService? focusService = null)
    {
        var rulePackRepo = new SqliteRulePackRepository(
            new SqliteConnectionFactory(_dbPath, new TestKeyStore()));
        var resolver = new SqliteRulePackResolver(_eventStore);
        var ruleEngine = new SqliteRuleEngine(rulePackRepo, resolver);

        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var dashboardVm = new DashboardViewModel(dashboardHandler);

        return new ShellViewModel(
            _eventStore,
            ruleEngine,
            dashboardVm,
            actorUserId: Guid.NewGuid(),
            deviceId: Guid.NewGuid(),
            focusService: focusService);
    }
}
