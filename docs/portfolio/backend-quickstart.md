# Backend quickstart

The Go service provides accounts, parties, matchmaking, admission, reliable control and settlement. It runs without Unity. A battle requires the separate Unity authority; starting only Go does not simulate combat.

## Prerequisites

- Windows with PowerShell 7 for the local helper; the CI scripts also document Linux execution.
- The Go version declared in `backend/go.mod` and available Docker Linux containers for storage. Start the Docker Linux engine before first-time `Prepare`; it checks whether existing Demo volumes already belong to another credential set.
- A writable external cache/private directory. Source paths may contain spaces. Do not put real credentials inside the repository.

From the repository root, unit tests and static checks do not require Docker:

```powershell
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action GoTest
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action GoVet
```

Database integration tests are explicit and separate. A plain `go test` can skip them; that is not an integration PASS. See [testing and evidence](testing-and-evidence.md).

## First local storage

```powershell
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action Prepare
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action Validate
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action ServicesStart
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action Initialize
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action CheckServices
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action GoBuild
pwsh -NoProfile -File ./deploy/portfolio.ps1 -Action Server
```

`Prepare` generates separate cryptographically random secrets and writes their names, never their values, in progress messages. Existing partial or unrecognized credentials are rejected rather than replaced. `Initialize` applies the unchanged checksum-checked migration chain through the v4 cooperation schema used by v8. Running it again verifies/reconciles the same stages; it is not a reset and does not delete runs.

MySQL is restricted to `127.0.0.1:23316`, Redis to `127.0.0.1:26389`; the database is `game_realtime_dc_demo`. The control service uses `127.0.0.1:12880`; its authority interface uses `127.0.0.1:12890`. Never expose storage or the internal authority interface publicly.

After `Server` reports readiness, use `http://127.0.0.1:12880/ready`. Stop the foreground service with Ctrl+C. `ServicesStop` stops the Demo's containers and retains the named volumes.

## Paths and existing installations

The source root defaults to the script's parent. `DC_CACHE_ROOT`, `DC_PRIVATE_ROOT` and `DC_UNITY_PATH` or matching helper arguments override local defaults. New checkouts use an external directory keyed by their absolute source root. The original development checkout continues to recognize its existing cache and private directory; credentials are not copied to a new checkout automatically.

For an explicit setup, repeat the same paths for subsequent actions:

```powershell
./deploy/portfolio.ps1 -Action Prepare -CacheRoot 'D:/Demo cache' -PrivateRoot 'D:/Demo private'
./deploy/portfolio.ps1 -Action Validate -CacheRoot 'D:/Demo cache' -PrivateRoot 'D:/Demo private'
```

Only one `dc-demo` storage installation uses these fixed loopback ports at a time. If volumes already exist, use their original private directory. Do not fix an authentication error by deleting a volume, regenerating passwords or applying credentials from another checkout.

## Failure checks

- Docker unavailable: open Docker Desktop and wait for the Linux engine; do not reset it.
- Partial secrets or mismatched storage identity: retain the files and verify which private directory belongs to the volumes.
- Port occupied: stop the owning Demo session or choose a separate isolated test namespace; do not kill unrelated services.
- Migration checksum/dirty status: preserve the original SQL and inspect the failed stage. Do not rewrite applied migrations.
- Build out of memory: keep one task running, respect the memory guard and resume after resources are available. Do not lower the guard to obtain a PASS.

The helper restores process-local environment variables after each action; it does not change machine-wide environment, firewall, VPN, Windows paging or WSL memory policy.
