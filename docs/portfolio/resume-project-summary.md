# Resume project summary

## One-line version

**Dungeon Contract — Go game-service backend for a cooperative Unity dungeon-run demo.**

## Resume bullets

- Built a Go/Gin/WebSocket game-service layer for identity, parties, matchmaking, versioned admission, run control, task facts, settlement, and durable reward records.
- Separated high-frequency Unity combat authority from Go business facts; used stable run/source/connection generations, idempotent action/event identities, bounded retries, and transactional reward uniqueness to handle reconnects and duplicate delivery.
- Integrated MySQL durable facts with rebuildable Redis projections and added schema/content/build fingerprints so a client or package cannot silently mix incompatible runtime versions.
- Delivered Windows local/LAN playtest tooling, isolated Docker-backed storage checks, source-bound package audits, and a documented evidence boundary for human feedback versus automated verification.

## Interview prompts

Be ready to explain:

1. Why projectile and collision frames remain in Unity while settlement facts enter Go.
2. How source, run, connection, action, and reward identities prevent duplicate progress.
3. How a stale reconnect is fenced from a newer run.
4. What MySQL guarantees and what Redis is allowed to lose.
5. How retries and outbox records recover without minting rewards twice.
6. Which claims are automated, which are user-reported, and which remain waived or outside the Demo scope.

Avoid claiming Internet-scale throughput, four-player cross-PC stability, or formal performance numbers unless a dated record for the exact release contains those facts.
