using DebtManager.Domain.Ai;
using DebtManager.Domain.Forecasting;
using DebtManager.Domain.Projections;

namespace DebtManager.Application.Ai;

/// <summary>
/// Pure deterministic AI advisor engine. Reads projections and produces insights + proposals.
/// No randomness, no external calls, no side effects.
/// </summary>
public static class AiAdvisorEngine
{
    public sealed record AnalysisInput(
        CashLedgerState LedgerState,
        BudgetState BudgetState,
        CategoryState CategoryState,
        BillingState BillingState,
        GoalsState GoalsState,
        RetirementState RetirementState,
        PortfolioState PortfolioState,
        AssetsState AssetsState,
        DataQualityState DataQualityState,
        ForecastReport? ForecastReport,
        DateOnly AsOfDate);

    public sealed record AnalysisOutput(
        List<AiInsight> Insights,
        List<AiProposal> Proposals);

    public static AnalysisOutput Analyze(AnalysisInput input)
    {
        var insights = new List<AiInsight>();
        var proposals = new List<AiProposal>();

        AnalyzeCashHealth(input, insights, proposals);
        AnalyzeBudgets(input, insights, proposals);
        AnalyzeBilling(input, insights, proposals);
        AnalyzeGoals(input, insights, proposals);
        AnalyzeRetirement(input, insights, proposals);
        AnalyzePortfolio(input, insights, proposals);
        AnalyzeAssets(input, insights, proposals);
        AnalyzeForecast(input, insights, proposals);

        return new AnalysisOutput(insights, proposals);
    }

    private static void AnalyzeCashHealth(AnalysisInput input, List<AiInsight> insights, List<AiProposal> proposals)
    {
        var totalBalance = input.LedgerState.Accounts.Values
            .Where(a => !a.IsArchived)
            .Sum(a => a.Balance);

        if (totalBalance < 0)
        {
            insights.Add(new AiInsight
            {
                InsightId = DeterministicGuid("CashNegative", input.AsOfDate),
                InsightCode = "CASH_NEGATIVE_BALANCE",
                Severity = "Critical",
                Area = "Cash",
                Title = "Negative total cash balance",
                Message = $"Total cash balance across all accounts is {totalBalance:N2}. Immediate attention required.",
                RecordedDate = input.AsOfDate
            });
        }

        // Idle cash detection: if any single account has large balance and there are outstanding obligations
        var outstandingBills = input.BillingState.Bills.Values
            .Where(b => b.Status is "Due" or "PartiallyPaid" && !b.IsCancelled && !b.IsWrittenOff)
            .Sum(b => b.Outstanding);

        if (totalBalance > 0 && outstandingBills > 0 && totalBalance > outstandingBills * 2)
        {
            proposals.Add(new AiProposal
            {
                ProposalId = DeterministicGuid("ReallocateCashBills", input.AsOfDate),
                ProposalKind = "ReallocateCash",
                ProposalJson = $"{{\"idleCash\":{totalBalance:F2},\"outstandingBills\":{outstandingBills:F2}}}",
                Reason = $"Idle cash ({totalBalance:N2}) significantly exceeds outstanding bills ({outstandingBills:N2}). Consider paying bills to reduce outstanding balance.",
                RiskLevel = "Low",
                CreatedDate = input.AsOfDate
            });
        }
    }

    private static void AnalyzeBudgets(AnalysisInput input, List<AiInsight> insights, List<AiProposal> proposals)
    {
        var year = input.AsOfDate.Year;
        var month = input.AsOfDate.Month;

        var utilization = BudgetProjector.ComputeUtilization(
            input.BudgetState, input.LedgerState, input.CategoryState, year, month);

        var exceededCount = 0;
        foreach (var row in utilization)
        {
            if (row.Status == "Exceeded")
                exceededCount++;
        }

        if (exceededCount > 0)
        {
            insights.Add(new AiInsight
            {
                InsightId = DeterministicGuid($"BudgetExceeded_{year}_{month}", input.AsOfDate),
                InsightCode = "BUDGET_EXCEEDED",
                Severity = "Warning",
                Area = "Budget",
                Title = $"{exceededCount} budget(s) exceeded this month",
                Message = $"{exceededCount} budget limit(s) have been exceeded for {year}-{month:D2}. Review spending patterns.",
                RecordedDate = input.AsOfDate
            });

            proposals.Add(new AiProposal
            {
                ProposalId = DeterministicGuid($"AdjustBudget_{year}_{month}", input.AsOfDate),
                ProposalKind = "CreateBudget",
                ProposalJson = $"{{\"exceededCount\":{exceededCount},\"year\":{year},\"month\":{month}}}",
                Reason = $"{exceededCount} budgets exceeded. Consider adjusting limits or reducing spending.",
                RiskLevel = "Low",
                CreatedDate = input.AsOfDate
            });
        }
    }

    private static void AnalyzeBilling(AnalysisInput input, List<AiInsight> insights, List<AiProposal> proposals)
    {
        var overdueBills = input.BillingState.OverdueBills(input.AsOfDate);
        if (overdueBills.Count > 0)
        {
            var totalOverdue = overdueBills.Sum(b => b.Outstanding);
            insights.Add(new AiInsight
            {
                InsightId = DeterministicGuid("OverdueBills", input.AsOfDate),
                InsightCode = "BILLS_OVERDUE",
                Severity = "Warning",
                Area = "Billing",
                Title = $"{overdueBills.Count} overdue bill(s)",
                Message = $"{overdueBills.Count} bills are overdue with total outstanding of {totalOverdue:N2}. Pay promptly to avoid penalties.",
                RecordedDate = input.AsOfDate
            });
        }

        var overdueInvoices = input.BillingState.OverdueInvoices(input.AsOfDate);
        if (overdueInvoices.Count > 0)
        {
            insights.Add(new AiInsight
            {
                InsightId = DeterministicGuid("OverdueInvoices", input.AsOfDate),
                InsightCode = "INVOICES_OVERDUE",
                Severity = "Info",
                Area = "Billing",
                Title = $"{overdueInvoices.Count} overdue invoice(s)",
                Message = $"{overdueInvoices.Count} invoices are past due. Follow up on collections.",
                RecordedDate = input.AsOfDate
            });
        }
    }

    private static void AnalyzeGoals(AnalysisInput input, List<AiInsight> insights, List<AiProposal> proposals)
    {
        foreach (var goal in input.GoalsState.Goals.Values.Where(g => !g.IsArchived))
        {
            var progress = input.GoalsState.ProgressPercent(goal.GoalId);
            var remaining = input.GoalsState.RemainingAmount(goal.GoalId);
            var estimated = input.GoalsState.EstimatedCompletionDate(goal.GoalId, input.AsOfDate);

            if (estimated.HasValue && goal.TargetDate < estimated.Value)
            {
                insights.Add(new AiInsight
                {
                    InsightId = DeterministicGuid($"GoalBehind_{goal.GoalId}", input.AsOfDate),
                    InsightCode = "GOAL_BEHIND_SCHEDULE",
                    Severity = "Warning",
                    Area = "Goals",
                    Title = $"Goal '{goal.Name}' behind schedule",
                    Message = $"At current contribution rate, '{goal.Name}' will complete by {estimated.Value:yyyy-MM-dd}, after the target date of {goal.TargetDate:yyyy-MM-dd}. Consider increasing monthly contributions.",
                    RecordedDate = input.AsOfDate
                });
            }

            var avgMonthly = input.GoalsState.AvgMonthlyContribution(goal.GoalId, input.AsOfDate);
            if (progress < 50m && avgMonthly == 0m && remaining > 0)
            {
                insights.Add(new AiInsight
                {
                    InsightId = DeterministicGuid($"GoalNoContrib_{goal.GoalId}", input.AsOfDate),
                    InsightCode = "GOAL_NO_CONTRIBUTIONS",
                    Severity = "Info",
                    Area = "Goals",
                    Title = $"No recent contributions to '{goal.Name}'",
                    Message = $"Goal '{goal.Name}' has {progress:F1}% progress but no contributions in the last 6 months.",
                    RecordedDate = input.AsOfDate
                });
            }
        }
    }

    private static void AnalyzeRetirement(AnalysisInput input, List<AiInsight> insights, List<AiProposal> proposals)
    {
        var profile = input.RetirementState.ActiveProfile;
        var assumptions = input.RetirementState.ActiveAssumptions;
        if (profile == null || assumptions == null) return;

        var yearsToRetirement = (profile.RetirementDate.Year - input.AsOfDate.Year);
        if (yearsToRetirement <= 0) return;

        var monthlyTarget = profile.DesiredMonthlySpending.Amount;
        var yearsInRetirement = profile.LifeExpectancyYears - (profile.RetirementDate.Year - input.AsOfDate.Year);
        if (yearsInRetirement <= 0) return;

        var totalNeeded = monthlyTarget * 12m * yearsInRetirement;
        var monthlySavings = assumptions.CurrentMonthlySavings.Amount;

        if (monthlySavings <= 0)
        {
            insights.Add(new AiInsight
            {
                InsightId = DeterministicGuid("RetirementNoSavings", input.AsOfDate),
                InsightCode = "RETIREMENT_NO_SAVINGS",
                Severity = "Critical",
                Area = "Retirement",
                Title = "No retirement savings defined",
                Message = "Monthly retirement savings is zero. Consider setting up automated savings.",
                RecordedDate = input.AsOfDate
            });
            return;
        }

        var projectedSavings = monthlySavings * 12m * yearsToRetirement;
        var fundingRatio = totalNeeded > 0 ? projectedSavings / totalNeeded * 100m : 0m;

        if (fundingRatio < 50m)
        {
            proposals.Add(new AiProposal
            {
                ProposalId = DeterministicGuid("RetirementIncrease", input.AsOfDate),
                ProposalKind = "AdjustRecurring",
                ProposalJson = $"{{\"currentMonthlySavings\":{monthlySavings:F2},\"projectedFundingPercent\":{fundingRatio:F1},\"totalNeeded\":{totalNeeded:F2}}}",
                Reason = $"Retirement funding at {fundingRatio:F0}% of target. Consider increasing monthly savings from {monthlySavings:N2}.",
                RiskLevel = "Medium",
                CreatedDate = input.AsOfDate
            });
        }
        else if (fundingRatio < 80m)
        {
            insights.Add(new AiInsight
            {
                InsightId = DeterministicGuid("RetirementUnderfunded", input.AsOfDate),
                InsightCode = "RETIREMENT_UNDERFUNDED",
                Severity = "Warning",
                Area = "Retirement",
                Title = "Retirement plan underfunded",
                Message = $"Projected savings cover {fundingRatio:F0}% of retirement needs. Target is at least 80%.",
                RecordedDate = input.AsOfDate
            });
        }
    }

    private static void AnalyzePortfolio(AnalysisInput input, List<AiInsight> insights, List<AiProposal> proposals)
    {
        var activePositions = input.PortfolioState.Positions.Values
            .Where(p => p.Quantity > 0)
            .ToList();

        if (activePositions.Count == 0) return;

        var totalCost = activePositions.Sum(p => p.TotalCost);
        if (totalCost <= 0) return;

        foreach (var pos in activePositions)
        {
            var weight = pos.TotalCost / totalCost * 100m;
            if (weight > 40m)
            {
                insights.Add(new AiInsight
                {
                    InsightId = DeterministicGuid($"Concentration_{pos.AccountId}_{pos.AssetId}", input.AsOfDate),
                    InsightCode = "PORTFOLIO_CONCENTRATION",
                    Severity = "Warning",
                    Area = "Portfolio",
                    Title = $"High concentration in {pos.Symbol}",
                    Message = $"Position '{pos.Symbol}' represents {weight:F1}% of portfolio cost basis. Consider diversifying to reduce risk.",
                    RecordedDate = input.AsOfDate
                });
            }
        }
    }

    private static void AnalyzeAssets(AnalysisInput input, List<AiInsight> insights, List<AiProposal> proposals)
    {
        var assetsWithPrices = new HashSet<Guid>(input.AssetsState.Prices.Select(p => p.AssetId));

        foreach (var asset in input.AssetsState.Assets.Values.Where(a => !a.IsArchived))
        {
            if (!assetsWithPrices.Contains(asset.AssetId))
            {
                insights.Add(new AiInsight
                {
                    InsightId = DeterministicGuid($"AssetNoPrice_{asset.AssetId}", input.AsOfDate),
                    InsightCode = "ASSET_MISSING_PRICE",
                    Severity = "Info",
                    Area = "Assets",
                    Title = $"Asset '{asset.Name}' has no price data",
                    Message = $"Asset '{asset.Name}' ({asset.Symbol}) has no recorded price. Net worth and portfolio calculations may be inaccurate.",
                    RecordedDate = input.AsOfDate
                });
            }
        }
    }

    private static void AnalyzeForecast(AnalysisInput input, List<AiInsight> insights, List<AiProposal> proposals)
    {
        if (input.ForecastReport == null) return;

        foreach (var warning in input.ForecastReport.Summary.Warnings)
        {
            if (warning.Code == "NEGATIVE_BALANCE")
            {
                insights.Add(new AiInsight
                {
                    InsightId = DeterministicGuid($"ForecastNegBal_{warning.RelevantDate}", input.AsOfDate),
                    InsightCode = "FORECAST_NEGATIVE_BALANCE",
                    Severity = "Critical",
                    Area = "Forecast",
                    Title = "Forecast predicts negative balance",
                    Message = warning.Message,
                    RecordedDate = input.AsOfDate
                });

                proposals.Add(new AiProposal
                {
                    ProposalId = DeterministicGuid($"AdjustRecurringForecast_{warning.RelevantDate}", input.AsOfDate),
                    ProposalKind = "AdjustRecurring",
                    ProposalJson = $"{{\"warningCode\":\"{warning.Code}\",\"relevantDate\":\"{warning.RelevantDate}\"}}",
                    Reason = $"Forecast shows negative balance. Consider adjusting recurring expenses or increasing income.",
                    RiskLevel = "High",
                    CreatedDate = input.AsOfDate
                });
            }
        }

        if (input.ForecastReport.Summary.KnownEndBalance < 0)
        {
            insights.Add(new AiInsight
            {
                InsightId = DeterministicGuid("ForecastEndNeg", input.AsOfDate),
                InsightCode = "FORECAST_END_NEGATIVE",
                Severity = "Warning",
                Area = "Forecast",
                Title = "Forecast end balance is negative",
                Message = $"Projected end balance is {input.ForecastReport.Summary.KnownEndBalance:N2}. Action needed.",
                RecordedDate = input.AsOfDate
            });
        }
    }

    private static Guid DeterministicGuid(string key, DateOnly asOfDate)
    {
        var input = $"AiAdvisor:{key}:{asOfDate:yyyy-MM-dd}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        return new Guid(guidBytes);
    }
}
