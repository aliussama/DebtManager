using System.Text.Json;
using DebtManager.Domain.Billing;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class BillAdjustmentRecord
{
    public Guid AdjustmentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public bool IsReversed { get; set; }
}

public sealed class BillPaymentRecord
{
    public Guid PaymentId { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly PaidDate { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
    public bool IsReversed { get; set; }
    public bool IsUnapplied { get; set; }
}

public sealed class BillRecord
{
    public Guid BillId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid PartyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string Status { get; set; } = "Due";
    public DateOnly LastStatusDate { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsDisputed { get; set; }
    public bool IsWrittenOff { get; set; }
    public List<BillAdjustmentRecord> Adjustments { get; set; } = new();
    public List<BillPaymentRecord> Payments { get; set; } = new();

    public decimal TotalAdjustments => Adjustments.Where(a => !a.IsReversed).Sum(a =>
        a.Kind is "Discount" or "Credit" ? -a.Amount : a.Amount);

    public decimal TotalPaid => Payments.Where(p => !p.IsReversed && !p.IsUnapplied).Sum(p => p.Amount);

    public decimal EffectiveTotal => Amount + TotalAdjustments;

    public decimal Outstanding => Math.Max(0, EffectiveTotal - TotalPaid);
}

public sealed class InvoiceAdjustmentRecord
{
    public Guid AdjustmentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
    public bool IsReversed { get; set; }
}

public sealed class InvoicePaymentRecord
{
    public Guid PaymentId { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateOnly PaidDate { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? ExternalReference { get; set; }
    public bool IsReversed { get; set; }
    public bool IsUnapplied { get; set; }
}

public sealed class InvoiceRecord
{
    public Guid InvoiceId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid PartyId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string Status { get; set; } = "Due";
    public DateOnly LastStatusDate { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsDisputed { get; set; }
    public bool IsWrittenOff { get; set; }
    public List<InvoiceAdjustmentRecord> Adjustments { get; set; } = new();
    public List<InvoicePaymentRecord> Payments { get; set; } = new();

    public decimal TotalAdjustments => Adjustments.Where(a => !a.IsReversed).Sum(a =>
        a.Kind is "Discount" or "Credit" ? -a.Amount : a.Amount);

    public decimal TotalPaid => Payments.Where(p => !p.IsReversed && !p.IsUnapplied).Sum(p => p.Amount);

    public decimal EffectiveTotal => Amount + TotalAdjustments;

    public decimal Outstanding => Math.Max(0, EffectiveTotal - TotalPaid);
}

public sealed class BillingState
{
    public Dictionary<Guid, BillRecord> Bills { get; } = new();
    public Dictionary<Guid, InvoiceRecord> Invoices { get; } = new();

    /// <summary>
    /// Set of CycleKeys already generated per contract (for idempotency).
    /// Key = ContractId, Value = set of CycleKeys.
    /// </summary>
    public Dictionary<Guid, HashSet<string>> GeneratedCycleKeys { get; } = new();

    public IReadOnlyList<BillRecord> BillsByParty(Guid partyId)
        => Bills.Values.Where(b => b.PartyId == partyId).ToList();

    public IReadOnlyList<InvoiceRecord> InvoicesByParty(Guid partyId)
        => Invoices.Values.Where(i => i.PartyId == partyId).ToList();

    public IReadOnlyList<BillRecord> BillsByContract(Guid contractId)
        => Bills.Values.Where(b => b.ContractId == contractId).ToList();

    public IReadOnlyList<InvoiceRecord> InvoicesByContract(Guid contractId)
        => Invoices.Values.Where(i => i.ContractId == contractId).ToList();

    public IReadOnlyList<BillRecord> OverdueBills(DateOnly asOfDate)
        => Bills.Values.Where(b => b.Status is "Due" or "PartiallyPaid" && b.DueDate < asOfDate && !b.IsCancelled && !b.IsWrittenOff).ToList();

    public IReadOnlyList<InvoiceRecord> OverdueInvoices(DateOnly asOfDate)
        => Invoices.Values.Where(i => i.Status is "Due" or "PartiallyPaid" && i.DueDate < asOfDate && !i.IsCancelled && !i.IsWrittenOff).ToList();
}

public static class BillingProjector
{
    public static BillingState Project(IEnumerable<EventEnvelope> envelopes, DateOnly? asOfDate = null)
    {
        var state = new BillingState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            if (asOfDate.HasValue && env.EffectiveDate > asOfDate.Value)
                continue;

            switch (env.EventType)
            {
                // --- Bills ---
                case nameof(BillIssued):
                {
                    var ev = JsonSerializer.Deserialize<BillIssued>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Bills[ev.BillId] = new BillRecord
                    {
                        BillId = ev.BillId,
                        ContractId = ev.ContractId,
                        PartyId = ev.PartyId,
                        CurrencyCode = ev.CurrencyCode,
                        Amount = ev.Amount,
                        DueDate = ev.DueDate,
                        Category = ev.Category,
                        Reference = ev.Reference,
                        Notes = ev.Notes,
                        Status = "Due",
                        LastStatusDate = ev.EffectiveDate
                    };
                    break;
                }
                case nameof(BillCancelled):
                {
                    var ev = JsonSerializer.Deserialize<BillCancelled>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Bills.TryGetValue(ev.BillId, out var bill))
                    {
                        bill.IsCancelled = true;
                        bill.Status = "Cancelled";
                        bill.LastStatusDate = ev.EffectiveDate;
                    }
                    break;
                }
                case nameof(BillDisputed):
                {
                    var ev = JsonSerializer.Deserialize<BillDisputed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Bills.TryGetValue(ev.BillId, out var bill))
                    {
                        bill.IsDisputed = true;
                        bill.Status = "Disputed";
                        bill.LastStatusDate = ev.EffectiveDate;
                    }
                    break;
                }
                case nameof(BillWrittenOff):
                {
                    var ev = JsonSerializer.Deserialize<BillWrittenOff>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Bills.TryGetValue(ev.BillId, out var bill))
                    {
                        bill.IsWrittenOff = true;
                        bill.Status = "WrittenOff";
                        bill.LastStatusDate = ev.EffectiveDate;
                    }
                    break;
                }

                // --- Bill Adjustments ---
                case nameof(BillAdjustmentAdded):
                {
                    var ev = JsonSerializer.Deserialize<BillAdjustmentAdded>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Bills.TryGetValue(ev.BillId, out var bill))
                    {
                        bill.Adjustments.Add(new BillAdjustmentRecord
                        {
                            AdjustmentId = ev.AdjustmentId,
                            Kind = ev.Kind,
                            Amount = ev.Amount,
                            Notes = ev.Notes
                        });
                        RecomputeBillStatus(bill, asOfDate);
                    }
                    break;
                }
                case nameof(BillAdjustmentReversed):
                {
                    var ev = JsonSerializer.Deserialize<BillAdjustmentReversed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Bills.TryGetValue(ev.BillId, out var bill))
                    {
                        var adj = bill.Adjustments.FirstOrDefault(a => a.AdjustmentId == ev.AdjustmentId);
                        if (adj != null) adj.IsReversed = true;
                        RecomputeBillStatus(bill, asOfDate);
                    }
                    break;
                }

                // --- Bill Payments ---
                case nameof(BillPaymentRecorded):
                {
                    var ev = JsonSerializer.Deserialize<BillPaymentRecorded>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Bills.TryGetValue(ev.BillId, out var bill))
                    {
                        bill.Payments.Add(new BillPaymentRecord
                        {
                            PaymentId = ev.PaymentId,
                            AccountId = ev.AccountId,
                            Amount = ev.Amount,
                            CurrencyCode = ev.CurrencyCode,
                            PaidDate = ev.PaidDate,
                            Method = ev.Method,
                            ExternalReference = ev.ExternalReference
                        });
                        RecomputeBillStatus(bill, asOfDate);
                    }
                    break;
                }
                case nameof(BillPaymentReversed):
                {
                    var ev = JsonSerializer.Deserialize<BillPaymentReversed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Bills.TryGetValue(ev.BillId, out var bill))
                    {
                        var pmt = bill.Payments.FirstOrDefault(p => p.PaymentId == ev.PaymentId);
                        if (pmt != null) pmt.IsReversed = true;
                        RecomputeBillStatus(bill, asOfDate);
                    }
                    break;
                }
                case nameof(BillPaymentUnapplied):
                {
                    var ev = JsonSerializer.Deserialize<BillPaymentUnapplied>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Bills.TryGetValue(ev.BillId, out var bill))
                    {
                        var pmt = bill.Payments.FirstOrDefault(p => p.PaymentId == ev.PaymentId);
                        if (pmt != null) pmt.IsUnapplied = true;
                        RecomputeBillStatus(bill, asOfDate);
                    }
                    break;
                }

                // --- Invoices ---
                case nameof(InvoiceIssued):
                {
                    var ev = JsonSerializer.Deserialize<InvoiceIssued>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Invoices[ev.InvoiceId] = new InvoiceRecord
                    {
                        InvoiceId = ev.InvoiceId,
                        ContractId = ev.ContractId,
                        PartyId = ev.PartyId,
                        CurrencyCode = ev.CurrencyCode,
                        Amount = ev.Amount,
                        DueDate = ev.DueDate,
                        Category = ev.Category,
                        Reference = ev.Reference,
                        Notes = ev.Notes,
                        Status = "Due",
                        LastStatusDate = ev.EffectiveDate
                    };
                    break;
                }
                case nameof(InvoiceCancelled):
                {
                    var ev = JsonSerializer.Deserialize<InvoiceCancelled>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Invoices.TryGetValue(ev.InvoiceId, out var inv))
                    {
                        inv.IsCancelled = true;
                        inv.Status = "Cancelled";
                        inv.LastStatusDate = ev.EffectiveDate;
                    }
                    break;
                }
                case nameof(InvoiceDisputed):
                {
                    var ev = JsonSerializer.Deserialize<InvoiceDisputed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Invoices.TryGetValue(ev.InvoiceId, out var inv))
                    {
                        inv.IsDisputed = true;
                        inv.Status = "Disputed";
                        inv.LastStatusDate = ev.EffectiveDate;
                    }
                    break;
                }
                case nameof(InvoiceWrittenOff):
                {
                    var ev = JsonSerializer.Deserialize<InvoiceWrittenOff>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Invoices.TryGetValue(ev.InvoiceId, out var inv))
                    {
                        inv.IsWrittenOff = true;
                        inv.Status = "WrittenOff";
                        inv.LastStatusDate = ev.EffectiveDate;
                    }
                    break;
                }

                // --- Invoice Adjustments ---
                case nameof(InvoiceAdjustmentAdded):
                {
                    var ev = JsonSerializer.Deserialize<InvoiceAdjustmentAdded>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Invoices.TryGetValue(ev.InvoiceId, out var inv))
                    {
                        inv.Adjustments.Add(new InvoiceAdjustmentRecord
                        {
                            AdjustmentId = ev.AdjustmentId,
                            Kind = ev.Kind,
                            Amount = ev.Amount,
                            Notes = ev.Notes
                        });
                        RecomputeInvoiceStatus(inv, asOfDate);
                    }
                    break;
                }
                case nameof(InvoiceAdjustmentReversed):
                {
                    var ev = JsonSerializer.Deserialize<InvoiceAdjustmentReversed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Invoices.TryGetValue(ev.InvoiceId, out var inv))
                    {
                        var adj = inv.Adjustments.FirstOrDefault(a => a.AdjustmentId == ev.AdjustmentId);
                        if (adj != null) adj.IsReversed = true;
                        RecomputeInvoiceStatus(inv, asOfDate);
                    }
                    break;
                }

                // --- Invoice Payments ---
                case nameof(InvoicePaymentRecorded):
                {
                    var ev = JsonSerializer.Deserialize<InvoicePaymentRecorded>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Invoices.TryGetValue(ev.InvoiceId, out var inv))
                    {
                        inv.Payments.Add(new InvoicePaymentRecord
                        {
                            PaymentId = ev.PaymentId,
                            AccountId = ev.AccountId,
                            Amount = ev.Amount,
                            CurrencyCode = ev.CurrencyCode,
                            PaidDate = ev.PaidDate,
                            Method = ev.Method,
                            ExternalReference = ev.ExternalReference
                        });
                        RecomputeInvoiceStatus(inv, asOfDate);
                    }
                    break;
                }
                case nameof(InvoicePaymentReversed):
                {
                    var ev = JsonSerializer.Deserialize<InvoicePaymentReversed>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Invoices.TryGetValue(ev.InvoiceId, out var inv))
                    {
                        var pmt = inv.Payments.FirstOrDefault(p => p.PaymentId == ev.PaymentId);
                        if (pmt != null) pmt.IsReversed = true;
                        RecomputeInvoiceStatus(inv, asOfDate);
                    }
                    break;
                }
                case nameof(InvoicePaymentUnapplied):
                {
                    var ev = JsonSerializer.Deserialize<InvoicePaymentUnapplied>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Invoices.TryGetValue(ev.InvoiceId, out var inv))
                    {
                        var pmt = inv.Payments.FirstOrDefault(p => p.PaymentId == ev.PaymentId);
                        if (pmt != null) pmt.IsUnapplied = true;
                        RecomputeInvoiceStatus(inv, asOfDate);
                    }
                    break;
                }

                // --- Generation tracking ---
                case nameof(ContractBillGenerated):
                {
                    var ev = JsonSerializer.Deserialize<ContractBillGenerated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (!state.GeneratedCycleKeys.ContainsKey(ev.ContractId))
                        state.GeneratedCycleKeys[ev.ContractId] = new HashSet<string>();
                    state.GeneratedCycleKeys[ev.ContractId].Add(ev.CycleKey);
                    break;
                }
                case nameof(ContractInvoiceGenerated):
                {
                    var ev = JsonSerializer.Deserialize<ContractInvoiceGenerated>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (!state.GeneratedCycleKeys.ContainsKey(ev.ContractId))
                        state.GeneratedCycleKeys[ev.ContractId] = new HashSet<string>();
                    state.GeneratedCycleKeys[ev.ContractId].Add(ev.CycleKey);
                    break;
                }
            }
        }

        return state;
    }

    private static void RecomputeBillStatus(BillRecord bill, DateOnly? asOfDate)
    {
        if (bill.IsCancelled || bill.IsWrittenOff || bill.IsDisputed) return;
        bill.Status = BillingStatusRules.ComputeStatus(bill.EffectiveTotal, bill.TotalPaid,
            bill.IsCancelled, bill.IsDisputed, bill.IsWrittenOff, asOfDate ?? bill.DueDate);
    }

    private static void RecomputeInvoiceStatus(InvoiceRecord inv, DateOnly? asOfDate)
    {
        if (inv.IsCancelled || inv.IsWrittenOff || inv.IsDisputed) return;
        inv.Status = BillingStatusRules.ComputeStatus(inv.EffectiveTotal, inv.TotalPaid,
            inv.IsCancelled, inv.IsDisputed, inv.IsWrittenOff, asOfDate ?? inv.DueDate);
    }
}
