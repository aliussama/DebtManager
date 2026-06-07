using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services;

/// <summary>
/// Service to expand multiple schedule specifications into a unified installment list.
/// Handles schedule modifications and produces a deterministic sequence.
/// </summary>
public sealed class ScheduleExpander
{
    /// <summary>
    /// Expand all schedules for an obligation, applying any modifications.
    /// </summary>
    public IReadOnlyList<Installment> ExpandAll(
        IEnumerable<IScheduleSpec> schedules,
        IEnumerable<ScheduleModificationRecord> modifications,
        DateOnly from,
        DateOnly to)
    {
        var allInstallments = new List<Installment>();
        var modificationLookup = modifications
            .GroupBy(m => m.OriginalScheduleId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.EffectiveDate).ToList());

        foreach (var schedule in schedules)
        {
            var effectiveSchedule = ApplyModifications(schedule, modificationLookup, from, to);
            var expanded = effectiveSchedule.Expand(from, to);
            allInstallments.AddRange(expanded);
        }

        // Sort by due date, then by installment key for determinism
        return allInstallments
            .OrderBy(i => i.DueDate)
            .ThenBy(i => i.InstallmentKey)
            .ToList();
    }

    private IScheduleSpec ApplyModifications(
        IScheduleSpec original,
        Dictionary<Guid, List<ScheduleModificationRecord>> modificationLookup,
        DateOnly from,
        DateOnly to)
    {
        if (!modificationLookup.TryGetValue(original.ScheduleId, out var mods) || !mods.Any())
        {
            return original;
        }

        // Find the most recent modification effective before or on 'to'
        var applicableMod = mods
            .Where(m => m.EffectiveDate <= to)
            .OrderByDescending(m => m.EffectiveDate)
            .FirstOrDefault();

        if (applicableMod == null)
        {
            return original;
        }

        // Apply modification based on type
        return applicableMod.ModificationType switch
        {
            ScheduleModificationType.Deferral => ApplyDeferral(original, applicableMod),
            ScheduleModificationType.AmountAdjustment => ApplyAmountAdjustment(original, applicableMod),
            ScheduleModificationType.TermChange => ApplyTermChange(original, applicableMod),
            _ => original // Other modifications may need custom handling
        };
    }

    private IScheduleSpec ApplyDeferral(IScheduleSpec original, ScheduleModificationRecord mod)
    {
        if (original is RecurringScheduleSpec recurring && mod.DaysDeferred.HasValue)
        {
            return new RecurringScheduleSpec(
                scheduleId: mod.NewScheduleId,
                obligationId: recurring.ObligationId,
                pattern: recurring.Pattern,
                dayOfMonth: recurring.DayOfMonth,
                startDate: recurring.StartDate.AddDays(mod.DaysDeferred.Value),
                endDate: recurring.EndDate?.AddDays(mod.DaysDeferred.Value),
                maxOccurrences: recurring.MaxOccurrences,
                installmentAmount: recurring.InstallmentAmount,
                weekendAdjustment: recurring.WeekendAdjustment
            );
        }

        return original;
    }

    private IScheduleSpec ApplyAmountAdjustment(IScheduleSpec original, ScheduleModificationRecord mod)
    {
        if (original is RecurringScheduleSpec recurring && mod.NewInstallmentAmount.HasValue)
        {
            return new RecurringScheduleSpec(
                scheduleId: mod.NewScheduleId,
                obligationId: recurring.ObligationId,
                pattern: recurring.Pattern,
                dayOfMonth: recurring.DayOfMonth,
                startDate: mod.EffectiveDate > recurring.StartDate ? mod.EffectiveDate : recurring.StartDate,
                endDate: recurring.EndDate,
                maxOccurrences: recurring.MaxOccurrences,
                installmentAmount: mod.NewInstallmentAmount.Value,
                weekendAdjustment: recurring.WeekendAdjustment
            );
        }

        return original;
    }

    private IScheduleSpec ApplyTermChange(IScheduleSpec original, ScheduleModificationRecord mod)
    {
        if (original is AmortizationScheduleSpec amort && mod.NewTermMonths.HasValue)
        {
            return new AmortizationScheduleSpec(
                scheduleId: mod.NewScheduleId,
                obligationId: amort.ObligationId,
                principal: amort.Principal,
                annualInterestRate: mod.NewInterestRate ?? amort.AnnualInterestRate,
                termInMonths: mod.NewTermMonths.Value,
                firstPaymentDate: amort.FirstPaymentDate,
                dayOfMonth: amort.DayOfMonth,
                amortizationType: amort.AmortizationType,
                weekendAdjustment: amort.WeekendAdjustment
            );
        }

        return original;
    }
}

/// <summary>
/// Record of a schedule modification for the expander.
/// </summary>
public sealed record ScheduleModificationRecord(
    Guid OriginalScheduleId,
    Guid NewScheduleId,
    ScheduleModificationType EffectiveModificationType,
    DateOnly EffectiveDate,
    int? DaysDeferred,
    int? NewTermMonths,
    Money? NewInstallmentAmount,
    decimal? NewInterestRate
)
{
    public ScheduleModificationType ModificationType => EffectiveModificationType;
}