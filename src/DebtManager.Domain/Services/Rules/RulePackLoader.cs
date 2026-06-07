using System.Text.Json;
using DebtManager.Domain.Rules;

namespace DebtManager.Domain.Services.Rules;

/// <summary>
/// Loads and parses rule packs from JSON format into domain models.
/// Supports the full DSL including grace periods, interest accrual, and complex predicates.
/// </summary>
public sealed class RulePackLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Load a complete rule pack from JSON.
    /// </summary>
    public RulePack Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var packId = GetRequiredString(root, "pack_id");
        var displayName = GetString(root, "display_name") ?? GetString(root, "name") ?? packId;
        var countryCode = GetString(root, "country_code") ?? "EG";
        var currencyCode = GetString(root, "currency_code") ?? "EGP";

        var versions = new List<RulePackVersion>();

        // Single version format (most common)
        if (root.TryGetProperty("version", out _) || root.TryGetProperty("rules", out _))
        {
            versions.Add(LoadVersion(root));
        }
        // Multiple versions format
        else if (root.TryGetProperty("versions", out var versionsEl))
        {
            foreach (var versionEl in versionsEl.EnumerateArray())
            {
                versions.Add(LoadVersion(versionEl));
            }
        }

        return new RulePack(
            PackId: packId,
            DisplayName: displayName,
            CountryCode: countryCode,
            CurrencyCode: currencyCode,
            Versions: versions.AsReadOnly()
        );
    }

    /// <summary>
    /// Load a single rule pack version from JSON.
    /// </summary>
    public RulePackVersion LoadVersion(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return LoadVersion(doc.RootElement);
    }

    private RulePackVersion LoadVersion(JsonElement el)
    {
        var versionId = el.TryGetProperty("version_id", out var vidEl)
            ? Guid.Parse(vidEl.GetString()!)
            : Guid.NewGuid();

        var versionLabel = GetString(el, "version") ?? GetString(el, "version_label") ?? "1.0";

        var effectiveFrom = GetDateOnly(el, "effective_from") ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveTo = GetDateOnlyNullable(el, "effective_to");
        var status = GetString(el, "status") ?? "active";

        var rules = new List<RuleDefinition>();

        if (el.TryGetProperty("rules", out var rulesEl))
        {
            foreach (var ruleEl in rulesEl.EnumerateArray())
            {
                var rule = LoadRule(ruleEl);
                if (rule != null)
                {
                    rules.Add(rule);
                }
            }
        }

        return new RulePackVersion(
            VersionId: versionId,
            VersionLabel: versionLabel,
            EffectiveFrom: effectiveFrom,
            EffectiveTo: effectiveTo,
            Status: status,
            Rules: rules.AsReadOnly()
        );
    }

    private RuleDefinition? LoadRule(JsonElement el)
    {
        var ruleKey = GetString(el, "id") ?? GetString(el, "rule_key") ?? Guid.NewGuid().ToString();
        var description = GetString(el, "description") ?? GetString(el, "type") ?? "";

        var enabled = !el.TryGetProperty("enabled", out var enabledEl) || enabledEl.GetBoolean();
        if (!enabled) return null;

        // Parse "when" conditions
        var when = LoadWhen(el);

        // Parse "effect" / "then" actions
        var then = LoadThen(el);

        return new RuleDefinition(
            RuleKey: ruleKey,
            Description: description,
            When: when,
            Then: then.AsReadOnly()
        );
    }

    private RuleWhen LoadWhen(JsonElement el)
    {
        var predicates = new List<RulePredicate>();
        var op = "and";

        if (el.TryGetProperty("when", out var whenEl))
        {
            // Check for "all" (AND) or "any" (OR) arrays
            if (whenEl.TryGetProperty("all", out var allEl))
            {
                op = "and";
                foreach (var condEl in allEl.EnumerateArray())
                {
                    predicates.Add(LoadPredicate(condEl));
                }
            }
            else if (whenEl.TryGetProperty("any", out var anyEl))
            {
                op = "or";
                foreach (var condEl in anyEl.EnumerateArray())
                {
                    predicates.Add(LoadPredicate(condEl));
                }
            }
            else if (whenEl.ValueKind == JsonValueKind.Array)
            {
                // Direct array of conditions (implicit AND)
                foreach (var condEl in whenEl.EnumerateArray())
                {
                    predicates.Add(LoadPredicate(condEl));
                }
            }
            else
            {
                // Single condition object
                predicates.Add(LoadPredicate(whenEl));
            }
        }

        // If no "when" clause, rule always fires
        if (!predicates.Any())
        {
            // Add a trivially true predicate
            predicates.Add(new RulePredicate("_always", "eq", true));
        }

        return new RuleWhen(Op: op, Predicates: predicates.AsReadOnly());
    }

    private RulePredicate LoadPredicate(JsonElement el)
    {
        var field = GetRequiredString(el, "field");
        var opStr = GetString(el, "op") ?? GetString(el, "compare") ?? "eq";

        // Convert DSL operators to internal format
        var compare = opStr.ToLowerInvariant() switch
        {
            ">" or "gt" => "gt",
            ">=" or "gte" => "gte",
            "<" or "lt" => "lt",
            "<=" or "lte" => "lte",
            "==" or "=" or "eq" => "eq",
            "!=" or "<>" or "neq" => "neq",
            _ => opStr.ToLowerInvariant()
        };

        object value = null!;
        if (el.TryGetProperty("value", out var valEl))
        {
            value = valEl.ValueKind switch
            {
                JsonValueKind.Number => valEl.TryGetInt32(out var i) ? i : valEl.GetDecimal(),
                JsonValueKind.String => valEl.GetString()!,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => valEl.GetRawText()
            };
        }

        return new RulePredicate(Fact: field, Compare: compare, Value: value);
    }

    private List<RuleThen> LoadThen(JsonElement el)
    {
        var thens = new List<RuleThen>();

        // Check for "effect" property (new DSL format)
        if (el.TryGetProperty("effect", out var effectEl))
        {
            thens.AddRange(LoadEffects(effectEl, el));
        }
        // Check for "then" property (legacy format)
        else if (el.TryGetProperty("then", out var thenEl))
        {
            if (thenEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var tEl in thenEl.EnumerateArray())
                {
                    thens.AddRange(LoadEffects(tEl, el));
                }
            }
            else
            {
                thens.AddRange(LoadEffects(thenEl, el));
            }
        }

        return thens;
    }

    private IEnumerable<RuleThen> LoadEffects(JsonElement el, JsonElement ruleEl)
    {
        var ruleKey = GetString(ruleEl, "id") ?? GetString(ruleEl, "rule_key") ?? "";

        // Check for specific effect types
        if (el.TryGetProperty("add_charge", out var chargeEl))
        {
            yield return LoadChargeEffect(chargeEl, RuleEffectTypes.AddCharge, ruleKey);
        }

        if (el.TryGetProperty("accrue_interest", out var interestEl))
        {
            yield return LoadInterestEffect(interestEl, ruleKey);
        }

        if (el.TryGetProperty("apply_grace", out var graceEl))
        {
            yield return LoadGraceEffect(graceEl, ruleKey);
        }

        if (el.TryGetProperty("apply_penalty", out var penaltyEl))
        {
            yield return LoadChargeEffect(penaltyEl, RuleEffectTypes.ApplyPenalty, ruleKey);
        }

        if (el.TryGetProperty("apply_fee", out var feeEl))
        {
            yield return LoadChargeEffect(feeEl, RuleEffectTypes.ApplyFee, ruleKey);
        }

        // Direct effect properties (legacy/simple format)
        if (el.TryGetProperty("effect_type", out var etEl))
        {
            var effectType = etEl.GetString()!;
            var data = new Dictionary<string, object> { [RuleEffectFields.RuleKey] = ruleKey };

            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Name != "effect_type")
                {
                    data[prop.Name] = GetValueFromElement(prop.Value);
                }
            }

            yield return new RuleThen(effectType, data);
        }
    }

    private RuleThen LoadChargeEffect(JsonElement el, string effectType, string ruleKey)
    {
        var data = new Dictionary<string, object>
        {
            [RuleEffectFields.RuleKey] = ruleKey
        };

        if (el.TryGetProperty("amount", out var amountEl))
        {
            data[RuleEffectFields.Amount] = amountEl.ValueKind == JsonValueKind.Number
                ? amountEl.GetDecimal()
                : decimal.Parse(amountEl.GetString()!);
        }

        if (el.TryGetProperty("label", out var labelEl))
        {
            data[RuleEffectFields.Label] = labelEl.GetString()!;
        }

        if (el.TryGetProperty("chargeType", out var ctEl) || el.TryGetProperty("charge_type", out ctEl))
        {
            data[RuleEffectFields.ChargeType] = ctEl.GetString()!;
        }

        if (el.TryGetProperty("penaltyType", out var ptEl) || el.TryGetProperty("penalty_type", out ptEl))
        {
            data[RuleEffectFields.PenaltyType] = ptEl.GetString()!;
        }

        if (el.TryGetProperty("maxPenalty", out var maxEl) || el.TryGetProperty("max_penalty", out maxEl))
        {
            data[RuleEffectFields.MaxPenalty] = maxEl.GetDecimal();
        }

        return new RuleThen(effectType, data);
    }

    private RuleThen LoadInterestEffect(JsonElement el, string ruleKey)
    {
        var data = new Dictionary<string, object>
        {
            [RuleEffectFields.RuleKey] = ruleKey
        };

        if (el.TryGetProperty("rate", out var rateEl))
        {
            data[RuleEffectFields.Rate] = rateEl.GetDecimal();
        }

        if (el.TryGetProperty("compounding", out var compEl))
        {
            data[RuleEffectFields.Compounding] = compEl.GetString()!;
        }

        if (el.TryGetProperty("basis", out var basisEl))
        {
            data[RuleEffectFields.Basis] = basisEl.GetString()!;
        }

        if (el.TryGetProperty("label", out var labelEl))
        {
            data[RuleEffectFields.Label] = labelEl.GetString()!;
        }

        return new RuleThen(RuleEffectTypes.AccrueInterest, data);
    }

    private RuleThen LoadGraceEffect(JsonElement el, string ruleKey)
    {
        var data = new Dictionary<string, object>
        {
            [RuleEffectFields.RuleKey] = ruleKey
        };

        if (el.TryGetProperty("days", out var daysEl))
        {
            data[RuleEffectFields.GraceDays] = daysEl.GetInt32();
        }

        if (el.TryGetProperty("type", out var typeEl))
        {
            data[RuleEffectFields.GraceType] = typeEl.GetString()!;
        }

        if (el.TryGetProperty("appliesToPenalties", out var penEl) ||
            el.TryGetProperty("applies_to_penalties", out penEl))
        {
            data[RuleEffectFields.AppliesToPenalties] = penEl.GetBoolean();
        }

        if (el.TryGetProperty("appliesToInterest", out var intEl) ||
            el.TryGetProperty("applies_to_interest", out intEl))
        {
            data[RuleEffectFields.AppliesToInterest] = intEl.GetBoolean();
        }

        return new RuleThen(RuleEffectTypes.ApplyGrace, data);
    }

    private static object GetValueFromElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt32(out var i) ? i : el.GetDecimal(),
            JsonValueKind.String => el.GetString()!,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => el.GetRawText()
        };
    }

    private static string GetRequiredString(JsonElement el, string propName)
    {
        if (!el.TryGetProperty(propName, out var prop))
            throw new InvalidOperationException($"Required property '{propName}' not found.");
        return prop.GetString() ?? throw new InvalidOperationException($"Property '{propName}' is null.");
    }

    private static string? GetString(JsonElement el, string propName)
    {
        return el.TryGetProperty(propName, out var prop) ? prop.GetString() : null;
    }

    private static DateOnly? GetDateOnly(JsonElement el, string propName)
    {
        if (el.TryGetProperty(propName, out var prop) && prop.ValueKind != JsonValueKind.Null)
        {
            var str = prop.GetString();
            if (!string.IsNullOrEmpty(str) && DateOnly.TryParse(str, out var date))
            {
                return date;
            }
        }
        return null;
    }

    private static DateOnly? GetDateOnlyNullable(JsonElement el, string propName)
    {
        return GetDateOnly(el, propName);
    }
}
