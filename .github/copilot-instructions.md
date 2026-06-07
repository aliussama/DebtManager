ARCHITECTURAL LAWS: Finance Management App
Status: Non-Negotiable Professional Standards

Core Principle: The app models REALITY, not Excel. Excel stores numbers; this app models obligations, time, decisions, and consequences.

1. Canonical Vocabulary (The Law)
Every class, variable, and database table must obey this language:


PERSON: A real individual (Debtor, Creditor, or Guarantor).


FINANCIAL INSTITUTION: An entity that defines behavior/rules (Banks, Universities, etc.).

OBLIGATION: A promise to pay (Loan, Tuition, Subscription). It exists independently of payment.


INSTALLMENT: A single required "slice" of an obligation with a due date and currency.


EVENT: An immutable fact that happened at a specific moment (PaymentMade, ScheduleModified).

RULE: Logic that defines behavior (Interest, Penalties, Grace Periods). Rules must be versioned and time-bound.

2. Mandatory Architecture (Step 3)
Event-First Core: We do NOT "update balances." We store immutable events and derive the current state by replaying them.

Layer Separation:


UI: Never computes finance; it only renders domain data.


Domain: Contains ALL business logic and rules.


Infrastructure: SQLite/Postgres stores events; it contains no business meaning.


Offline-First: The app must be 100% functional without internet.

3. Rule Engine Requirements (Step 4)
Zero Hardcoding: No "if" statements for bank logic. All logic must be data-driven via a JSON/YAML DSL.


Determinism: Same inputs must always yield the same outputs.


Explainability: Every number must produce a "Calculation Trace" showing exactly which rules fired and the formulas used.

4. Data Integrity (Step 5 & 8)
Append-Only: Financial reality is never overwritten. Use reversal/correction events instead of deletions.


Sync-Safe IDs: Use GUID/UUID (or ULID) for all primary keys to prevent conflicts across devices.


Encryption: The local database and all attachments must be encrypted at rest using OS-level key storage (DPAPI/Keychain).


Tamper Evidence: Events should be chained via hashes (H(prev_hash + payload)) to detect unauthorized history edits.

5. Governance & Feature Rule
Realism Test: Before adding a feature, ask: "Could a real person reasonably need this in managing their finances?" If NO, exclude it.


No MVP Shortcuts: Simplification of layers, merging of logic, or skipping audit steps is a failure of the professional requirement.