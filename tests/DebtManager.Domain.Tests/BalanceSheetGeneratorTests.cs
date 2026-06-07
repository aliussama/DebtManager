using DebtManager.Domain.Projections;
using DebtManager.Reporting.Models;
using DebtManager.Reporting.Services;

namespace DebtManager.Domain.Tests;

/// <summary>
/// B6 Tests: BalanceSheetGenerator must be deterministic and exclude unknown values correctly.
/// </summary>
public sealed class BalanceSheetGeneratorTests
{
    [Fact]
    public void AssetsGroupedCorrectly()
    {
        var netWorthState = new NetWorthState
        {
            AsOfDate = new DateOnly(2025, 1, 15),
            ReportingCurrency = "EGP"
        };

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Cash",
            Name = "Wallet",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 1000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 1000m,
            IsValued = true
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Asset",
            SubCategory = "RealEstate",
            Name = "Apartment",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 2_000_000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 2_000_000m,
            IsValued = true
        });

        netWorthState.TotalAssets = 2_001_000m;
        netWorthState.TotalCash = 1000m;
        netWorthState.TotalInvestmentAssets = 2_000_000m;

        var generator = new BalanceSheetGenerator();
        var report = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");

        Assert.Equal(2, report.Assets.Count);
        Assert.Contains(report.Assets, a => a.Category == "Cash & Cash Equivalents");
        Assert.Contains(report.Assets, a => a.Category == "Real Estate");
        Assert.Equal(2_001_000m, report.TotalAssets);
    }

    [Fact]
    public void LiabilitiesGroupedCorrectly()
    {
        var netWorthState = new NetWorthState
        {
            AsOfDate = new DateOnly(2025, 1, 15),
            ReportingCurrency = "EGP"
        };

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Liability",
            SubCategory = "Loan",
            Name = "Car Loan",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 150_000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 150_000m,
            IsValued = true
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Liability",
            SubCategory = "CreditCard",
            Name = "Visa Card",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 5000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 5000m,
            IsValued = true
        });

        netWorthState.TotalLiabilities = 155_000m;

        var generator = new BalanceSheetGenerator();
        var report = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");

        Assert.Equal(2, report.Liabilities.Count);
        Assert.Contains(report.Liabilities, l => l.Category == "Loans");
        Assert.Contains(report.Liabilities, l => l.Category == "Credit Cards");
        Assert.Equal(155_000m, report.TotalLiabilities);
    }

    [Fact]
    public void EquityComputedCorrectly()
    {
        var netWorthState = new NetWorthState
        {
            AsOfDate = new DateOnly(2025, 1, 15),
            ReportingCurrency = "EGP"
        };

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Bank",
            Name = "Checking",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 50_000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 50_000m,
            IsValued = true
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Liability",
            SubCategory = "Loan",
            Name = "Personal Loan",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 20_000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 20_000m,
            IsValued = true
        });

        netWorthState.TotalAssets = 50_000m;
        netWorthState.TotalLiabilities = 20_000m;

        var generator = new BalanceSheetGenerator();
        var report = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");

        Assert.Equal(30_000m, report.Equity);
    }

    [Fact]
    public void UnknownValuesExcludedFromTotals()
    {
        var netWorthState = new NetWorthState
        {
            AsOfDate = new DateOnly(2025, 1, 15),
            ReportingCurrency = "EGP",
            UnknownValueCount = 2
        };

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Cash",
            Name = "USD Wallet",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "USD",
            NativeAmount = 100m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 0m,
            IsValued = false,
            ValuationNote = "Missing FX rate USD->EGP"
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Asset",
            SubCategory = "Vehicle",
            Name = "Car",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 0m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 0m,
            IsValued = false,
            ValuationNote = "No price recorded"
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Bank",
            Name = "Bank Account",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 10_000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 10_000m,
            IsValued = true
        });

        netWorthState.TotalAssets = 10_000m;

        var generator = new BalanceSheetGenerator();
        var report = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");

        Assert.Equal(1, report.Assets.Count);
        Assert.Equal(10_000m, report.TotalAssets);
        Assert.Equal(2, report.UnknownExcludedCount);
    }

    [Fact]
    public void DeterministicOrderingStable()
    {
        var netWorthState = new NetWorthState
        {
            AsOfDate = new DateOnly(2025, 1, 15),
            ReportingCurrency = "EGP"
        };

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Asset",
            SubCategory = "Vehicle",
            Name = "Car",
            ReferenceId = Guid.NewGuid(),
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 200_000m,
            IsValued = true
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Cash",
            Name = "Wallet",
            ReferenceId = Guid.NewGuid(),
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 1000m,
            IsValued = true
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Bank",
            Name = "Checking",
            ReferenceId = Guid.NewGuid(),
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 50_000m,
            IsValued = true
        });

        netWorthState.TotalAssets = 251_000m;

        var generator = new BalanceSheetGenerator();
        var report1 = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");
        var report2 = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");

        Assert.Equal(report1.Assets.Count, report2.Assets.Count);
        for (int i = 0; i < report1.Assets.Count; i++)
        {
            Assert.Equal(report1.Assets[i].Name, report2.Assets[i].Name);
            Assert.Equal(report1.Assets[i].Category, report2.Assets[i].Category);
            Assert.Equal(report1.Assets[i].Amount, report2.Assets[i].Amount);
        }

        // Cash should come before vehicles
        Assert.True(report1.Assets[0].Category.Contains("Cash") || report1.Assets[0].Category.Contains("Bank"));
    }

    [Fact]
    public void ZeroData_ReturnsEmptyButValidReport()
    {
        var netWorthState = new NetWorthState
        {
            AsOfDate = new DateOnly(2025, 1, 15),
            ReportingCurrency = "EGP"
        };

        var generator = new BalanceSheetGenerator();
        var report = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");

        Assert.NotNull(report);
        Assert.Empty(report.Assets);
        Assert.Empty(report.Liabilities);
        Assert.Equal(0m, report.TotalAssets);
        Assert.Equal(0m, report.TotalLiabilities);
        Assert.Equal(0m, report.Equity);
        Assert.Equal(0, report.UnknownExcludedCount);
    }

    [Fact]
    public void MultiCurrency_HandledSafely()
    {
        var netWorthState = new NetWorthState
        {
            AsOfDate = new DateOnly(2025, 1, 15),
            ReportingCurrency = "EGP"
        };

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Bank",
            Name = "USD Account",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "USD",
            NativeAmount = 1000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 31_000m,
            IsValued = true
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Bank",
            Name = "EGP Account",
            ReferenceId = Guid.NewGuid(),
            NativeCurrencyCode = "EGP",
            NativeAmount = 10_000m,
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 10_000m,
            IsValued = true
        });

        netWorthState.TotalAssets = 41_000m;
        netWorthState.TotalCash = 41_000m;

        var generator = new BalanceSheetGenerator();
        var report = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");

        Assert.Equal(2, report.Assets.Count);
        Assert.Equal(41_000m, report.TotalAssets);
        Assert.Equal("EGP", report.ReportingCurrency);
    }

    [Fact]
    public void Determinism_SameInputSameReport()
    {
        var netWorthState = new NetWorthState
        {
            AsOfDate = new DateOnly(2025, 1, 15),
            ReportingCurrency = "EGP"
        };

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Cash",
            SubCategory = "Bank",
            Name = "Checking",
            ReferenceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 5000m,
            IsValued = true
        });

        netWorthState.Rows.Add(new NetWorthBreakdownRow
        {
            Category = "Liability",
            SubCategory = "Loan",
            Name = "Car Loan",
            ReferenceId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ReportingCurrencyCode = "EGP",
            ReportingAmount = 2000m,
            IsValued = true
        });

        netWorthState.TotalAssets = 5000m;
        netWorthState.TotalLiabilities = 2000m;

        var generator = new BalanceSheetGenerator();
        var report1 = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");
        var report2 = generator.Generate(netWorthState, netWorthState.AsOfDate, "EGP");

        Assert.Equal(report1.TotalAssets, report2.TotalAssets);
        Assert.Equal(report1.TotalLiabilities, report2.TotalLiabilities);
        Assert.Equal(report1.Equity, report2.Equity);
        Assert.Equal(report1.UnknownExcludedCount, report2.UnknownExcludedCount);
        Assert.Equal(report1.Assets.Count, report2.Assets.Count);
        Assert.Equal(report1.Liabilities.Count, report2.Liabilities.Count);
    }
}
