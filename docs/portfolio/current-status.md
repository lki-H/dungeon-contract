# Current scope and status

Recorded 2026-11-10. Current rules: design2.5 / `dc_ruins.v8.p5.1` / battle8 / control `/ws` V2 / tasks `dc_tasks.v5.p4.1`.

The repository owner reported that the latest existing package completed multiplayer, a full run and return-to-lobby continuation without major problems. This is a **user-reported** result. Player count, PC count, network conditions, exact test date and package checksum were not provided; they are not inferred.

Earlier source-bound native, compatibility, Go storage and two-role UI checks belong to their original source/build identities. Changes for source portability and the optional background fallback require their own relevant checks. The public source does not ship the development machine's private logs, deleted historical evidence or binaries, and does not promote old PASS results to a new release.

The owner explicitly waived formal four-window multiplayer performance, seven automated full-victory requirements and 26 complete-flow/formal-performance reruns of a later package. Those remain waived, not passed, and are not reinstated by this source preparation.

## 2026-11-09 follow-up review

The [November 7 repairs](review-20261107.md) now have current-source C# execution and rebuilt binary evidence: four focused client models, 73 native groups, 23 compatibility groups, ordinary Unity build, Player inputs, both role UI checks, and offline LAN package integrity passed. The build fingerprint now includes shared scripts/importers/meta and embedded configuration inputs; independent LAN builds use their own receipt. Both frontend production builds and backend/tooling checks passed.

At the November 9 checkpoint, storage and race checks were Docker-blocked. They were completed in the [November 10 follow-up](review-20261110.md): the isolated Redis CLI health parameters were corrected, and current-v8 real storage tests ran with `-race`, six top-level passes plus nine Worker subcases, zero failures/skips. Temporary storage and credentials were removed; existing player storage was not used. The added Worker test still requires the isolated-runner marker before fixture creation. Failure reports now retain bounded, redacted container diagnostics. All 80 Python source-tool tests passed.

Browser automation was rejected at the November 9 checkpoint. After the owner explicitly requested completion, [November 10 browser verification](browser-validation.md) ran against disposable real storage: both UI entry points, authentication, observation views, player room actions, disconnect/reconnect and seven controlled Control assertions passed. The [recorded Web demo and screenshots](demo-walkthrough.md) distinguish real browser operations from Unity probe scenes. Read the [storage review](review-20261110.md) and [current threat model](game-realtime-gm-demo-threat-model.md).

## Release boundary

- Go service: identity, party/matchmaking, versioned admission, control, durable facts and settlement.
- Unity authority: frame-by-frame combat and room simulation; Unity client: input/prediction/presentation.
- Current source preparation: portable launch, isolated backend checks and a clean public-source directory.
- Public source is available in `lki-H/dungeon-contract`; [publication details](publication.md) and the actual target-commit Actions result define its review state. Prebuilt release attachments remain separate.
- No claim of production throughput, SLO, public Internet operation, four-PC stability or finalized balance.

See [S1–S3 results](s1-s3-results.md) for commands actually run and material limitations. Missing measurements are not filled with estimates.
