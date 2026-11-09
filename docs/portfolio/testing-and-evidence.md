# Testing boundaries and evidence

The project deliberately separates engineering checks, runtime playtests, and external human acceptance.

## Current evidence classes

| Class | What it can support | What it cannot support |
| --- | --- | --- |
| Go unit/integration tests | Backend state, storage, authorization, idempotency, migrations, and failure branches | Unity visual quality or cross-PC network behavior |
| Unity/source-bound checks | Contract, geometry, content, UI and build-input relationships | Broad balance or human fairness |
| Source-bound build/package audits | That a package matches the measured source/configuration | That every game scenario is fun or complete |
| User-reported latest LAN playtest | The user completed the latest package's playtest, full run and lobby return without major issues | Exact player count, device count, network profile, performance or universal reliability unless recorded |
| Waived retests | A scope decision to stop spending time on specified cases | A PASS result for those cases |

The current public summary is [current-status.md](current-status.md). The original machine-specific records remain in the development workspace; their paths and deleted historical archives are not public evidence downloads.

## Required backend checks for a public clone

```powershell
Push-Location backend
go test ./...
go vet ./...
go mod verify
Pop-Location
```

Integration tests require dedicated Demo MySQL/Redis services and an explicit integration flag. They must not point at a user's existing production or personal database. CI should use disposable credentials and tear down its isolated services after the job.

The repository's bounded CI helper is `deploy/portfolio-ci-backend.ps1`; it is intended for unit/vet checks in the workflow, not as a server launcher; it does not create Docker storage. The separate `deploy/portfolio-ci-dc.ps1` creates disposable, isolated integration storage and must not reuse a developer's running Demo containers or private credentials.

The current-v8 isolated suite selects `TestV8WorkerDispatchAndAttemptFencing`: real MySQL `DATETIME(3)` selection/conditional updates, supported job dispatch, quarantine, stale-result fencing, and failure/retry success. Before creating its fixture, this global Worker test also requires the explicit temporary-storage marker set only by the isolated Compose runner; an integration-enabled invocation without it fails before database access. Use `deploy/portfolio-ci-dc.ps1` with disposable storage. The marker prevents accidental invocation; it is not a substitute for checking storage ownership, and must never be manually copied into an existing player database workflow. Actual execution status is recorded in the dated review, separately from test selection.

On [November 10](review-20261110.md), that isolated runner completed `^TestV8` with `-race`: six top-level tests plus nine Worker subcases, zero failures/skips, followed by successful temporary-resource cleanup. It is not an all-backend race suite. Startup/test failures retain bounded diagnostics from the exact temporary namespace, with generated credentials redacted before output and report serialization.

## Memory-safe execution

Run one heavy task at a time. Start with Go tests, vet, schema and script checks; only then consider Unity builds or rendered playtests. Keep the existing admission and memory guardrails. A protected stop is a useful result and must not be converted to a pass by repeating the same workload or lowering the guardrail.

## Reporting rules

Each result records the source/build identity, command, environment, status, and whether a test was skipped, waived, failed, or passed. Historical results remain tied to their original binary. A changed source requires a new bound build before a prior binary result can be reused.
