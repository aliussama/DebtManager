# Release Checklist (Non-Skippable)

## Correctness
- [ ] dotnet test passes
- [ ] hash chain tests pass
- [ ] fuzz invariants test passes
- [ ] sync convergence test passes

## Security
- [ ] local keys stored via DPAPI / key store
- [ ] no secrets in repo
- [ ] verify event hash chain in CI

## Data Safety
- [ ] backup/export works (CSV + JSON)
- [ ] rollback strategy documented
- [ ] schema migrations documented

## UX
- [ ] calculations explainable (drill-down)
- [ ] audit log visible and exportable
- [ ] no hidden assumptions
