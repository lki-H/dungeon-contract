# Public documentation index

This index belongs to the public Dungeon Contract source repository. It points to current, reviewable material and avoids presenting old phase notes as the current product state.

## Start here

- [Project README](../README.md)
- [Current implementation and verification scope](portfolio/current-status.md)
- [Recorded demo and screenshots](portfolio/demo-walkthrough.md)
- [Interview and source walkthrough](portfolio/interview-notes.md)
- [Player guide](game-demo/v8-player-guide.md)
- [LAN/Radmin guide](game-demo/radmin-human-multiplayer-guide.md)

## Backend and architecture

- [Portfolio architecture](portfolio/architecture-overview.md)
- [Reliability and settlement](portfolio/reliability-and-settlement.md)
- [Testing boundaries](portfolio/testing-and-evidence.md)
- [Protocol and data design](portfolio/protocol-and-data.md)
- [Runtime v8 contract](game-demo/contracts/runtime-v8/README.md)

## Running and verifying

- [Backend quickstart](portfolio/backend-quickstart.md)
- [Complete game source build](portfolio/full-demo.md)
- [Public source export procedure](portfolio/public-source.md)

## Release, security, and rights

- [GitHub publication and final review](portfolio/publication.md)
- [Repository lineage and shared maintenance](portfolio/repository-lineage.md)
- [2026-11-10 real storage and race checks](portfolio/review-20261110.md)
- [2026-11-10 browser verification](portfolio/browser-validation.md)
- [2026-11-09 follow-up review and verification](portfolio/review-20261109.md)
- [2026-11-07 original review and fixes](portfolio/review-20261107.md)
- [Current Demo threat model](portfolio/game-realtime-gm-demo-threat-model.md)

- [Security policy](../SECURITY.md)
- [Third-party notices](../THIRD_PARTY_NOTICES.md)
- [Code-license status](../LICENSE-NOTICE.md)
- [Public source export procedure](portfolio/public-source.md)
- [Public export manifest](portfolio/public-export-manifest.json)

## Evidence boundaries

The public repository should not include private credentials, database volumes, raw test payloads, local absolute paths, Unity caches, generated player binaries, or deleted historical archives. The current validation file distinguishes source-bound checks, user-waived retests, and external human acceptance. Historical records are context, not current proof.
