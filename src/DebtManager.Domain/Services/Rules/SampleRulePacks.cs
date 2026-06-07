namespace DebtManager.Domain.Services.Rules;

/// <summary>
/// Sample rule packs in JSON format for testing and documentation.
/// These demonstrate the DSL capabilities.
/// </summary>
public static class SampleRulePacks
{
    /// <summary>
    /// Basic loan rule pack with grace period and late penalty.
    /// </summary>
    public const string BasicLoan = """
{
  "pack_id": "basic_loan",
  "display_name": "Basic Loan Rules",
  "country_code": "EG",
  "currency_code": "EGP",
  "version": "1.0",
  "effective_from": "2025-01-01",
  "status": "active",
  "rules": [
    {
      "id": "grace_period_7_days",
      "type": "grace",
      "description": "7-day grace period before penalties apply",
      "when": {
        "all": [
          { "field": "installment.is_overdue", "op": "eq", "value": true }
        ]
      },
      "effect": {
        "apply_grace": {
          "days": 7,
          "type": "calendar",
          "appliesToPenalties": true,
          "appliesToInterest": false
        }
      }
    },
    {
      "id": "late_penalty_50egp",
      "type": "penalty",
      "description": "Fixed 50 EGP late payment penalty after grace period",
      "when": {
        "all": [
          { "field": "installment.days_overdue", "op": "gt", "value": 7 }
        ]
      },
      "effect": {
        "apply_penalty": {
          "amount": 50,
          "label": "Late Payment Penalty",
          "chargeType": "penalty"
        }
      }
    }
  ]
}
""";

    /// <summary>
    /// Credit card rule pack with interest accrual and tiered penalties.
    /// </summary>
    public const string CreditCard = """
{
  "pack_id": "credit_card_standard",
  "display_name": "Standard Credit Card Rules",
  "country_code": "EG",
  "currency_code": "EGP",
  "version": "2025-01",
  "effective_from": "2025-01-01",
  "status": "active",
  "rules": [
    {
      "id": "interest_accrual_24pct",
      "type": "interest",
      "description": "24% annual interest on outstanding balance",
      "when": {
        "all": [
          { "field": "outstanding.amount", "op": "gt", "value": 0 }
        ]
      },
      "effect": {
        "accrue_interest": {
          "rate": 0.24,
          "compounding": "daily",
          "basis": "actual365",
          "label": "Credit Card Interest"
        }
      }
    },
    {
      "id": "grace_period_3_days",
      "type": "grace",
      "description": "3-day grace period for minimum payment",
      "when": {
        "all": [
          { "field": "installment.is_overdue", "op": "eq", "value": true }
        ]
      },
      "effect": {
        "apply_grace": {
          "days": 3,
          "type": "calendar",
          "appliesToPenalties": true,
          "appliesToInterest": false
        }
      }
    },
    {
      "id": "late_fee_percentage",
      "type": "penalty",
      "description": "5% late fee on minimum payment (max 500 EGP)",
      "when": {
        "all": [
          { "field": "installment.days_overdue", "op": "gt", "value": 3 }
        ]
      },
      "effect": {
        "apply_penalty": {
          "amount": 5,
          "penaltyType": "percentage",
          "maxPenalty": 500,
          "label": "Late Payment Fee"
        }
      }
    }
  ]
}
""";

    /// <summary>
    /// Mortgage rule pack with variable rate interest.
    /// </summary>
    public const string Mortgage = """
{
  "pack_id": "mortgage_standard",
  "display_name": "Standard Mortgage Rules",
  "country_code": "EG",
  "currency_code": "EGP",
  "version": "2025-01",
  "effective_from": "2025-01-01",
  "status": "active",
  "rules": [
    {
      "id": "grace_period_15_days",
      "type": "grace",
      "description": "15-day grace period for mortgage payments",
      "when": {
        "all": [
          { "field": "installment.is_overdue", "op": "eq", "value": true }
        ]
      },
      "effect": {
        "apply_grace": {
          "days": 15,
          "type": "calendar",
          "appliesToPenalties": true,
          "appliesToInterest": true
        }
      }
    },
    {
      "id": "late_penalty_2pct",
      "type": "penalty",
      "description": "2% late penalty on overdue amount",
      "when": {
        "all": [
          { "field": "installment.days_overdue", "op": "gt", "value": 15 }
        ]
      },
      "effect": {
        "apply_penalty": {
          "amount": 2,
          "penaltyType": "percentage",
          "label": "Mortgage Late Penalty"
        }
      }
    },
    {
      "id": "interest_accrual_variable",
      "type": "interest",
      "description": "Variable rate interest on overdue balance",
      "when": {
        "all": [
          { "field": "installment.days_overdue", "op": "gt", "value": 15 },
          { "field": "outstanding.amount", "op": "gt", "value": 0 }
        ]
      },
      "effect": {
        "accrue_interest": {
          "rate": 0.18,
          "compounding": "monthly",
          "basis": "actual365",
          "label": "Overdue Interest"
        }
      }
    }
  ]
}
""";

    /// <summary>
    /// University tuition rule pack with semester-based logic.
    /// </summary>
    public const string UniversityTuition = """
{
  "pack_id": "university_tuition",
  "display_name": "University Tuition Rules",
  "country_code": "EG",
  "currency_code": "EGP",
  "version": "2025-01",
  "effective_from": "2025-01-01",
  "status": "active",
  "rules": [
    {
      "id": "registration_hold",
      "type": "warning",
      "description": "Registration hold if overdue more than 30 days",
      "when": {
        "all": [
          { "field": "installment.days_overdue", "op": "gt", "value": 30 }
        ]
      },
      "effect": {
        "effect_type": "warning",
        "code": "REGISTRATION_HOLD",
        "message": "Registration hold - payment required to register for next semester"
      }
    },
    {
      "id": "late_fee_fixed",
      "type": "penalty",
      "description": "Fixed 200 EGP late registration fee",
      "when": {
        "all": [
          { "field": "installment.days_overdue", "op": "gt", "value": 7 }
        ]
      },
      "effect": {
        "apply_penalty": {
          "amount": 200,
          "label": "Late Registration Fee",
          "chargeType": "fee"
        }
      }
    },
    {
      "id": "grace_period_7_days",
      "type": "grace",
      "description": "7-day grace after tuition due date",
      "when": {
        "all": [
          { "field": "installment.is_overdue", "op": "eq", "value": true }
        ]
      },
      "effect": {
        "apply_grace": {
          "days": 7,
          "type": "calendar",
          "appliesToPenalties": true
        }
      }
    }
  ]
}
""";

    /// <summary>
    /// All sample rule packs as a dictionary.
    /// </summary>
    public static IReadOnlyDictionary<string, string> All => new Dictionary<string, string>
    {
        ["basic_loan"] = BasicLoan,
        ["credit_card_standard"] = CreditCard,
        ["mortgage_standard"] = Mortgage,
        ["university_tuition"] = UniversityTuition
    };
}
