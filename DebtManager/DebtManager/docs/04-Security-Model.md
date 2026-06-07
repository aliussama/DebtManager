# Security Model

## Threat Model (local-first)
- A stolen laptop should not reveal financial data.
- Cloud transport must not reveal event payloads without authorization.
- Sync must not allow event tampering (hash chain).

## Data Protection
- Local DB uses key-wrapping (DPAPI on Windows).
- Encryption keys are stored in LocalKeyStore (device-bound).
- Secrets never committed to source control.

## Integrity
- Event hash chain detects tampering at rest and during sync.
- VerifyStreamAsync must fail on any modified payload.

## Sync Auth
- Requests require x-sync-key header.
- VaultId partitions data.
- Cloud only stores events; business logic stays local.
