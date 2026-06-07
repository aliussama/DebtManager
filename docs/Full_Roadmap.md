🧠 STEP 1 — FINANCIAL DOMAIN DECOMPOSITION
(This is the foundation stone. Everything else depends on this.)


🎯 Objective of this step
Define what exists in the financial world, independently of:

•	UI
•	Database
•	Programming language
•	Technology

If something exists in real life, it must exist here as a concept.
 
🚫 What we are NOT doing
•	No tables
•	No SQL
•	No classes
•	No screens
•	No buttons Only meanings.


🧱 Core Principle (non-negotiable)
The app models REALITY, not Excel.

Excel stores numbers.
Your app models obligations, time, decisions, and consequences.



🧩 DOMAIN VOCABULARY (Canonical Language)
This vocabulary becomes LAW.
Every developer (including future-you) must obey it.


1️⃣ PERSON
A real individual whose finances are being managed.

•	Can have multiple roles:
o	Debtor
o	Creditor
o	Guarantor
•	May represent:
o	Self
o	Family member
o	Legal dependent
 
   The app never assumes “one person = one role”.


2️⃣ FINANCIAL INSTITUTION
An entity that imposes rules. Examples:
•	Banks
•	Universities
•	Developers (property companies)
•	Credit card issuers
•	Government bodies

   Institutions don’t just hold money — they define behavior.


3️⃣ OBLIGATION (🔥 MOST IMPORTANT CONCEPT)
An obligation is a promise to pay. Examples:
•	Loan
•	Tuition
•	Installment plan
•	Credit card balance
•	Subscription
•	Penalty

Key Truth

An obligation exists even if no payment happens yet.

Obligation ≠ Payment
 
4️⃣ OBLIGATION SCHEDULE
Defines WHEN and HOW the obligation unfolds. Supports:
•	Fixed dates
•	Recurring rules
•	Variable timing
•	Conditional timing Examples:
•	Monthly
•	Quarterly
•	Custom dates
•	“If unpaid after X days…”

   Time is first-class — never inferred.


5️⃣ INSTALLMENT
A single required slice of an obligation. Properties:
•	Due date
•	Expected amount
•	Currency
•	Rules applied to it

   Installments can exist before money exists.


6️⃣ RULE (⚙️ INTELLIGENCE LIVES HERE)
Rules define behavior, not data. Types:
 
•	Interest rules
•	Penalty rules
•	Grace period rules
•	Tax rules
•	Fee rules
•	Early payment rules
•	Restructuring rules

   Rules are:

•	Versioned
•	Time-bound
•	Institution-specific

No “if” statements scattered in code — only rules.


7️⃣ EVENT (🧠 EVENT-DRIVEN THINKING)
An event is something that happened at a moment in time. Examples:
•	Payment made
•	Payment missed
•	Rule triggered
•	Income received
•	Schedule modified
•	Obligation restructured

   Events are immutable. History is sacred.


8️⃣ PAYMENT
A payment is an event, not a state. Characteristics:
•	Date
•	Amount
 
•	Source
•	Target obligation
•	Allocation logic Supports:
•	Partial payments
•	Overpayments
•	Late payments
•	Multi-installment allocation


9️⃣ INCOME
An incoming cash event. Supports:
•	Fixed income
•	Variable income
•	One-time income
•	Seasonal income
•	Conditional income

   Income is not guaranteed unless explicitly defined as such.


🔟 CASH FLOW
A derived concept, never stored.

Cash Flow = Σ Income Events – Σ Obligation Effects


   Cash flow is recalculated, not saved.


1️⃣1️⃣ SCENARIO
A hypothetical branch of reality.
 
Used for:

•	Loan simulation
•	Refinancing
•	“What if I delay this?”
•	“What if I pay early?”

   Scenarios never modify reality unless approved.


1️⃣2️⃣ FINANCIAL STATE
The result of applying:

•	All events
•	All rules
•	Over time

This is what the user sees.



🧠 Hidden but Critical Concepts
✔ Audit Trail
Every change is traceable.

✔ Versioned Reality
Past assumptions remain valid for their time.

✔ Explainability
Every number must answer:

“Why is this number like this?”
 
✅ Completion Criteria for STEP 1
You are done ONLY when:

•	Every real-world financial thing maps to a concept
•	No concept overlaps another
•	No concept leaks implementation details

   If a future feature cannot be explained using this vocabulary → domain is broken
 
🟦 STEP 2 — Feature Universe Map
Goal: enumerate the entire universe of features a real person might need, and organize it so nothing is forgotten, ever.

2.1	Feature taxonomy (how we’ll structure completeness)
We’ll use a capability map (not a backlog):

1.	Foundation (must exist to make anything trustworthy)
2.	Operations (day-to-day usage: add/edit/pay/track)
3.	Intelligence (forecast, detect crisis, advise)
4.	Simulation (what-if, refinance, loan vs card, etc.)
5.	Governance (audit, versioning, rollback, compliance-style reliability)
6.	Integration (banks/rates/import/export/notifications)
7.	Customization (rules, templates, power-user controls)
8.	UX Modes (beginner/advanced/expert)

Everything below is included because it can realistically be needed.



✅ 2.2 Feature Universe (Full List)
A)	User, Identity, and Access
•	Multi-user profiles (family, assistant accountant, read-only viewer)
•	Roles & permissions (owner/admin/viewer/auditor)
•	Local-only mode + cloud account mode
•	Multi-device session management
•	Secure unlock (PIN/biometric where OS supports)
•	Activity log per user

B)	Core Financial Modeling
•	People/entities (self/family/third parties)
•	Institutions (banks, universities, developers, government)
•	Accounts (cash, bank accounts, cards, e-wallets)
•	Obligations (loan/tuition/installment/subscription/overdraft/guarantee)
 
•	Assets linked to obligations (property, car, etc.)
•	Multi-currency support + FX rates + revaluation logic

C)	Scheduling and Time Complexity
•	Fixed schedules (explicit dates)
•	Recurring schedules (monthly/quarterly/annual/custom cadence)
•	Irregular schedules (manually specified series)
•	Grace periods, holidays/weekend shifts
•	Variable installments (index-linked, step-up/down)
•	Rescheduling / deferment / restructuring flows
•	Retroactive edits with correct historical recomputation

D)	Payments and Allocation
•	Full/partial/over/under payments
•	Late payments handling
•	Payment allocation strategies:
o	by oldest installment
o	by highest interest
o	user-directed split across obligations
•	Fees/penalties/taxes attached to a payment event
•	Reversals / chargebacks / corrections
•	Proof attachments (receipt scan, bank statement pdf)

E)	Income and Cash Sources
•	Fixed income schedules
•	Variable income (ranges, probability, scenarios)
•	Seasonal income patterns
•	One-time income
•	Income delays (salary late, rent late)
•	Cash reserve / emergency fund modeling

F)	Fees, Taxes, Interest, Penalties (Rule-Driven)
•	Bank-specific fee catalog (processing, admin, insurance)
•	Interest calculation methods (simple/compound/daily/monthly, etc.)
•	Penalty rules (fixed, percentage, escalating, grace thresholds)
•	Taxes & stamp duties modeling
 
•	Early repayment penalties + settlement discounts
•	Credit card min payment + revolving interest rules
•	Rule versioning (bank changes policy over time)

G)	Dashboards and Views
•	Timeline view (all obligations on time axis)
•	Monthly cashflow calendar (income vs outflow per day)
•	Stress heatmap (risk intensity)
•	Obligations list with drill-down
•	Bank exposure view (how much per bank)
•	Category view (education, property, loans, cards)

H)	Forecasting and Crisis Detection
•	Forward forecast (months/years)
•	Risk detection:
o	negative months
o	low buffer months
o	penalty-trigger months
o	overlap spikes
•	“Explain why” engine (trace which obligations caused deficit)
•	Sensitivity analysis (if income drops X%, what breaks)

I)	Simulation and Decision Support
•	What-if sandbox (branch reality without affecting base)
•	Loan offers comparison (fees + APR/effective cost)
•	Credit card vs loan payoff strategy
•	Early settlement vs keep schedule
•	Refinance / consolidate debts
•	Payment plan optimizer (given budget, minimize penalties/interest)
•	Goal planning (be debt-free by date, keep min cash buffer)

J)	Alerts, Notifications, and Reminders
•	Due-date reminders (configurable lead time)
•	Crisis warnings (projected deficit)
•	Rule-trigger alerts (grace ending, penalty about to apply)
•	Multi-channel notifications (desktop + email optional)
 
•	Quiet hours and escalation

K)	Import/Export and Interop (Excel replacement must be painless)
•	Excel import wizard (map columns, validate, preview)
•	CSV import/export
•	PDF report export
•	Backup/restore (local + encrypted)
•	Data migration tooling (schema upgrades)

L)	Reporting and Audit-Grade Outputs
•	Monthly/quarterly/yearly reports
•	Bank statement style ledger
•	Obligation amortization schedules
•	Net worth view (optional, but realistic)
•	Audit log export (who changed what when)
•	Change history diff per record

M)	Security, Reliability, and Data Integrity
•	Encrypted local database
•	Secure key storage (OS keychain/DPAPI where available)
•	Tamper-evident logs (hash chain optional)
•	Automatic backups + restore points
•	Conflict-safe sync (event-based, merge rules)
•	Offline-first guarantees (app usable with zero internet)

N)	Customization and Power-User Controls
•	Rule editor UI (with guardrails + test harness)
•	Templates (property plan, tuition plan, loan plan)
•	Custom categories/tags
•	Custom dashboards
•	Advanced “manual adjustment” events (explicitly marked, auditable)
 
O)	Egypt-Specific Extensions (without hardcoding)
•	Bank catalog as data pack (editable, versioned)
•	Arabic/English UI localization
•	Local date formats and common fee structures
•	Pluggable “country pack” architecture (Egypt now, others later)


✅ 2.3 Completeness check mechanism
Every future feature request is evaluated by:

•	Need realism: could a real user need it? (YES/MAYBE ⇒ include)
•	Scope fit: debt/cashflow/banking/decision-making related?
•	Audit impact: does it affect history? (then event + version required)
•	Rule impact: does it vary by institution/time? (then rule-driven)
 
🟦 STEP 3 — Domain-Driven Architecture Design (No UI yet)
The architecture must survive completeness. That means:

•	modular boundaries
•	rule-driven logic
•	event/history integrity
•	simulation as first-class
•	offline-first + sync
•	audit/version/rollback built-in

3.1	Bounded Contexts (Modules with clear responsibilities)
1)	Ledger Core (Reality + history)

Owns:

•	Events (payments, income, adjustments, schedule changes)
•	Immutable history store
•	Aggregation into “financial state” at any time

Exposes:

•	GetState(atDate)
•	AppendEvent(event)
•	RebuildProjections(range)

2)	Obligations (Debts/installments/schedules)

Owns:

•	Obligation definitions
•	Installment generation rules (schedule expansion)
•	Restructure/reschedule operations

Exposes:

•	GenerateInstallments(obligation, range)
•	ApplyRestructure(plan)
 
3)	Rules Engine (Bank logic, taxes, penalties, interest)

Owns:

•	Rule definitions (InterestRule, FeeRule, PenaltyRule, TaxRule, GraceRule)
•	Rule versioning + effective dates
•	Rule evaluation (deterministic, explainable)

Exposes:

•	Evaluate(obligation/installment, context)
•	Explain(calculationId)

4)	Cashflow & Forecasting

Owns:

•	Monthly/daily cashflow calculations
•	Forecast models (deterministic baseline + scenario variations)
•	Crisis detection and “why” tracing

Exposes:

•	Forecast(range, assumptions)
•	DetectRisks(range)
•	ExplainRisk(riskId)

5)	Simulation (What-if sandbox)

Owns:

•	Forking reality into scenarios
•	Applying hypothetical events/rules/offers
•	Comparing results and trade-offs

Exposes:

•	CreateScenario(fromDate)
•	ApplyHypothesis(h)
•	Compare(scenarioA, scenarioB)

6)	Reporting

Owns:
 
•	Accountant-grade outputs
•	Audit exports
•	PDF/CSV/Excel exports (later)

Exposes:

•	GenerateReport(type, range, filters)

7)	Security & Identity

Owns:

•	AuthN/AuthZ
•	Encryption policy and key handling
•	Permission checks

8)	Sync & Collaboration

Owns:

•	Offline-first sync protocol (event-based)
•	Conflict resolution (merge rules)
•	Multi-device consistency


3.2	Layered Architecture (Enforced boundaries)
Presentation (Desktop UI)
└── ViewModels / UI state only
Application Services (Use-cases)
└── Orchestrate domain operations (no business math here)
Domain Layer (Truth)
└── Entities + Value Objects + Domain Services + Rules (ALL logic)
Infrastructure (Plumbing)
└── SQLite/Postgres, file storage, sync transport, crypto, APIs


Two “never break” rules here:

•	UI never computes finance. It asks the domain and renders.
•	Database never contains business meaning. It stores events/records; meaning comes from domain.
 
3.3	Event-first core (to enable audit + rollback + sync)
Instead of “updating balances”, we store:

•	IncomeReceived
•	PaymentMade
•	InstallmentScheduled
•	ObligationCreated
•	RuleVersionActivated
•	CorrectionApplied
•	RestructureApplied

Then we derive current state by replaying events + rules. This is what makes:
•	audit logs automatic
•	rollback possible
•	multi-device sync sane
•	retroactive edits safe


3.4	Module Dependency Rules (to keep it scalable)
•	UI → Application → Domain → Infrastructure (one direction)
•	Rules Engine is called by Ledger/Obligations/Forecasting but never depends on UI/DB.
•	Simulation depends on Domain services, but writes to a scenario event store, not the real one.


3.5	Production folder structure (example)
•	App.Desktop (UI)
•	App.Application (use-cases, orchestration)
•	App.Domain (entities, value objects, rule definitions, calculators)
•	App.Infrastructure (SQLite/Postgres repos, crypto, sync client)
•	App.Sync (protocol, conflict resolution, transport)
•	App.Reporting (renderers/exporters)
•	App.Tests (domain tests, rule tests, scenario tests)
 

 
🟦 STEP 4 — Rule Engine Design
Objective: make all banking/fees/taxes/penalties/interest/scheduling logic data-driven, versioned, explainable, and testable — with zero hardcoding.
This is the “completeness enabler”. If we get this right, adding any Egyptian bank (or changing policies
over time) becomes adding/updating data, not rewriting code.


4.1	Non-negotiable requirements for the Rule Engine
A)	Deterministic

Same inputs ⇒ same outputs. No hidden randomness.

B)	Time-bound + versioned

Rules change in real life. We must support:

•	“Bank X changed early settlement fee on 2024-07-01”
•	Old contracts must still compute using old rules for past periods.

C)	Explainable

Every computed number must have a trace:

•	which rule(s) fired
•	what inputs were used
•	formula breakdown
•	intermediate results

D)	Composable

Multiple rules can apply together (tax + fee + interest + penalty).

E)	Safe to edit

Rules must be editable (power-user/admin) with guardrails:

•	validation
•	sandbox test runs
 
•	conflict detection
•	approval workflow (optional but realistic)


4.2	Core Concepts (Rule Engine Vocabulary)
1)	RulePack

A named collection of rules, usually per institution/bank and sometimes per product.

•	Bank: CIB
•	Product: CreditCard Platinum
•	Loan: Mortgage Standard

2)	RuleVersion

A version of a pack with:

•	effective_from, effective_to
•	jurisdiction (Egypt)
•	contract_scope (applies to new contracts only? or all from date?)

3)	Rule

A single conditional policy:

•	when it applies (predicate)
•	what it produces (effects/calculations)
•	priority/order
•	tags (interest/fee/tax/penalty/grace/schedule)

4)	Evaluation Context

All inputs a rule may need:

•	obligation details (principal, rate, dates, type)
•	installment details (due date, expected amount)
•	payment history (events)
•	current evaluation date
•	account/bank context
•	user preferences (allocation strategy, rounding)
 
5)	Effects

Rules don’t “change state directly” — they produce effects:

•	AddCharge(amount, label, ledger_code)
•	AccrueInterest(amount, basis, rate_used)
•	ApplyGrace(days)
•	ShiftDueDate(rule)
•	RequireMinimumPayment(amount)
•	AddTax(amount, tax_type)

These effects are then applied by the domain layer to build projections and balances.


4.3	Rule Types We Must Support (Complete Coverage)
Financial Charges

•	Fixed fees
•	Percentage fees
•	Tiered fees (brackets)
•	Min/max caps
•	One-time vs recurring

Interest

•	Simple vs compound
•	Daily/monthly accrual
•	Rate schedules (intro rate, step-up)
•	Variable rates (linked to a reference rate — even if manual data)

Penalties

•	Fixed late fee
•	Percentage late fee
•	Escalation after N days
•	“Only once per cycle” vs “per day”

Grace Periods

•	“No penalty for first X days”
 
•	“No interest until statement date”
•	“Special grace for certain products”

Credit Card Rules

•	Statement cycles
•	Minimum payment computation
•	Revolving interest
•	Cash advance rules
•	Fees (annual, late, overlimit)

Loans

•	Amortization generation policy
•	Insurance/administrative costs
•	Early settlement fee
•	Restructure/refinance impacts

Taxes & Stamp Duties

•	Percent of fee/interest/transaction
•	Fixed stamp duty per transaction
•	Tax exemptions / thresholds

Scheduling Rules

•	Weekend/holiday shifting
•	“15th of month” type anchors
•	Quarterly patterns
•	Custom calendars (Egypt holidays as a data pack)

Currency & Rounding

•	Currency-specific rounding
•	Bank rounding policy (ceil/floor to nearest unit)
•	FX conversion rules (spot vs average vs user-provided)


4.4	How Rules Are Represented (Data, not code)
We’ll use a JSON/YAML rule DSL stored in the database (and exportable).
 
Structure (high level)

{
"pack_id": "bank.cib.creditcard.platinum", "version": "2025.01",
"effective_from": "2025-01-01", "effective_to": null,
"rules": [
{
"id": "late_fee",
"type": "fee", "priority": 100, "when": {
"all": [
{ "field": "installment.days_overdue", "op": ">", "value": 0 },
{ "field": "obligation.product_type", "op": "==", "value": "credit_card" }
]
},
"effect": { "add_charge": {
"label": "Late Payment Fee",
"amount": { "fixed": 150.0, "currency": "EGP" }
}
}
}
]
}


Why this format works

•	Editable
•	Versionable
•	Testable
•	Portable (Egypt pack today, others later)
 
4.5	Evaluation Pipeline (How the Engine Runs)
Step 1 — Select applicable rule packs

Based on:

•	obligation.bank_id
•	product type
•	contract dates
•	evaluation date

Step 2 — Resolve the active version

Pick the version where:

•	effective_from ≤ eval_date < effective_to (or null)

Step 3 — Build context snapshot

Compile all needed facts from domain events:

•	days overdue
•	outstanding principal
•	statement balance
•	paid-to-date
•	last payment date
•	etc.

Step 4 — Evaluate rules in deterministic order

Order is critical for correctness:

1.	scheduling/grace rules
2.	base interest accrual rules
3.	penalty rules
4.	fees
5.	taxes (often applied on fees/interest) We enforce order with:
•	phase + priority
 
Step 5 — Produce Effects + Explanation Trace

Output:

•	list of effects (charges/interest/etc.)
•	full trace (for UI “why”)

Step 6 — Apply effects in Domain Layer

Domain layer decides how effects impact projections, balances, and reports.


4.6	Explainability Design (Trust Requirement)
Every evaluation produces a Calculation Trace record:

•	calc_id
•	timestamp
•	input snapshot hash
•	rules fired (ids)
•	per-rule breakdown:
o	predicate results
o	formula inputs
o	intermediate values
o	final effect(s) UI can show:
•	“Late fee applied because installment overdue = 12 days”
•	“Interest = principal × daily_rate × days = …” This is the “accountant replacement” requirement.


4.7	Conflict Detection & Guardrails (Professional Editing)
When rules are edited/imported:

•	Schema validation (required fields, types)
•	Semantic validation:
o	impossible conditions
 
o	negative rates/fees unless allowed
o	circular dependencies
•	Conflict checks:
o	two rules applying same fee twice unintentionally
o	overlapping versions with same effective range
•	Sandbox test suite must pass before activation


4.8	Built-in Rule Testing Harness (Non-optional)
A “Rule Lab” inside the app:

•	Pick an obligation + date range
•	Run evaluation
•	See:
o	effects
o	trace
o	differences between versions
•	Save as regression tests:
o	“Given this context, expected late fee = 150 EGP”

This is how you keep correctness when packs grow to hundreds of rules.


4.9	How Egypt “All Banks” Is Done (No hardcoding)
We ship a Country Pack: Egypt containing:

•	bank list (as data)
•	product templates (loan/card/overdraft)
•	common fee/interest policies as starting points
•	holiday calendar pack
•	default rounding/currency behaviors Each bank becomes:
•	RulePack + Versions + Metadata

Users can:

•	choose a bank product template
 
•	adjust to match their actual contract terms
•	keep the audit trail of rule changes


✅ Output of Step 4 (what we now “have”)
•	A rule engine that is:
o	data-driven
o	versioned
o	time-aware
o	explainable
o	testable
o	safe to edit
•	A structure that can model any bank policy without code changes.




🟦 STEP 5 — Data Model v1 (NEVER BREAK THIS)
Objective: design a schema that supports complete scope, event history, auditability, rollback, offline-first sync, and rule/version evolution — without painting us into a corner.
This is not “tables for the UI”. This is a financial ledger + rules repository + sync log.


5.1	Principles (non-negotiable)
1)	Event-first, append-only history

•	You never overwrite financial reality.
•	You append events and corrections.

2)	Separate “facts” from “derived state”

•	Stored: events, rule versions, user edits, attachments, identities.
•	Derived: balances, forecasts, “current status”.
 
3)	Every record is versioned & auditable

•	“Who changed what, when, from which device”
•	Support rollback by replay + reversal events.

4)	Sync-safe IDs

•	Use GUID/UUID (or ULID) everywhere.
•	No auto-increment IDs as primary keys across devices.

5)	Rules are data, not code

•	Rule packs and versions live in DB, validated, testable.


5.2	High-level data domains (what the DB must store)
1.	Identity & Access
2.	Reference Catalogs (banks, products, currencies, calendars)
3.	Domain Definitions (obligations, schedules, accounts)
4.	Event Store (the immutable truth)
5.	Rule Store (packs, versions, rules, tests)
6.	Derivations/Read Models (optional caches for speed)
7.	Audit & Versioning
8.	Sync & Conflict Handling
9.	Files/Attachments


5.3	Core Tables (v1)
A)	Identity & Access

users

•	user_id (UUID)
•	display_name
•	created_at
•	security_policy_id

roles
 
•	role_id
•	name (owner/admin/editor/viewer/auditor)

user_role_bindings

•	user_id
•	role_id
•	scope (global / per-entity)


B)	Reference Catalogs

institutions

•	institution_id (UUID)
•	type (bank/university/developer/government/other)
•	name
•	country_code (EG)
•	metadata_json

products

•	product_id
•	institution_id
•	product_type (loan/credit_card/overdraft/tuition/installment_plan)
•	name
•	metadata_json

currencies

•	currency_code (EGP, USD…)
•	minor_units (2)
•	rounding_policy_default

calendars

•	calendar_id
•	name (Egypt Holidays 2026)
•	timezone (Africa/Cairo)
•	definition_json
 
C)	Accounts & Cash Containers

accounts

•	account_id
•	owner_entity_id (person/family/business)
•	institution_id (nullable for cash)
•	account_type (cash/bank_account/credit_card/ewallet/other)
•	currency_code
•	metadata_json


D)	Entities (People / Groups)

entities

•	entity_id
•	type (person/family/business/other)
•	name
•	metadata_json


E)	Obligations (Debts / Commitments)

obligations

•	obligation_id
•	owner_entity_id
•	institution_id
•	product_id (nullable)
•	obligation_type (loan/property_installment/tuition/credit_card/overdraft/guarantee/subscription/custom)
•	currency_code
•	start_date
•	contract_end_date (nullable)
•	principal_amount (nullable: not all obligations have principal)
•	metadata_json (contract terms, references, notes)

Key: obligations store “definition”, not computed balances.
 
F)	Scheduling Definitions (Rule-driven, extensible)

schedule_defs

•	schedule_id
•	obligation_id
•	schedule_type (fixed_dates/recurrence_rule/custom_series/generated_by_rulepack)
•	schedule_spec_json (RRULE-like, or explicit date list, or pointers to rules)
•	timezone
•	created_at This lets you model:
•	“15th Sep / 30th Nov / 28th Feb”
•	monthly/quarterly/annual
•	dynamic schedules


5.4	Event Store (THE TRUTH)
G)	Events (immutable append-only)

events

•	event_id (UUID)
•	stream_id (usually obligation_id or account_id or global)
•	event_type (enum string)
•	occurred_at (timestamp)
•	effective_date (date) ← financial meaning date (may differ from occurred_at)
•	actor_user_id
•	device_id
•	correlation_id (for grouping multiple events in one action)
•	causation_id (parent event)
•	payload_json (typed by event_type)
•	payload_schema_version
•	hash (optional tamper evidence)
•	is_voided (boolean) + void_reason (rare; prefer reversal events)

Event types you MUST support in v1

Obligation lifecycle
 
•	ObligationCreated
•	ObligationUpdated (metadata only; financial changes via specific events)
•	ObligationClosed

Scheduling

•	ScheduleDefined
•	ScheduleRescheduled
•	InstallmentsGenerated (if we materialize generation)

Payments

•	PaymentMade
•	PaymentReversed
•	PaymentAllocated (if allocation is explicit and auditable)

Income

•	IncomeReceived
•	IncomeAdjusted

Fees/Interest/Penalties (usually derived, but can be posted)

•	ChargeAssessed (posted fee/penalty)
•	InterestAccrued (posted interest)
•	TaxApplied

Corrections

•	CorrectionApplied (links to what it corrects)
•	BackdatedAdjustment

Rules

•	RulePackInstalled
•	RulePackVersionActivated
•	RulePackVersionDeprecated

Why events instead of mutable “status”:

•	audit log becomes automatic
•	rollback is possible
•	sync becomes “merge event streams”
 
5.5	Rule Store (bank logic as data)
rule_packs

•	rule_pack_id (string or UUID)
•	institution_id (nullable if generic pack)
•	name
•	description
•	created_at

rule_pack_versions

•	rule_pack_version_id
•	rule_pack_id
•	version_label (e.g., 2025.01)
•	effective_from
•	effective_to (nullable)
•	applies_to (product_type / product_id / obligation_type / wildcard)
•	status (draft/active/deprecated)
•	created_by
•	created_at
•	signature_hash (optional)

rules

•	rule_id
•	rule_pack_version_id
•	rule_key (stable identifier like “late_fee”)
•	phase (schedule/grace/interest/penalty/fee/tax/rounding)
•	priority (int)
•	rule_json (predicate + effect)
•	enabled (bool)

rule_tests

•	test_id
•	rule_pack_version_id
•	name
•	context_json
•	expected_effects_json
•	expected_trace_assertions_json

This is the “Rule Lab” backing store.
 
5.6	Attachments & Evidence
attachments

•	attachment_id
•	owner_type (event/obligation/account/entity)
•	owner_id
•	file_name
•	mime_type
•	storage_uri (local path or encrypted blob ref)
•	sha256
•	encrypted (bool)
•	created_at

event_attachments

•	event_id
•	attachment_id


5.7	Audit, Versioning, Rollback
Even with events, you still need explicit governance: change_sets
•	change_set_id
•	actor_user_id
•	created_at
•	summary
•	status (applied/rolled_back)

change_set_events

•	change_set_id
•	event_id Rollback means:
•	append reversal/correction events in a new change_set
 
•	never delete


5.8	Derived Read Models (Optional but practical)
To keep UI fast, you can maintain cached projections:

readmodel_obligation_state

•	obligation_id
•	as_of_date
•	outstanding_amount
•	next_due_date
•	risk_level
•	computed_at
•	computed_from_event_id (high watermark)

readmodel_monthly_cashflow

•	month (YYYY-MM)
•	projected_income
•	projected_outflow
•	net
•	min_daily_balance_estimate
•	computed_at
•	watermark_event_id

These are rebuildable caches. If corrupted, you rebuild from events.


5.9	Sync & Conflict Strategy Tables
devices

•	device_id
•	user_id
•	device_name
•	created_at
•	last_seen_at

sync_state
 
•	device_id
•	last_pulled_event_id
•	last_pushed_event_id
•	last_sync_at

event_conflicts

•	conflict_id
•	event_id_a
•	event_id_b
•	conflict_type
•	detected_at
•	resolution_status
•	resolution_payload_json

Because we’re event-based, most conflicts resolve by:

•	merging streams
•	or requiring user decision when two edits collide (e.g., reschedule vs restructure)


✅ Step 5 Output (What we achieved)
You now have a never-break schema blueprint that supports:

•	complete real-world cases
•	audit logs automatically
•	retroactive edits safely
•	rule versioning over time
•	simulation (via separate scenario event streams later)
•	offline-first sync




🟦 STEP 6 — Calculation Engine (The Heart)
Objective: turn raw events + rules + schedules into trustworthy numbers: balances, upcoming dues, cashflow, forecasts, risks, and explanations.
 
This step defines deterministic engines with strict inputs/outputs and zero UI/DB dependency.


6.1	Golden Principles
1.	Pure computation
•	Engine functions are deterministic: (inputs) -> (outputs + trace)
•	No database calls inside engines.
2.	Reproducible “as-of” evaluation
•	You must compute results as of a date/time reliably (past, present, future).
3.	Explainability is built-in
•	Every output has an explanation trace referencing:
o	events used
o	rules fired
o	formulas
4.	Separately testable engines
•	Each engine has unit tests and golden scenarios.


6.2	Engine Suite (Separate Engines, Clear Responsibilities)
Engine A — Event Replay Engine (Ledger Engine)
Purpose: reconstruct financial state by replaying events in order.

Inputs

•	Event stream(s): obligation/account/global
•	Evaluation date (as-of)
•	Rule pack versions resolved for that time
•	Settings (rounding, allocation strategy)

Outputs

•	Current outstanding amounts per obligation
•	Payment history summary
•	Derived state snapshots
 
•	Trace map (event→state transitions)

Notes

•	Supports retroactive edits naturally: add a backdated event ⇒ replay.


Engine B — Schedule Expansion Engine
Purpose: turn schedule definitions into concrete expected installments for a time range.

Inputs

•	schedule_defs
•	range [from_date, to_date]
•	calendar rules (weekends/holidays)
•	obligation metadata (start date, anchors)

Outputs

•	A list of ExpectedInstallment items:
o	due_date
o	expected_amount (or formula reference)
o	installment_key (stable ID derived from schedule, not DB id)
o	tags (tuition, property, etc.)

Why this matters

Installments are obligations, not payments. This engine generates what should happen.


Engine C — Rule Evaluation Engine (Financial Effects)
Purpose: for each obligation/installment and time window, compute effects:

•	interest
•	penalties
•	taxes
•	fees
 
•	grace behavior
•	rounding

Inputs

•	context snapshot (days overdue, principal, payment history, etc.)
•	selected rule pack version
•	evaluation date/time

Outputs

•	Effects list (AddCharge, AccrueInterest, ApplyTax…)
•	Rule trace (which rules fired and why)


Engine D — Allocation Engine (Payments → Installments)
Purpose: decide how a payment reduces obligations/installments.

Inputs

•	payment event
•	list of outstanding expected installments / balances
•	allocation strategy (user choice + bank constraints)

Outputs

•	Allocation map:
o	payment_id → [(installment_key, amount)]
•	Residual handling:
o	overpayment → prepayment bucket or principal reduction (rule-driven)
o	underpayment → remaining overdue amounts

Completeness requirement

Supports:

•	split across multiple obligations
•	minimum payment rules (credit cards)
•	priority ordering policies (penalties first vs principal first)
 
Engine E — Cashflow Forecast Engine
Purpose: compute projected cash-in/cash-out over time.

Inputs

•	income schedules + income events
•	expected installments (from Schedule Expansion)
•	derived effects (from Rule Engine)
•	optional scenario overrides

Outputs

•	Daily or monthly cashflow table:
o	opening balance (if account modeling enabled)
o	inflows
o	outflows
o	net
o	minimum balance inside period
•	Explanation: biggest drivers


Engine F — Risk & Crisis Detection Engine
Purpose: detect financial stress periods.

Inputs

•	forecast outputs
•	user-defined buffer thresholds (min cash, max debt ratio, etc.)

Outputs

•	Risk objects:
o	deficit months
o	low buffer windows
o	penalty trigger windows
o	“overlap spikes”
•	“Why” trace:
o	top obligations causing risk
o	timeline breakdown
 
Engine G — Trace & Explain Engine
Purpose: unify explanations into human-readable and auditor-readable forms. Outputs:
•	UI-friendly explanation
•	Audit report explanation
•	Debug trace for developers


6.3	Unified Data Contracts (the engine input/output types)
These are conceptual contracts (language-agnostic):

ExpectedInstallment

•	installment_key (stable deterministic)
•	obligation_id
•	due_date
•	expected_amount (money or formula reference)
•	currency
•	schedule_origin (which schedule_def)
•	tags

ComputedCharge

•	charge_type (interest/fee/penalty/tax)
•	amount
•	applies_to (installment_key or obligation_id)
•	computed_for_date_range
•	rule_id / rule_pack_version_id
•	trace_id

FinancialStateSnapshot

•	as_of_date
 
•	outstanding_by_obligation
•	overdue_by_obligation
•	next_due_installments
•	applied_charges_summary
•	trace_links

CashflowProjectionRow

•	period (day/month)
•	total_income
•	total_obligations
•	total_charges
•	net
•	cumulative_balance_estimate
•	risk_flags


6.4	Deterministic Execution Order (Critical)
When computing a forecast for a range:

1.	Expand schedules → expected installments
2.	Replay events up to each point → current state
3.	Evaluate rules → interest/penalties/fees/taxes
4.	Apply allocations for payments
5.	Aggregate into cashflow
6.	Detect risks
7.	Generate explanations

This order guarantees consistent results.



6.5	Handling Edge Cases (Completeness Checklist)
Retroactive edits

•	Add backdated payment ⇒ replay + recompute forward.
 
Changing bank rules over time

•	Rule version selection depends on eval date and contract scope.

Overlapping obligations

•	Forecast engine aggregates across all installments and charges.

Multiple currencies

•	Forecast can show:
o	per-currency cashflow
o	consolidated in base currency using chosen FX policy

Credit cards (special)

•	Statement cycles create installments dynamically:
o	statement generated → due installment created
o	min payment rule applies
o	revolving interest accrues

Restructuring

•	Restructure event creates:
o	old obligation closure state (not deletion)
o	new schedule / new principal / new fees
o	trace linking old→new


6.6	Testing Strategy (so it never lies)
A)	Golden Scenario Tests

•	Your father’s real Excel dataset converted into events:
o	expected outputs match Excel for historical months
o	forecasts align with known future dues

B)	Rule Regression Tests

•	Each bank pack includes tests:
o	given context → expected fee/interest
 
C)	Property-based invariants

•	Outstanding never goes negative unless explicit credit.
•	Sum(payments allocated) ≤ payment amount.
•	Replay is idempotent.


✅ Step 6 Output
You now have the full computation architecture:

•	modular engines
•	deterministic order
•	traceability everywhere
•	supports every complexity class required by your “complete” rule




🟦 STEP 7 — Simulation & Decision System
Objective: make the app an advisor, not a tracker — by allowing safe “what-if” branches, comparing
options, and recommending trade-offs with transparent math. Simulation must be:
•	isolated (never corrupts real data)
•	reproducible (deterministic)
•	explainable (“why this option is better”)
•	complete (covers loans, cards, refinancing, rescheduling, early settlement, income shocks, emergencies, etc.)


7.1	Core Concept: Reality vs Scenario
Reality

•	Your real event streams: obligations, income, payments, rule versions.
 
Scenario

A fork of reality:

•	references a baseline snapshot (date/time + event watermark)
•	has its own scenario events (hypothetical)
•	optionally overrides rules/assumptions (also as scenario events)

Key rule: scenarios never “edit” reality. They only add hypothetical deltas.


7.2	Scenario Storage Model (matches Step 5 architecture)
Tables / streams conceptually

•	scenario (id, name, baseline_date, baseline_event_watermark, created_by)
•	scenario_events (same structure as events, but scenario-scoped)
•	scenario_assumptions (fx policy, inflation, income variability assumptions)
•	scenario_results_cache (optional read model) This allows:
•	multiple scenarios running in parallel
•	sharing scenarios (family or advisor)
•	comparing scenarios later


7.3	Hypothesis Library (Complete Coverage)
A “hypothesis” is a structured action the user wants to test.

A)	Payments & Timing

•	Pay extra amount on date X
•	Delay payment by N days
•	Split payment across obligations differently
•	Bulk pay multiple installments
•	Apply emergency cash reserve injection
 
B)	New Financing

•	Take a new loan (principal, term, rate, fees, grace)
•	Use credit card to cover installments
•	Use overdraft temporarily
•	Balance transfer / card refinancing

C)	Restructure / Refinance

•	Refinance existing loan with bank B
•	Consolidate multiple debts into one
•	Change installment schedule (defer, extend, change frequency)
•	Early settlement (partial or full)

D)	Income Shocks

•	Income drop X% for Y months
•	Income delayed
•	New income source
•	Seasonal variability adjustments

E)	Policy / Bank Rule Changes

•	Change fee schedule (simulate bank policy update)
•	Change interest reference rate (variable rate scenario)

F)	Risk Controls

•	Enforce minimum cash buffer
•	Enforce max monthly outflow ratio
•	Stop using certain instruments (no overdraft, no card)

Everything above is needed by real users → included.


7.4	Scenario Execution Pipeline (Deterministic)
Given a scenario:

1.	Load baseline reality events up to watermark
2.	Append scenario events
 
3.	Run the same Step 6 engines:
a.	schedule expansion
b.	event replay
c.	rule evaluation
d.	allocation
e.	forecast
f.	risk detection
4.	Produce results + traces

Important: same engine code as reality. No duplicate logic.


7.5	Comparison Framework (How we judge options)
Each scenario produces a metrics vector:

Cost Metrics

•	Total paid over horizon
•	Total interest
•	Total fees
•	Total penalties
•	Effective APR / total borrowing cost
•	Opportunity cost estimate (optional)

Liquidity Metrics

•	Minimum projected balance
•	Number of deficit months
•	Maximum single-month outflow
•	Buffer violations count

Risk Metrics

•	Penalty-trigger probability (if stochastic income is enabled)
•	Credit utilization risk (cards/overdraft)
•	Concentration risk per bank

Goal Metrics

•	Debt-free date
 
•	Time to stable buffer
•	“Can I afford tuition on time?” confidence

Explainability

•	Top 5 changes that caused improvement/worsening
•	“why” chain: which obligations moved which months


7.6	Decision Support Outputs (What user sees)
For any “what-if”, UI must output:

1.	Verdict summary
•	“Option B reduces crisis months from 3 to 0, but increases total cost by 6.1%.”
2.	Trade-off table
•	cost vs risk vs liquidity vs timeline
3.	Timeline diff
•	months that changed
•	obligations causing changes
4.	Explanation trace
•	rule-level detail if user drills down No black box. Ever.


7.7	Recommendation Engine (Advisory, not controlling)
We can implement recommendations in tiers:

Tier 1 — Deterministic heuristics (must exist)

•	Pay highest penalty-first if liquidity tight
•	Avoid compounding interest spirals
•	Keep minimum cash buffer
•	Prefer early settlement when penalty < interest saved

Tier 2 — Optimization (should exist for completeness)

•	Constrained optimization:
 
o	minimize total cost
o	subject to no-deficit months
o	subject to min cash buffer
•	Budget allocation across debts monthly

Tier 3 — Stochastic simulation (optional advanced)

•	Monte Carlo on variable income/expenses
•	Outputs probability of crisis months

Even Tier 1 must be present; Tier 2 is realistic for “complete”; Tier 3 is advanced but valuable.


7.8	Scenario Types (Product features)
1)	Quick What-if

•	single change (pay early, delay, new income)

2)	Plan Builder

•	a sequence of actions over months:
o	“pay +2000 EGP extra monthly to loan A”
o	“avoid overdraft”
o	“hold 50k EGP buffer”

3)	Offer Comparator

•	input multiple bank offers (loan/card)
•	app computes total cost + risk impact

4)	Auto-Solver

•	user goal: “eliminate crises and minimize cost”
•	system proposes a plan with explanation


✅ Step 7 Output
You now have a complete simulation/decision system design:
 
•	scenario isolation
•	hypothesis library
•	deterministic execution
•	comparison metrics
•	recommendation tiers
•	explainable outputs




🟦 STEP 8 — Security & Data Integrity Layer
Objective: make the system trustworthy enough to hold real financial life data: secure at rest, secure in transit, tamper-evident, auditable, recoverable, and safe under sync conflicts.
This is not “add encryption later”. Security is part of correctness.


8.1	Threat Model (what we defend against)
Local threats

•	Laptop stolen / lost
•	Someone opens the local DB file
•	Malware reads files (we can’t fully stop this, but we reduce exposure)
•	Unauthorized family member access
•	Data corruption (power loss, disk issues)

Sync/cloud threats

•	MITM attacks (intercepted traffic)
•	Account takeover
•	Server breach
•	Sync conflicts causing silent data loss
•	Rogue device syncing into account

Integrity threats

•	Undetected history edits
 
•	“Excel style overwrites” losing truth
•	Rule changes altering old results without trace


8.2	Security Goals (non-negotiable)
1.	Confidentiality: data unreadable without keys
2.	Integrity: detect tampering + preserve history
3.	Availability: backups + recovery + offline mode
4.	Auditability: who did what, when, from which device
5.	Least privilege: users/devices only access what they should



8.3	Local Data Protection (Offline-first core)
A)	Encrypted local database
•	SQLite (local) must be encrypted.
•	Keys must not be stored in the DB.

Key storage options (best practice):

•	Windows: DPAPI / Credential Manager
•	macOS: Keychain
•	Linux: Secret Service / GNOME Keyring (fallback to user-supplied passphrase)

B)	App-level encryption policy
•	Encrypt:
o	DB file
o	attachments (receipts, PDFs)
o	local backups
•	Use authenticated encryption (confidentiality + integrity)

C)	Screen/session security
•	Auto-lock after inactivity
•	“Quick hide” mode
 
•	Optional separate “view-only mode” on shared device


8.4	Authentication & Authorization
A)	Authentication (who you are)
•	Local-only mode:
o	passphrase / PIN
o	optional OS biometrics for unlock
•	Cloud mode:
o	password + strong KDF
o	optional 2FA (TOTP)

B)	Authorization (what you can do)
Roles:

•	Owner (everything)
•	Admin (manage users/devices, rule packs)
•	Editor (financial data edits)
•	Viewer (read-only)
•	Auditor (read + export logs, no edits) Permissions must be enforced in:
•	UI (to prevent actions)
•	Application services (real enforcement)
•	Server API (cloud enforcement)


8.5	Data Integrity & Audit (Finance-grade)
A)	Immutable event log (already in Step 5)
•	Never edit/delete financial events.
•	Corrections happen as new events.
 
B)	Tamper-evident chaining (recommended)
Each event includes:

•	prev_hash
•	hash = H(prev_hash + event_payload + metadata)

This makes offline tampering detectable.
(Doesn’t stop malware, but detects silent edits.)

C)	Audit log (human-readable)
•	Who changed what
•	before/after for non-financial metadata updates
•	ties to change_sets for rollback


8.6	Backups, Restore, and Disaster Recovery
A)	Local automatic backups
•	rotating backups (e.g., daily + weekly)
•	encrypted backup archive
•	restore wizard

B)	Cloud snapshots
•	periodic server-side encrypted snapshots
•	user-controlled restore points

C)	Rollback
•	rollback is implemented by:
o	creating a new change_set that appends reversal/correction events
•	never “rewind and delete”
 
8.7	Sync Security (Multi-device)
A)	Transport security
•	TLS everywhere (HTTPS)
•	certificate pinning optional (advanced)

B)	Device registration & trust
•	Each device has a device_id + key pair
•	Owner approves new devices (or uses one-time pairing code)

C)	Sync protocol (event-based)
•	Sync sends events, not “current balances”
•	Server validates:
o	permissions
o	schema versions
o	signature/tamper checks (if enabled)

D)	Conflict resolution
Because events merge naturally, conflicts are mostly:

•	two schedule changes at same time
•	two rule pack activations
•	contradictory edits Resolution strategy:
•	auto-merge when safe
•	otherwise create event_conflicts entry requiring user decision
•	never silently discard
 
8.8	Rule Pack Security (because rules change money)
Rule editing is dangerous — so:

•	Rule packs have:
o	status: draft → tested → active
o	mandatory test suite pass before activation
o	version activation event (audited)
•	Optional:
o	signed official bank packs
o	user modifications clearly labeled “custom”


8.9	Privacy & Data Minimization
•	Offline-first means no mandatory cloud.
•	Cloud sync stores only what’s needed.
•	Avoid collecting:
o	unnecessary identifiers
o	analytics by default (opt-in only)


✅ Step 8 Output
You now have a complete security + integrity design:

•	encrypted at rest
•	secure authentication + roles
•	tamper-evident history
•	robust backups + recovery
•	secure event-based sync with conflict handling
•	controlled rule edits
 
🟦 STEP 9 — UI / UX DESIGN (ONLY NOW)
Objective: design a professional, modern, accountant-grade desktop UI that can handle full scope complexity without hiding logic, while still being usable by non-experts.
This UI does not simplify the domain. It reveals it progressively.


9.1	Core UX Philosophy (non-negotiable)
1️⃣ Time is the primary axis Money is meaningless without when. Every major screen answers:
“What happens, on which date, and why?”

2️⃣ Nothing magical, nothing hidden
Every number:

•	is clickable
•	has an explanation
•	traces back to events + rules

3️⃣ Progressive complexity
•	Beginner sees safe summaries
•	Advanced user drills down
•	Expert can edit rules and replay logic Same app. Different depth.


9.2	Global Navigation Structure
Dashboard Timeline
 
Obligations Income Simulation Reports
Rule Lab (Advanced) Audit & History Settings


Navigation is horizontal + context-aware, not nested hell.


9.3	Screen-by-Screen Design
 
🟩 1) Dashboard — “Financial Control Room”

 
What it answers immediately:

•	Am I safe this month?
•	When is the next crisis?
•	Where is my money going?
 
Components

•	Monthly cashflow graph (income vs obligations)
•	Risk indicator bar
o	Green / Yellow / Red
o	Click to see why
•	Upcoming critical dates (next 30–90 days)
•	Key metrics
o	Min projected balance
o	Total outstanding
o	Next major payment

UX rule

No scrolling to understand status. Scrolling only adds detail.


🟩 2) Timeline — “Truth Over Time”


This is the most important screen. Design
•	Horizontal time axis
•	Rows:
o	Income streams
o	Obligations
o	Charges (interest/fees)
•	Color coding:
o	Expected
o	Paid
o	Overdue
o	Simulated

Interactions

•	Hover: quick summary
•	Click: drill into obligation/installment
•	Right-click: simulate action at this date

Why this replaces Excel

Excel shows rows.
Timeline shows collisions.
 
🟩 3) Obligations — “Everything You Owe”
 
List View

Columns:

•	Name
•	Type
•	Bank
•	Outstanding
•	Next due
•	Risk level

Detail View (click any obligation)

Tabs:

•	Overview (summary + health)
•	Schedule (installments timeline)
•	Payments (events ledger)
•	Charges (interest/fees/penalties)
•	Rules applied (read-only for most users)
•	History (all changes)

UX rule

You never “edit balance”.
You edit events or definitions.



🟩 4) Income — “Money Coming In”

Features

•	Fixed income schedules
•	Variable income with ranges
•	Seasonal patterns
•	Delay simulation (salary late)

Visualization

•	Monthly bars
•	Confidence shading (for variable income)
 
🟩 5) Simulation — “What If?”

Modes

1.	Quick What-If
a.	Single action
2.	Plan Builder
a.	Sequence of actions
3.	Offer Comparator
a.	Loan vs card vs refinance

Output

•	Side-by-side comparison
•	Cost vs risk vs liquidity
•	Timeline diff
•	Clear verdict:

“Option B eliminates March deficit but costs +4.2% total.”
 
🟩 6) Reports — “Accountant-Grade Output”

Reports

•	Monthly cashflow
•	Amortization schedules
•	Bank exposure
•	Payment history
•	Audit reports Export:
•	PDF
•	CSV
•	Excel (structured, not raw dumps)
 
🟩 7) Rule Lab — “Advanced / Expert Only”

Features

•	View active rule packs
•	Rule version timeline
•	Edit rules (with validation)
•	Run rule tests
•	Compare rule versions Clear warning banners:
“Changing rules affects calculations.”
 
🟩 8) Audit & History — “Nothing Ever Disappears”

Shows

•	Every change
•	Who did it
•	From which device
•	Before / after
•	Rollback option (if permitted) This is what makes it trustworthy.


9.4	UX Modes (Critical for completeness)
Beginner Mode

•	Safe defaults
 
•	No rule editing
•	Simplified explanations

Advanced Mode

•	Full drill-down
•	Manual adjustments
•	Simulation tools

Expert Mode

•	Rule Lab
•	Raw event views
•	Debug traces

Switchable anytime. No data loss.


9.5	Explainability UI Pattern (Everywhere)
Whenever a number appears:

•	•   icon → “Why?”
•	Opens a side panel:
o	events involved
o	rules fired
o	formulas
o	dates

This is what replaces “trust me”.


9.6	Import UX (Excel → App)
Wizard:

1.	Upload Excel
2.	Map columns
3.	Validate dates/amounts
4.	Preview timeline
5.	Confirm → events created
 
No black box import.



✅ Step 9 Output
You now have a complete, professional UI/UX blueprint:

•	timeline-centric
•	explanation-first
•	scalable to full complexity
•	suitable for real financial life




🟦 STEP 10 — Cloud Sync & Multi-Device
Objective: guarantee offline-first reliability, safe multi-device access, and zero silent data loss, while respecting your complete / professional rule.
Cloud is a service, not a dependency.


10.1	First Principle (Never Break)
The app must work fully with zero internet.
Cloud only enhances availability, backup, and multi-device sync.


10.2	Sync Philosophy (Why event-based)
We never sync “balances” or “current state”. We sync:
•	immutable events
•	rule pack versions
•	attachments metadata
 
•	audit records

State is reconstructed locally by replaying events (Step 6). This guarantees:
•	correctness
•	auditability
•	conflict transparency


10.3	High-Level Sync Architecture



[ Desktop App ]
├─ Encrypted Local DB
├─ Event Store
├─ Rule Packs
└─ Sync Client
│
 
▼
[ Secure Sync API ]
├─ Auth & Device Registry
├─ Event Log Store
├─ Conflict Detector
└─ Snapshot/Backup Service
│
▼
[ Cloud Storage ]



10.4	Device Model (Professional-grade)
A)	Device identity

Each device has:

•	device_id
•	asymmetric key pair
•	human-readable name

B)	Device trust

•	First device = root trust
•	New device requires:
o	approval from owner device
o	or one-time pairing code

C)	Device permissions

•	Full edit
•	Read-only
•	Audit-only


10.5	Sync Lifecycle (Step-by-Step)
Step 1 — Startup (offline or online)

•	App loads local DB
 
•	Full functionality available immediately

Step 2 — Sync handshake (if online)

•	Authenticate user
•	Validate device trust
•	Exchange last known event watermark

Step 3 — Pull phase

•	Server sends:
o	new events
o	new rule versions
o	metadata updates
•	Client verifies:
o	schema
o	signatures
o	permissions

Step 4 — Apply & replay

•	Append pulled events
•	Rebuild affected read models
•	Flag potential conflicts

Step 5 — Push phase

•	Client sends its new local events
•	Server validates and stores

Step 6 — Conflict resolution (if needed)

•	Auto-merge when safe
•	Otherwise create conflict record


10.6	Conflict Types & UX (Never Silent)
A)	Safe auto-merge

•	Independent payments
 
•	Independent income events
•	Attachments

B)	Needs user decision

•	Two schedule changes on same obligation
•	Rule pack activation clashes
•	Restructure vs payment overlap

Conflict UX

•	Explicit conflict list
•	Side-by-side comparison
•	Choice produces a new event, not deletion


10.7	Cloud Data Responsibilities
Server stores:

•	encrypted event streams
•	rule packs + versions
•	device registry
•	backups/snapshots

Server does NOT:

•	compute balances
•	decide financial logic
•	silently alter data

This keeps the cloud stateless in logic terms.


10.8	Backups via Sync (Disaster Recovery)
A)	Continuous snapshots

•	Server periodically builds encrypted snapshots
•	Snapshot = compressed event streams + rule packs
 
B)	Restore flow

1.	New device logs in
2.	Select snapshot
3.	Download → decrypt → replay
4.	Local DB restored exactly


10.9	Attachments Sync (Receipts, PDFs)
Strategy

•	Metadata synced with events
•	Binary data:
o	uploaded encrypted
o	chunked
o	resumable

Offline behavior

•	Attachment visible as “pending upload”
•	Hash ensures integrity


10.10	Privacy Controls
•	User chooses:
o	sync everything
o	sync without attachments
o	local-only mode
•	Explicit “sign out device” revokes sync access


10.11	Performance & Scalability
•	Event batching
•	Read-model caching
•	Partial replay (only affected obligations)
•	Background sync (non-blocking UI)
 
✅ Step 10 Output
You now have:

•	offline-first, event-based sync
•	safe multi-device access
•	explicit conflict handling
•	cloud-assisted backup & recovery
•	zero silent data loss




🟦 STEP 11 — AI Layer (Advisory, Not Controlling)
Objective: add intelligence that is genuinely useful in real finance without ever compromising correctness.
Your deterministic engines (Steps 6–7) remain the source of truth.
AI becomes an advisor layer that proposes, explains, and detects—never silently changes.


11.1	Non-Negotiable Boundaries
✅ AI may do
•	detect anomalies and risks early
•	suggest options and plans
•	explain complex situations in plain language
•	help categorize/import messy data
•	forecast uncertain variables (income variability, unexpected expenses) as probabilities
•	generate summaries and reports

❌ AI must never do

•	write/modify financial events in “Reality” without explicit user confirmation
 
•	invent bank fees, tax rules, or interest methods
•	override rule engine outputs
•	hide assumptions

Rule: AI outputs must always include “Assumptions used” when uncertainty exists.


11.2	Where AI Fits in the Architecture
Domain Engines (deterministic)
├─ Ledger Replay
├─ Rules Engine
├─ Forecast Engine
└─ Simulation Engine
▲
│
AI Advisor Layer (probabilistic + NLP)
├─ Insight Generator
├─ Anomaly Detector
├─ Recommendation Builder
├─ Natural Language Explainer
└─ Import/Classification Helper


AI reads:

•	projections, risks, traces, scenarios, history AI writes:
•	only “proposals” (draft actions / suggested scenarios), never real events.


11.3	AI Feature Set (Complete, Realistic)
A)	Anomaly Detection (Must)
Detect patterns that humans miss:

•	payments unusually late compared to history
•	sudden spikes in fees/interest
•	duplicate payments (possible double charge)
•	abnormal income drop vs typical
 
•	“silent drift” (small recurring costs increasing)

Output: alert + explanation + linked evidence (events, obligations).


B)	Crisis Prediction Under Uncertainty (Must)
Deterministic forecast says what happens if assumptions hold. AI adds: “How likely is a crisis if income varies?”
•	Model uncertain incomes as distributions or scenarios
•	Run Monte Carlo (or scenario sampling) using deterministic engine as evaluator
•	Output probability:
o	“Feb deficit probability: 68%”
o	“Most likely driver: tuition installment + loan overlap”

Important: the final math evaluation is still engine-based; AI only proposes distributions and interprets results.


C)	Recommendation Engine (Must)
Generate plans with trade-offs:

Examples:

•	“To avoid March deficit, either delay X, pay Y early, or take short overdraft.”
•	“Paying extra 2,000 EGP/month to loan A reduces total interest by … but increases short-term risk
in April.”

Implementation strategy:

•	AI proposes candidate actions
•	The simulation engine evaluates each option
•	AI ranks and explains

So recommendations are verified by the deterministic simulator.
 
D)	Natural Language Financial Explanations (Must)
Turn traces into human explanations:

•	“Why is this month red?”
•	“Why did interest increase?”
•	“What changed since last week?”

AI can summarize:

•	top 5 contributing obligations
•	key rules triggered (grace ended, penalty rule activated)
•	suggested mitigation actions (as scenario drafts) All explanation must be backed by trace references.


E)	Smart Import & Data Cleaning (Must)
Excel conversion is messy in real life. AI helps:
•	map columns (“due date”, “installment”, “bank”)
•	detect date formats inconsistencies
•	infer obligation grouping (these 12 rows belong to one property plan)
•	auto-tag categories (property/education/loan)

But it must show a preview and require user confirmation before creating events/obligations.


F)	Bank Offer Parsing (Optional but valuable)
Users may paste loan offers or PDFs/text. AI can extract:

•	nominal rate
•	fees
•	term
•	early settlement penalty
•	grace period

Then create a draft product + draft rule pack for review and testing.
 
Safeguard:

•	extracted terms must be verified by user
•	a “confidence” score displayed
•	the rule lab can run tests against it


G)	Personalized Insights (Optional)
•	“Your tight months are usually Feb/Mar due to tuition.”
•	“Keeping a 50k buffer prevents penalties in 90% of sampled outcomes.”
•	“You are overexposed to Bank X fees—consider consolidation.”


11.4	Safety & Anti-Hallucination Design (Critical)
1)	“Source of truth” gating

AI is only allowed to reference:

•	events in DB
•	rule packs in DB
•	simulator outputs
•	traces produced by engines

If the user asks: “What are fees of Bank X?”
AI must answer from the bank rule pack data—or say it’s unknown until entered.

2)	Structured outputs

AI suggestions must be emitted as structured “Proposals”, e.g.:

•	Proposal type: EarlyPayment
•	Parameters: obligation_id, date, amount
•	Expected impact: pulled from scenario simulation No free-form “do this” without a computed impact.
3)	Human confirmation workflow

Every proposal requires:
 
•	review screen
•	“simulate first” option
•	explicit “Apply to reality” action creating real events


11.5	Data & Model Strategy (Practical)
Tiered approach (recommended)

•	Tier 0 (No AI): app fully functional with deterministic engines (must)
•	Tier 1 (Local ML / rules-based AI):
o	anomaly detection heuristics
o	clustering for import mapping
•	Tier 2 (LLM assistant):
o	explanations
o	offer parsing
o	plan narration
•	Tier 3 (Probabilistic forecasting):
o	scenario sampling / Monte Carlo on uncertain incomes Even if AI is disabled, the app stays complete and correct.


11.6	“Explainable Recommendation” Output Format (Standard)
Whenever AI recommends anything, it must include:

•	Action (what)
•	Reason (why)
•	Evidence (events/rules/trace IDs)
•	Impact (numbers from simulation)
•	Trade-offs (what gets worse)
•	Assumptions (uncertainty) This is what makes it professional.
 
✅ Step 11 Output
You now have an AI layer that:

•	increases value without risking correctness
•	is verified by deterministic simulation
•	is auditable and explainable
•	supports real workflows (import, analysis, offers, planning)

🟦 STEP 12 — Stress Testing & Edge Cases
Objective: prove that the app survives real-world abuse, not just happy paths. This is where
“professional” is either earned or lost.

Nothing ships until it survives this step.


12.1	Philosophy of Testing (Finance-Grade)
A)	We test behavior, not screens

UI tests are secondary. Correctness lives in:
•	domain logic
•	rule evaluation
•	event replay
•	sync merging

B)	We assume users will:

•	make mistakes
•	change their mind
•	backdate edits
•	lose devices
•	input messy data
•	experience financial chaos The app must not panic.
 
12.2	Stress Test Categories (Complete Coverage)


🟥 A) Temporal & Historical Stress
1️⃣ Retroactive edits

•	Add a payment 2 years ago
•	Change a schedule anchor in the past
•	Correct an income event retroactively

Expected behavior:

•	Full replay forward
•	Future forecasts adjust correctly
•	Audit log shows correction
•	No duplicated charges


2️⃣ Long timelines

•	20+ years of obligations
•	Thousands of installments
•	Decades of income events

Expected behavior:

•	Deterministic results
•	Acceptable performance
•	Partial replay optimization kicks in


🟥 B) Rule Engine Stress
3️⃣ Rule version changes
•	Bank changes penalty rules mid-contract
 
•	Old installments use old rules
•	New installments use new rules

Expected behavior:

•	Correct rule version selection by date
•	Explanation shows which version applied


4️⃣ Rule conflicts

•	Two fee rules triggered unintentionally
•	Overlapping effective dates
•	Contradictory predicates

Expected behavior:

•	Conflict detected
•	Validation warning
•	Rule pack blocked or requires explicit override


🟥 C) Financial Edge Cases
5️⃣ Partial & messy payments

•	Payment smaller than penalty
•	Payment between multiple overdue installments
•	Overpayment with no clear target

Expected behavior:

•	Allocation engine handles gracefully
•	Residuals tracked
•	No negative balances unless allowed


6️⃣ Credit instruments

•	Credit card minimum payment unpaid
 
•	Revolving interest compounding
•	Overdraft crossing limit

Expected behavior:

•	Proper penalty/interest escalation
•	Risk flagged early
•	Clear explanation


7️⃣ Restructuring chaos
•	Restructure then backdate payment
•	Early settlement after restructure
•	Partial restructure

Expected behavior:

•	Clear linkage old → new obligation
•	No orphan balances
•	History intact


🟥 D) Cashflow Crisis Scenarios
8️⃣ Income shock
•	Salary delayed 2 months
•	Income reduced by 40%
•	Seasonal income disappears

Expected behavior:

•	Forecast reflects shock
•	Crisis detection fires
•	Simulation suggests mitigations
 
9️⃣ Obligation overlap spikes

•	Tuition + property + loan same month
•	Quarterly + annual overlap

Expected behavior:

•	Timeline clearly shows collision
•	Risk flagged
•	“Why” explanation points to overlap


🟥 E) Sync & Multi-Device Stress
🔟 Offline divergence

•	Device A offline for weeks
•	Device B makes changes
•	Device A makes different changes

Expected behavior:

•	Events merge
•	Conflicts detected explicitly
•	No silent loss


1️⃣1️⃣ Device loss

•	Device lost without sign-out

Expected behavior:

•	Owner revokes device
•	Sync access denied
•	Local encrypted DB remains unreadable
 
1️⃣2️⃣ Partial sync failure

•	Network drops mid-sync
•	Attachment upload interrupted

Expected behavior:

•	Resume cleanly
•	No partial corruption
•	Hash verification


🟥 F) Data Corruption & Recovery
1️⃣3️⃣ DB corruption

•	Simulate corrupted local DB

Expected behavior:

•	Detect corruption
•	Restore from backup or snapshot
•	Replay events safely


1️⃣4️⃣ Rule pack corruption

•	Invalid rule JSON
•	Missing fields

Expected behavior:

•	Rule pack rejected
•	Old version remains active
•	User warned
 
🟥 G) Performance & Scale
1️⃣5️⃣ Large datasets

•	10k+ events
•	Hundreds of obligations
•	Dozens of scenarios

Expected behavior:

•	UI remains responsive
•	Background computation
•	Progress indicators
•	Partial recompute


1️⃣6️⃣ Simulation explosion

•	Many scenarios
•	Long horizons

Expected behavior:

•	Scenario isolation
•	Cache reuse
•	User warned when computation is heavy


12.3	Automated Test Matrix (What must exist)
Unit Tests

•	Rule evaluation correctness
•	Allocation logic
•	Schedule expansion

Integration Tests

•	Event replay → forecast
•	Scenario vs reality comparison
 
•	Rule version transitions

Property-Based Tests

•	Replay idempotence
•	No money created/destroyed unintentionally
•	Allocation sums ≤ payment

Regression Tests

•	Real historical datasets (e.g., your father’s Excel)
•	Known outcomes locked in


12.4	Manual “Abuse” Testing (Non-optional)
Before release, do:

•	Random edits
•	Rapid undo/redo
•	Contradictory actions
•	Wrong inputs on purpose

If the app survives this, it’s professional.



✅ Step 12 Output
You now have:

•	a full stress-testing strategy
•	explicit edge-case coverage
•	confidence that completeness holds under chaos
 
🟦 STEP 13 — Documentation & Governance
Objective: ensure the system is understandable, auditable, maintainable, and trustworthy for years, not just usable today.
This is what separates serious financial software from “a clever app”.


13.1	Why Documentation Is Part of the Product
For a complete professional app, documentation is not optional:

•	Users need to trust numbers.
•	Auditors (or future-you) need to verify logic.
•	Rules must be explainable.
•	Changes must be traceable.
•	New features must not break old assumptions.

Documentation is part of correctness.


13.2	Documentation Layers (Complete Coverage)


📘 A) User Documentation (Trust & Adoption)
1️⃣ Conceptual Guide — “How the App Thinks”
Explains:

•	obligations vs payments
•	events vs balances
•	schedules vs installments
•	why numbers may change after edits
•	why history is never deleted

Goal: replace Excel mindset with system mindset.
 
2️⃣ Feature Guides (Screen-level)
For each major screen:

•	what it shows
•	how it’s computed
•	what actions do (and don’t do)
•	common pitfalls Examples:
•	“Why this month is red”
•	“What happens when you backdate a payment”


3️⃣ Simulation Guide
•	how scenarios differ from reality
•	interpreting comparisons
•	understanding trade-offs Critical to prevent misuse.


📗 B) Financial Transparency Documentation
4️⃣ Rule Documentation (Per Bank/Product)
•	human-readable summary:
o	interest method
o	penalties
o	fees
o	taxes
•	version history:
o	what changed
o	when it applied
•	links to rule pack versions This lets users say:
“Yes, this matches my contract.”
 
5️⃣ Calculation Methodology
For each calculation type:

•	formula
•	inputs
•	rounding
•	examples Examples:
•	“How daily compounding interest is calculated”
•	“How minimum credit card payment is computed”


📕 C) Governance & Audit Documentation
6️⃣ Change Log (User-facing)

•	meaningful changes only
•	rule pack updates
•	calculation changes
•	data model upgrades

Not release notes fluff—financially relevant changes.


7️⃣ Audit & History Guide
Explains:

•	event history
•	corrections vs deletions
•	rollback semantics
•	audit exports Important for professionals.
 
📙 D) Developer / Maintainer Documentation
8️⃣ Architecture Guide

•	module boundaries
•	data flow
•	dependency rules
•	where logic lives (and doesn’t)

This prevents accidental rule leakage into UI.


9️⃣ Domain Model Reference

•	definitions of:
o	obligation
o	installment
o	event
o	rule
o	scenario
•	invariants and assumptions This protects the conceptual integrity.


🔟 Rule Authoring Manual
For advanced users/admins:

•	rule DSL reference
•	predicate syntax
•	effect types
•	versioning rules
•	testing workflow

This is essential if users can edit rules.
 
13.3	Governance Model (How the System Evolves Safely)


A)	Rule Governance
•	Draft → Test → Activate lifecycle
•	Mandatory test suite pass
•	Activation creates an event
•	Deprecation keeps history intact


B)	Data Governance
•	Schema migrations are:
o	additive
o	backward-compatible
•	No destructive migrations
•	Old data always readable


C)	Feature Governance
Before adding any feature, ask:
“Could a real person reasonably need this in managing their finances?” YES / MAYBE → include
NO → exclude

This rule is written in the contributor guide.


D)	AI Governance
•	AI suggestions always labeled
•	AI assumptions always visible
•	AI never auto-applies
•	Deterministic engines always override
 
13.4	User Trust Signals (Subtle but Powerful)
UI should surface:

•	last calculation time
•	rule pack versions used
•	data freshness
•	sync status
•	“why” links everywhere

This creates confidence.


13.5	Long-Term Maintainability Checklist
•	New bank = new rule pack (no code change)
•	New country = new country pack
•	New feature = new module, not hack
•	Bugs fixed via:
o	correction events
o	rule version updates
•	No “quick fixes” that bypass architecture


✅ FINAL OUTPUT — COMPLETE ROADMAP FINISHED
You now have:

•	a full-scope professional system design
•	from domain → rules → data → engines → UI → sync → AI → testing → governance
•	zero MVP shortcuts
•	zero hardcoded assumptions
•	full auditability and explainability This design is:
 
•	complex ✔
•	heavy ✔
•	professional ✔
•	extensible ✔
•	worth building ✔