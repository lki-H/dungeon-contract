# Security policy

Dungeon Contract is a portfolio/demo project, not a public hosted service. The repository must never contain passwords, JWT secrets, private keys, database dumps, Docker/WSL disks, real player credentials, or credential-bearing connection strings.

## Reporting

If this repository is public and you find a security issue, open a private GitHub security advisory when that feature is available. Otherwise contact the repository owner privately through the GitHub profile. Do not include tokens, passwords, private player data, or a working exploit in a public issue.

## Supported scope

Reports are most useful for:

- authentication or authorization bypasses;
- duplicate reward or settlement caused by retries, stale connections, or concurrent requests;
- cross-player or cross-run data exposure;
- unbounded WebSocket, HTTP, database, or worker resource use;
- secret leakage through logs, fixtures, build artifacts, or release packages;
- unsafe Docker, PowerShell, Go, or CI behavior introduced by this repository.

The local LAN/Radmin demo intentionally uses private IPv4 addresses and is not an Internet deployment. Do not expose its MySQL, Redis, internal control port, or development diagnostics to the public Internet.

## Handling a report

Please include the affected commit or release, a minimal reproduction, expected and actual behavior, and whether data or credentials were exposed. The maintainer should first revoke or rotate any exposed secret, preserve a redacted reproduction, patch the smallest safe boundary, add a regression check, and document the release impact.

## Current security boundaries

- Client input is an intent; the Unity authority produces high-frequency combat facts.
- Go validates admission, task/settlement facts, authorization, idempotency, and persistence boundaries.
- MySQL is the durable fact store; Redis data is rebuildable and must not be treated as the sole source of rewards.
- Secrets are external to the source tree and are generated per local Demo storage.
- CI and local scripts must keep request sizes, WebSocket connections, retries, queues, and process lifetimes bounded.

This policy does not claim that the Demo is production-hardened. It describes the intended review boundary for a public portfolio repository.
