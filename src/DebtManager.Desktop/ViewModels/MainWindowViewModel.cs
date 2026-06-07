using DebtManager.Infrastructure.Persistence;
using DebtManager.Domain.Rules;

namespace DebtManager.Desktop.ViewModels;

public sealed class MainWindowViewModel
{
    public PortfolioStatusViewModel PortfolioStatus { get; }

    public MainWindowViewModel(SqliteEventStore store, IRuleEngine ruleEngine)
    {
        PortfolioStatus = new PortfolioStatusViewModel(store, ruleEngine);
    }
}
