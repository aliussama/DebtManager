using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed class DetectPortfolioCrisisHandler
{
    private readonly GetPortfolioTimelineHandler _timeline;

    public DetectPortfolioCrisisHandler(GetPortfolioTimelineHandler timeline)
    {
        _timeline = timeline;
    }

    public async Task<IReadOnlyList<CrisisWindow>> HandleAsync(DateOnly asOfDate, CancellationToken ct)
    {
        var timeline = await _timeline.HandleAsync(asOfDate, ct);
        var items = timeline.Items.OrderBy(x => x.Date).ToList();

        var windows = new List<CrisisWindow>();

        DateOnly? start = null;
        DateOnly? end = null;

        var lowest = Money.Zero(Currency.EGP);
        var lowestSet = false;

        var contributors = new List<CrisisContributor>();

        foreach (var it in items)
        {
            if (it.RunningBalance.Amount < 0m)
            {
                start ??= it.Date;
                end = it.Date;

                if (!lowestSet || it.RunningBalance.Amount < lowest.Amount)
                {
                    lowest = it.RunningBalance;
                    lowestSet = true;
                }

                if (it.Amount.Amount < 0m)
                {
                    contributors.Add(new CrisisContributor(
                        it.Date,
                        it.Type,
                        it.Amount,
                        it.Description));
                }
            }
            else if (start is not null)
            {
                windows.Add(new CrisisWindow(
                    start.Value,
                    end!.Value,
                    lowest,
                    contributors
                        .OrderByDescending(x => Math.Abs(x.Amount.Amount))
                        .Take(5)
                        .ToList()));

                start = null;
                end = null;
                lowest = Money.Zero(Currency.EGP);
                lowestSet = false;
                contributors.Clear();
            }
        }

        if (start is not null)
        {
            windows.Add(new CrisisWindow(
                start.Value,
                end!.Value,
                lowest,
                contributors
                    .OrderByDescending(x => Math.Abs(x.Amount.Amount))
                    .Take(5)
                    .ToList()));
        }

        return windows;
    }
}
