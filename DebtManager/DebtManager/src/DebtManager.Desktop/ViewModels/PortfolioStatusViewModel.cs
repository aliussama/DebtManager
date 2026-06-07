using DebtManager.Application.UseCases;
using DebtManager.Domain.Rules;
using DebtManager.Domain.Services.Rules;
using DebtManager.Infrastructure.Persistence;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DebtManager.Desktop.ViewModels;

public sealed class PortfolioStatusViewModel : INotifyPropertyChanged
{
    private readonly SqliteEventStore _store;
    private readonly IRuleEngine _ruleEngine;

    public PortfolioStatusViewModel(SqliteEventStore store, IRuleEngine ruleEngine)
    {
        _store = store;
        _ruleEngine = ruleEngine;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _bannerText = "Not loaded";
    public string BannerText
    {
        get => _bannerText;
        private set { _bannerText = value; OnPropertyChanged(); }
    }

    private bool _isInCrisis;
    public bool IsInCrisis
    {
        get => _isInCrisis;
        private set { _isInCrisis = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CrisisWindow> CrisisWindows { get; } = new();

    public async Task RefreshAsync(DateOnly asOfDate, CancellationToken ct)
    {
        try
        {
            if (_store is null)
                throw new InvalidOperationException("PortfolioStatusViewModel: _store is null (DI/initialization bug).");

            if (_ruleEngine is null)
                throw new InvalidOperationException("PortfolioStatusViewModel: _ruleEngine is null (DI/initialization bug).");

            var snapshots = new GetFinancialSnapshotHandler(_store, _ruleEngine);
            var timeline = new GetPortfolioTimelineHandler(_store, snapshots);
            var crisis = new DetectPortfolioCrisisHandler(timeline);

            var windows = await crisis.HandleAsync(asOfDate, ct);

            CrisisWindows.Clear();
            foreach (var w in windows)
                CrisisWindows.Add(w);

            if (windows.Count == 0)
            {
                IsInCrisis = false;
                BannerText = $"✅ No crisis detected (as of {asOfDate:yyyy-MM-dd})";
            }
            else
            {
                IsInCrisis = true;
                var first = windows[0];
                BannerText = $"⚠ Crisis from {first.Start:yyyy-MM-dd} to {first.End:yyyy-MM-dd} (lowest {first.LowestBalance})";
            }
        }
        catch (Exception ex)
        {
            IsInCrisis = false;
            BannerText = $"⚠ Portfolio status unavailable: {ex.Message}";
            CrisisWindows.Clear();
        }
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
