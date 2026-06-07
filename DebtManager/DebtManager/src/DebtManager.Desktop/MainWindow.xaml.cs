using DebtManager.Application.Simulation;
using DebtManager.Application.UseCases;
using DebtManager.Desktop.Security;
using DebtManager.Desktop.ViewModels;
using DebtManager.Domain.Events;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Scheduling;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace DebtManager.Desktop;

public partial class MainWindow : Window
{
    // fixed IDs so we can click buttons in any order and keep the same “scenario”
    private readonly Guid _obligationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _scheduleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _actorUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly Guid _deviceId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private readonly SqliteEventStore _store;
    private readonly IRuleEngine _ruleEngine;
    private readonly MainWindowViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        // 1) pick db path (use a real persistent path, not temp)
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebtManager",
            "debtmanager.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // 2) create store
        var factory = new SqliteConnectionFactory(dbPath, new DpapiKeyStore()); // or your actual KeyStore used in desktop
        _store = new SqliteEventStore(factory);

        // 3) create rule engine (use your real one)
        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(_store);
        _ruleEngine = new SqliteRuleEngine(repo, resolver);

        // 4) view model
        _vm = new MainWindowViewModel(_store, _ruleEngine);
        DataContext = _vm;

        // 5) initial refresh
        _ = _vm.PortfolioStatus.RefreshAsync(DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
    }
    private async Task RefreshPortfolioAsync()
    {
        // choose “today” as the portfolio as-of date for now
        var asOf = DateOnly.FromDateTime(DateTime.Today);
        await _vm.PortfolioStatus.RefreshAsync(asOf, CancellationToken.None);
    }
    private DebtManager.Domain.Projections.FinancialState? _lastState;
    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var handler = App.Services.GetRequiredService<CreateObligationHandler>();
        await handler.HandleAsync(
            new CreateObligationCommand(
                _obligationId, "Tuition", "Education", 30000, "EGP", new DateOnly(2026, 9, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        SummaryText.Text = "Obligation created.";

        await RefreshPortfolioAsync();
    }
    private async void Schedule_Click(object sender, RoutedEventArgs e)
    {
        var handler = App.Services.GetRequiredService<DefineScheduleHandler>();

        var spec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 9, 15), 10000),
                new FixedDateItem(new DateOnly(2026, 11, 30), 10000),
                new FixedDateItem(new DateOnly(2027, 2, 28), 10000),
            },
            new[] { "tuition", "education" });

        await handler.HandleAsync(
            new DefineScheduleCommand(
                _scheduleId,
                _obligationId,
                "fixed_dates",
                JsonSerializer.Serialize(spec, DebtManager.Domain.ValueObjects.DomainJson.Options),
                "Africa/Cairo",
                new DateOnly(2026, 9, 1)
            ),
            _actorUserId, _deviceId, CancellationToken.None);

        SummaryText.Text = "Schedule defined.";

        await RefreshPortfolioAsync();
    }

    private async void Pay_Click(object sender, RoutedEventArgs e)
    {
        var record = App.Services.GetRequiredService<RecordPaymentHandler>();

        await record.HandleAsync(
            new RecordPaymentCommand(
                _obligationId,
                12000,
                "EGP",
                new DateOnly(2026, 9, 20),
                "12k payment"
            ),
            _actorUserId,
            _deviceId,
            CancellationToken.None);

        SummaryText.Text = "Payment recorded (self-sufficient handler).";

        await RefreshPortfolioAsync();
    }
    private async void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = App.Services.GetRequiredService<GetFinancialSnapshotHandler>();
        var state = await snapshot.HandleAsync(_obligationId, new DateOnly(2027, 3, 10), CancellationToken.None);

        SummaryText.Text =
            $"Payments: {state.TotalPayments} | Installments: {state.Installments.Count} | Charges: {state.Charges.Count} | Audit: {state.Audit.Count}";
        InstallmentsGrid.ItemsSource = state.Installments
            .OrderBy(i => i.DueDate)
            .Select(i => new
            {
                i.DueDate,
                Expected = i.Expected.ToString(),
                Paid = i.Paid.ToString(),
                Outstanding = i.Outstanding.ToString(),
                i.Status,
                i.DaysOverdue,
                i.Risk
            })
            .ToList();

        _lastState = state;

        AuditGrid.ItemsSource = state.Audit
            .OrderBy(a => a.EffectiveDate)
            .ThenBy(a => a.At)
            .Select(a => new
            {
                a.At,
                a.EffectiveDate,
                a.Category,
                a.Severity,
                a.Message,
                a.RuleKey,
                a.RelatedEventId,
                a.ObligationId
            })
            .ToList();

        await RefreshPortfolioAsync();
    }

    private async void InstallRules_Click(object sender, RoutedEventArgs e)
    {
        var install = App.Services.GetRequiredService<InstallRulePackHandler>();
        var assign = App.Services.GetRequiredService<AssignRulePackToObligationHandler>();

        // Step 11 sample rule pack JSON
        var rulesJson = """
{
  "rules": [
    {
      "key": "late_penalty_v1",
      "when": { "all": [ { "fact": "installment.days_overdue", "op": ">", "value": 0 } ] },
      "effect": { "add_charge": { "amount": 100, "label": "Late Penalty", "chargeType": "penalty" } }
    }
  ]
}
""";

        // 1) Install rule pack (stored in SQLite rule tables)
        await install.HandleAsync(
            new InstallRulePackCommand(
                RulePackId: "pack.tuition.basic",
                Name: "Tuition Basic Rules",
                Description: "Sample rules: late penalty when overdue",
                VersionLabel: "2026.01",
                EffectiveFrom: new DateOnly(2026, 1, 1),
                EffectiveTo: null,
                Status: "active",
                RulesJson: rulesJson
            ),
            CancellationToken.None);

        // 2) Assign rule pack to obligation (event store)
        await assign.HandleAsync(
            new AssignRulePackToObligationCommand(
                ObligationId: _obligationId,
                RulePackId: "pack.tuition.basic",
                EffectiveDate: new DateOnly(2026, 1, 1)
            ),
            _actorUserId,
            _deviceId,
            CancellationToken.None);

        SummaryText.Text = "Rule pack installed + assigned. Take an overdue snapshot to see charges.";

        await RefreshPortfolioAsync();
    }
    private async void Simulate_Click(object sender, RoutedEventArgs e)
    {
        var sim = App.Services.GetRequiredService<SimulateScenarioHandler>();

        var result = await sim.HandleAsync(
            new SimulateScenarioCommand(
                ObligationId: _obligationId,
                AsOfDate: new DateOnly(2026, 12, 31),
                HorizonEndDate: new DateOnly(2027, 12, 31),
                Hypotheses: new[]
                {
            new Hypothesis(
                Type: HypothesisType.ExtraPayment,
                EffectiveDate: new DateOnly(2026, 9, 25),
                Amount: 5000,
                CurrencyCode: "EGP",
                Reference: "Scenario +5k")
                }
            ),
            _actorUserId,
            _deviceId,
            CancellationToken.None);

        SummaryText.Text =
            $"Baseline Payments: {result.Baseline.TotalPayments.Amount} | " +
            $"Scenario Payments: {result.Scenario.TotalPayments.Amount} | " +
            $"Baseline Charges: {result.Baseline.Charges.Count} | " +
            $"Scenario Charges: {result.Scenario.Charges.Count}";

        InstallmentsGrid.ItemsSource = result.Scenario.Installments
            .OrderBy(i => i.DueDate)
            .Select(i => new
            {
                i.DueDate,
                Expected = i.Expected.ToString(),
                Paid = i.Paid.ToString(),
                Outstanding = i.Outstanding.ToString(),
                i.Status,
                i.DaysOverdue,
                i.Risk
            })
            .ToList();
    }
    private void ExportAudit_Click(object sender, RoutedEventArgs e)
    {
        if (_lastState is null)
        {
            MessageBox.Show("No snapshot available. Click Snapshot first.");
            return;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"DebtManager_Audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        using var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        sw.WriteLine("At,EffectiveDate,Category,Severity,Message,RuleKey,RelatedEventId,ObligationId");

        foreach (var a in _lastState.Audit.OrderBy(x => x.EffectiveDate).ThenBy(x => x.At))
        {
            static string Esc(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

            sw.WriteLine(
                $"{a.At:O}," +
                $"{a.EffectiveDate:yyyy-MM-dd}," +
                $"{Esc(a.Category)}," +
                $"{Esc(a.Severity)}," +
                $"{Esc(a.Message)}," +
                $"{Esc(a.RuleKey)}," +
                $"{a.RelatedEventId}," +
                $"{a.ObligationId}"
            );
        }

        MessageBox.Show($"Audit exported to:\n{path}");
    }
}
