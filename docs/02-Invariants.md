# Invariants (Non-Negotiable)

These must always hold. If broken, it is a production bug.

## History & Audit
1. Events are append-only. No deleting. No overwriting.
2. All changes are represented as additional events (corrections, reversals).
3. Audit trail is derived from events and rule traces.

## Time Semantics
4. EffectiveDate determines financial impact (projection time).
5. OccurredAt is only used for deterministic ordering when EffectiveDate ties.

## Allocation Correctness
6. For any installment: Paid + Outstanding == Expected.
7. Outstanding is never negative.
8. Allocation must be reversible via explicit reversal events.

## Scenario Safety
9. Scenario computations never write to the real event store.
10. Scenario results must be reproducible from baseline + hypotheses.

## Sync Correctness
11. Sync is idempotent: same event pushed twice does not duplicate.
12. Sync convergence: two devices eventually reach the same event set.
13. Event hash chain verification must pass after sync.

## Security
14. Local storage must be encrypted or key-wrapped.
15. Secrets are never hardcoded. Keys are user/device-scoped.
