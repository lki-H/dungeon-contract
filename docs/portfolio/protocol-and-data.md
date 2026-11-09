# Protocol, configuration and persistent data

The current battle protocol is 8 and the control WebSocket contract remains V2. Admission freezes the run's operation, world, parameters and trial versions. An older client cannot supply default fields to join a newer profile. Runtime schemas and parameter files remain in the repository, including inherited versions still read by code/tests.

Inputs and requests represent intent. The Unity service owns high-frequency movement, hits, projectile lifecycle, invulnerability, action timing and combat state. Go accepts bounded, identity-checked facts required for run state, tasks and settlement. Client presentation acknowledgements do not grant currency or advance contracts.

## Identity and persistence

- Run, source generation and connection generation isolate stale senders.
- Input/action identity and payload fingerprints distinguish retries from conflicts.
- Root attack/cast identity groups task actions; independent damage instances can still carry real repeated hits.
- Terminal participant results, unique reward grants and asset-ledger writes are transactional.
- Redis projections can be rebuilt from durable MySQL facts; the frame loop does not write one row per projectile/tick.

The schema starts from `backend/internal/database/schema.sql`, then applies the unchanged ordered `day37`–`day46` migration chain. Existing migration checksums are verified. Initializing an empty Demo database and verifying an existing database are distinct from reset/restore operations.

## Contract entry points

- [HTTP/control OpenAPI](../openapi.yaml)
- [Current combat-state schema](../game-demo/contracts/runtime-v8/combat-state.schema.json)
- [Battle frame schema](../game-demo/contracts/frame-v8-p5.schema.json)
- [Current runtime OpenAPI](../game-demo/contracts/openapi-v8-p5-runtime.json)
- [Frozen v8 parameters](../game-demo/config/parameters-v8-p5.json)
- [Room coordinates](../game-demo/config/world-v8-p5.json)

The runtime-v8 JSON schema describes the reconstructed player state. Merge revision-bound deltas into the current reliable baseline before validating the full state; a delta is not a standalone complete character document. Recoveries retain absolute game deadlines and consumed/remaining deployment state; retries must not create a tower or repeat a damage jump.
