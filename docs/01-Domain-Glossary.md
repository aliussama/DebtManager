# Domain Glossary

This project uses event-sourcing with effective dating. Definitions here are the source of truth.

## Obligation
A financial responsibility that produces one or more expected installments over time (loan, tuition, property, credit card bill, etc.).

## Schedule
A rule/specification that expands into expected installments (fixed dates, recurring, irregular, future types).

## Expected Installment
An obligation-derived expected payment on a due date. This is a planning artifact.

## Payment
A user action that records money paid (PaymentMade). A payment is not allocation.

## Allocation
How a payment is applied across one or more installments (PaymentAllocated / PaymentAllocationReversed).

## Charge
A computed amount (fee/penalty/interest/tax) produced by rule evaluation as of a date.

## EffectiveDate
Business time. When an event is intended to affect financial reality.

## OccurredAt
System time. When an event was recorded.

## Snapshot
A computed view of financial state as of a date, derived from events + schedule expansion + allocation + rules.

## Scenario
A hypothetical branch of reality computed from baseline state + hypothetical events. Never mutates real history.
