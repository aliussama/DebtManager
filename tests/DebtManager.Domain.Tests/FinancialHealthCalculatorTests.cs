using DebtManager.Domain.Projections;
using DebtManager.Domain.Quality;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Tests;

public sealed class FinancialHealthCalculatorTests
{
    // ================================================================
    // 1) DebtToIncomeRatio_ComputedCorrectly
    // ================================================================
    [Fact]
    public void DebtToIncomeRatio_ComputedCorrectly()
    {
        var cashState = new CashLedgerState();
        var accountId = Guid.NewGuid();
        cashState.Accounts[accountId] = new AccountState
        {
            AccountId = accountId,
            Name = "Main",
            AccountType = "Cash",
            CurrencyCode = "EGP",
            Balance = 20000m,
            CreatedDate = new DateOnly(2025, 1, 1)
        };

        // Income: 15000 in Feb, 15000 in Mar, 15000 in Apr = 45000 over 3 months = 15000/month
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 2, 1),
            Direction = "In",
            Amount = 15000m,
            Category = "Salary",
            AccountId = accountId
        });
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 3, 1),
            Direction = "In",
            Amount = 15000m,
            Category = "Salary",
            AccountId = accountId
        });
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 4, 1),
            Direction = "In",
            Amount = 15000m,
            Category = "Salary",
            AccountId = accountId
        });

        // Debt payments: 3000 in Feb, 3000 in Mar = 6000 over 3 months = 2000/month
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 2, 10),
            Direction = "Out",
            Amount = 3000m,
            Category = "Loan Payment",
            AccountId = accountId
        });
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 3, 10),
            Direction = "Out",
            Amount = 3000m,
            Category = "Bill Payment",
            AccountId = accountId
        });

        var billingState = new BillingState();
        var budgetState = new BudgetState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        var calculator = new FinancialHealthCalculator();
        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Debt-to-income = 2000 / 15000 = 0.133...
        var debtComponent = healthScore.Components.First(c => c.Name == "Debt-to-Income Ratio");
        Assert.Equal(0.13m, debtComponent.Value, 2);
        Assert.True(healthScore.Score >= 80);
    }

    // ================================================================
    // 2) SavingsRate_ComputedCorrectly
    // ================================================================
    [Fact]
    public void SavingsRate_ComputedCorrectly()
    {
        var cashState = new CashLedgerState();
        var accountId = Guid.NewGuid();
        cashState.Accounts[accountId] = new AccountState
        {
            AccountId = accountId,
            Name = "Main",
            Balance = 10000m,
            CreatedDate = new DateOnly(2025, 1, 1)
        };

        // Income: 10000 over 3 months
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 2, 1),
            Direction = "In",
            Amount = 10000m,
            AccountId = accountId
        });

        // Expenses: 7000 over 3 months
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 2, 5),
            Direction = "Out",
            Amount = 7000m,
            Category = "Living",
            AccountId = accountId
        });

        var billingState = new BillingState();
        var budgetState = new BudgetState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        var calculator = new FinancialHealthCalculator();
        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Savings rate = (10000 - 7000) / 10000 = 0.30
        var savingsComponent = healthScore.Components.First(c => c.Name == "Savings Rate");
        Assert.Equal(0.30m, savingsComponent.Value);
        Assert.Equal("Excellent", savingsComponent.Status);
    }

    // ================================================================
    // 3) LiquidityRatio_ComputedCorrectly
    // ================================================================
    [Fact]
    public void LiquidityRatio_ComputedCorrectly()
    {
        var cashState = new CashLedgerState();
        var accountId = Guid.NewGuid();
        cashState.Accounts[accountId] = new AccountState
        {
            AccountId = accountId,
            Name = "Cash",
            Balance = 18000m,
            CreatedDate = new DateOnly(2025, 1, 1)
        };

        // Expenses: 2000/month over 3 months = 6000 total
        for (int i = 0; i < 3; i++)
        {
            cashState.Rows.Add(new CashLedgerRow
            {
                EventId = Guid.NewGuid(),
                EffectiveDate = new DateOnly(2025, 2 + i, 5),
                Direction = "Out",
                Amount = 2000m,
                Category = "Living",
                AccountId = accountId
            });
        }

        var billingState = new BillingState();
        var budgetState = new BudgetState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        var calculator = new FinancialHealthCalculator();
        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Liquidity ratio = 18000 / 2000 = 9 months
        var liquidityComponent = healthScore.Components.First(c => c.Name == "Liquidity Ratio");
        Assert.Equal(9m, liquidityComponent.Value);
        Assert.Equal("Excellent", liquidityComponent.Status);
    }

    // ================================================================
    // 4) EmergencyFundMonths_ComputedCorrectly
    // ================================================================
    [Fact]
    public void EmergencyFundMonths_ComputedCorrectly()
    {
        var cashState = new CashLedgerState();
        var accountId = Guid.NewGuid();
        cashState.Accounts[accountId] = new AccountState
        {
            AccountId = accountId,
            Name = "Emergency",
            Balance = 30000m,
            CreatedDate = new DateOnly(2025, 1, 1)
        };

        // Monthly expenses: 5000 over 3 months
        for (int i = 0; i < 3; i++)
        {
            cashState.Rows.Add(new CashLedgerRow
            {
                EventId = Guid.NewGuid(),
                EffectiveDate = new DateOnly(2025, 2 + i, 10),
                Direction = "Out",
                Amount = 5000m,
                Category = "Living",
                AccountId = accountId
            });
        }

        var billingState = new BillingState();
        var budgetState = new BudgetState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        var calculator = new FinancialHealthCalculator();
        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Emergency fund = 30000 / 5000 = 6 months
        var emergencyComponent = healthScore.Components.First(c => c.Name == "Emergency Fund Months");
        Assert.Equal(6.0m, emergencyComponent.Value);
        Assert.Equal("Excellent", emergencyComponent.Status);
    }

    // ================================================================
    // 5) BudgetAdherence_ComputedCorrectly
    // ================================================================
    [Fact]
    public void BudgetAdherence_ComputedCorrectly()
    {
        var cashState = new CashLedgerState();
        var accountId = Guid.NewGuid();
        cashState.Accounts[accountId] = new AccountState
        {
            AccountId = accountId,
            Name = "Main",
            Balance = 10000m,
            CreatedDate = new DateOnly(2025, 1, 1)
        };

        // Spending: 800 in category Food (within 1000 budget)
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 4, 10),
            Direction = "Out",
            Amount = 800m,
            Category = "Food",
            AccountId = accountId
        });

        // Budget: Food category, limit 1000 for April 2025
        var budgetState = new BudgetState();
        var catId = Guid.NewGuid();
        budgetState.Budgets[Guid.NewGuid()] = new BudgetItem
        {
            BudgetId = Guid.NewGuid(),
            PeriodYear = 2025,
            PeriodMonth = 4,
            CurrencyCode = "EGP",
            ScopeType = "category",
            CategoryId = catId,
            LimitAmount = 1000m,
            CarryPolicy = "None",
            IsArchived = false
        };

        var billingState = new BillingState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        var calculator = new FinancialHealthCalculator();
        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Budget adherence = 100% (1 budget, within limit)
        var budgetComponent = healthScore.Components.First(c => c.Name == "Budget Adherence");
        Assert.Equal(100m, budgetComponent.Value);
        Assert.Equal("Excellent", budgetComponent.Status);
    }

    // ================================================================
    // 6) OverdueRatio_ComputedCorrectly
    // ================================================================
    [Fact]
    public void OverdueRatio_ComputedCorrectly()
    {
        var cashState = new CashLedgerState();
        var billingState = new BillingState();

        // 3 bills: 1 overdue, 2 current
        var bill1Id = Guid.NewGuid();
        billingState.Bills[bill1Id] = new BillRecord
        {
            BillId = bill1Id,
            PartyId = Guid.NewGuid(),
            Amount = 500m,
            CurrencyCode = "EGP",
            DueDate = new DateOnly(2025, 4, 1),
            Status = "Due",
            Category = "Utilities",
            Reference = "Bill1"
        };

        var bill2Id = Guid.NewGuid();
        billingState.Bills[bill2Id] = new BillRecord
        {
            BillId = bill2Id,
            PartyId = Guid.NewGuid(),
            Amount = 1000m,
            CurrencyCode = "EGP",
            DueDate = new DateOnly(2025, 5, 10),
            Status = "Due",
            Category = "Rent",
            Reference = "Bill2"
        };

        var bill3Id = Guid.NewGuid();
        billingState.Bills[bill3Id] = new BillRecord
        {
            BillId = bill3Id,
            PartyId = Guid.NewGuid(),
            Amount = 200m,
            CurrencyCode = "EGP",
            DueDate = new DateOnly(2025, 5, 20),
            Status = "Due",
            Category = "Phone",
            Reference = "Bill3"
        };

        var budgetState = new BudgetState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        var calculator = new FinancialHealthCalculator();
        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 5, 5), evaluationMonths: 3);

        // Overdue ratio = 1 overdue / 3 total = 0.33
        var overdueComponent = healthScore.Components.First(c => c.Name == "Overdue Ratio");
        Assert.Equal(0.33m, overdueComponent.Value);
        Assert.Equal("Critical", overdueComponent.Status);
    }

    // ================================================================
    // 7) CompositeScore_WeightedCorrectly
    // ================================================================
    [Fact]
    public void CompositeScore_WeightedCorrectly()
    {
        var cashState = new CashLedgerState();
        var budgetState = new BudgetState();
        var billingState = new BillingState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        // Empty state => all components should be at defaults
        var calculator = new FinancialHealthCalculator();
        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Verify weights sum to 1.0
        var totalWeight = healthScore.Components.Sum(c => c.Weight);
        Assert.Equal(1.0m, totalWeight);

        // Verify all 6 components present
        Assert.Equal(6, healthScore.Components.Count);
        Assert.Contains(healthScore.Components, c => c.Name == "Debt-to-Income Ratio");
        Assert.Contains(healthScore.Components, c => c.Name == "Savings Rate");
        Assert.Contains(healthScore.Components, c => c.Name == "Liquidity Ratio");
        Assert.Contains(healthScore.Components, c => c.Name == "Emergency Fund Months");
        Assert.Contains(healthScore.Components, c => c.Name == "Budget Adherence");
        Assert.Contains(healthScore.Components, c => c.Name == "Overdue Ratio");

        // Empty state: debt=0 (100), savings=0 (40), liquidity=0 (0), emergency=0 (0), budget=100 (100), overdue=0 (100)
        // Weighted: 0.25*100 + 0.20*40 + 0.20*0 + 0.15*0 + 0.10*100 + 0.10*100
        //         = 25 + 8 + 0 + 0 + 10 + 10 = 53
        Assert.InRange(healthScore.Score, 50, 65);
        Assert.True(healthScore.Grade is "D" or "C");
    }

    // ================================================================
    // 8) GradeBoundaries_Correct
    // ================================================================
    [Fact]
    public void GradeBoundaries_Correct()
    {
        var cashState = new CashLedgerState();
        var budgetState = new BudgetState();
        var billingState = new BillingState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();
        var calculator = new FinancialHealthCalculator();

        // Test grade boundaries by simulating ideal scenario
        var accountId = Guid.NewGuid();
        cashState.Accounts[accountId] = new AccountState
        {
            AccountId = accountId,
            Name = "Perfect",
            Balance = 100000m,
            CreatedDate = new DateOnly(2025, 1, 1)
        };

        // Ideal: High income, low expenses, no debt, no overdue
        for (int i = 0; i < 3; i++)
        {
            cashState.Rows.Add(new CashLedgerRow
            {
                EventId = Guid.NewGuid(),
                EffectiveDate = new DateOnly(2025, 2 + i, 1),
                Direction = "In",
                Amount = 20000m,
                Category = "Salary",
                AccountId = accountId
            });
            cashState.Rows.Add(new CashLedgerRow
            {
                EventId = Guid.NewGuid(),
                EffectiveDate = new DateOnly(2025, 2 + i, 10),
                Direction = "Out",
                Amount = 5000m,
                Category = "Living",
                AccountId = accountId
            });
        }

        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Should be A grade (90+)
        Assert.True(healthScore.Score >= 90);
        Assert.Equal("A", healthScore.Grade);
    }

    // ================================================================
    // 9) ZeroIncome_HandledSafely
    // ================================================================
    [Fact]
    public void ZeroIncome_HandledSafely()
    {
        var cashState = new CashLedgerState();
        var accountId = Guid.NewGuid();
        cashState.Accounts[accountId] = new AccountState
        {
            AccountId = accountId,
            Name = "Main",
            Balance = 5000m,
            CreatedDate = new DateOnly(2025, 1, 1)
        };

        // No income, only expenses
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 2, 5),
            Direction = "Out",
            Amount = 1000m,
            Category = "Living",
            AccountId = accountId
        });

        var billingState = new BillingState();
        var budgetState = new BudgetState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        var calculator = new FinancialHealthCalculator();
        var healthScore = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Should not throw, should return valid score
        Assert.InRange(healthScore.Score, 0, 100);
        Assert.NotNull(healthScore.Grade);
        Assert.Equal(6, healthScore.Components.Count);

        // Debt-to-income and savings rate should be 0 (no income)
        var debtComponent = healthScore.Components.First(c => c.Name == "Debt-to-Income Ratio");
        var savingsComponent = healthScore.Components.First(c => c.Name == "Savings Rate");
        Assert.Equal(0m, debtComponent.Value);
        Assert.Equal(0m, savingsComponent.Value);
    }

    // ================================================================
    // 10) Determinism_SameInputSameScore
    // ================================================================
    [Fact]
    public void Determinism_SameInputSameScore()
    {
        var cashState = new CashLedgerState();
        var accountId = Guid.NewGuid();
        cashState.Accounts[accountId] = new AccountState
        {
            AccountId = accountId,
            Name = "Main",
            Balance = 15000m,
            CreatedDate = new DateOnly(2025, 1, 1)
        };

        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 2, 1),
            Direction = "In",
            Amount = 10000m,
            Category = "Salary",
            AccountId = accountId
        });
        cashState.Rows.Add(new CashLedgerRow
        {
            EventId = Guid.NewGuid(),
            EffectiveDate = new DateOnly(2025, 2, 10),
            Direction = "Out",
            Amount = 3000m,
            Category = "Rent",
            AccountId = accountId
        });

        var billingState = new BillingState();
        var budgetState = new BudgetState();
        var goalsState = new GoalsState();
        var incomeSourceState = new IncomeSourceState();

        var calculator = new FinancialHealthCalculator();
        var asOfDate = new DateOnly(2025, 4, 30);

        var score1 = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, asOfDate, evaluationMonths: 3);
        var score2 = calculator.Compute(cashState, budgetState, goalsState, billingState,
            incomeSourceState, asOfDate, evaluationMonths: 3);

        Assert.Equal(score1.Score, score2.Score);
        Assert.Equal(score1.Grade, score2.Grade);
        Assert.Equal(score1.Components.Count, score2.Components.Count);

        for (int i = 0; i < score1.Components.Count; i++)
        {
            Assert.Equal(score1.Components[i].Name, score2.Components[i].Name);
            Assert.Equal(score1.Components[i].Value, score2.Components[i].Value);
            Assert.Equal(score1.Components[i].Weight, score2.Components[i].Weight);
            Assert.Equal(score1.Components[i].Status, score2.Components[i].Status);
        }
    }
}
