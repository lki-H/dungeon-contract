# Third-party and resource notices

This inventory is a release-review aid. It does not grant rights beyond the terms of each upstream item. Before publishing a binary release, verify that every shipped file is covered by a compatible license and include the required notices.

## Source and generated resources

- **Kenney Tiny Dungeon**: the project resource record identifies this source as CC0-1.0 and records imported tile/prop usage. Preserve the upstream attribution record and the local `Kenney-CC0.txt` when those files are distributed.
- **Original program-generated pixel art and synthesized audio**: the project resource record and `client/unity-demo/Assets/Resources/DungeonContract/P5/LICENSE.txt` describe the listed outputs as CC0-1.0. This applies only to the files enumerated by the provenance record, not to Unity, fonts, historical assets, or third-party packages.
- **AI-generated lobby backdrop**: `ruins-lobby-ai.png` and its provenance are separate from the CC0 family. The configured provider, model identity, and redistribution rights have not been independently attested. Do not include this asset in a public Release until the rights decision is recorded. If the asset is excluded, the client must use a reviewed legal fallback before publishing a runnable package.

## Runtime and build dependencies

- Unity Editor and Unity packages retain their own Unity license and package notices; the repository does not relicense them.
- Go modules are declared in `backend/go.mod` and checksums in `backend/go.sum`. A local scan found license files for many cached modules, but the cache was intentionally cleaned and is incomplete; run a fresh-clone license inventory (for example, a reviewed `go-licenses` workflow) before a binary release.
- React, React DOM, TypeScript, Vite, and Node type packages are declared in the frontend package manifests and lockfiles. Preserve their package notices for any frontend distribution.
- Noto Sans SC and any other bundled font retain their original license. Do not assume a font license from the gameplay asset license.
- MySQL, Redis, Docker Desktop, PowerShell, and Windows are development/runtime prerequisites, not assets relicensed by this repository.

## Release checklist

Before publishing:

1. Generate a package inventory from the exact source commit and build output.
2. Match every non-source asset and dependency to a license or an explicit exclusion.
3. Keep required copyright and attribution text beside the Release or in this file.
4. Remove private credentials, database dumps, caches, Unity `Library`, Docker/WSL data, local logs, and unreviewed AI output.
5. Re-run the source and package audit after the final license decision.
