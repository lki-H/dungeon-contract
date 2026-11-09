# Public source export

The working development directory contains machine-specific history and existing local services. It is not the upload folder. A separate allowlisted source tree is prepared using:

```powershell
python -B ./deploy/portfolio-export.py --output 'D:/publish/dungeon-contract'
python -B ./deploy/portfolio-public-audit.py --root 'D:/publish/dungeon-contract'
```

The destination's parent must exist and the destination itself must not exist. Git must already be available to evaluate `.gitattributes` in an isolated temporary directory. The exporter refuses source/output overlap, reparse inputs and overwriting existing files. It copies only the [public policy](public-export-manifest.json), normalizes explicitly declared text line endings for Git, checks every exported file hash and emits a relative `source-export.json` inventory of the resulting bytes. It does not initialize the source/output as a Git repository, stage, commit, push or create a GitHub repository.

Unspecified files retain their bytes (`* -text`); SQL, configuration/contracts and Windows command files have explicit preservation rules. Existing migration bytes and content fingerprints therefore survive checkout and GitHub source archives. Regression checks compare the manifest with a real Git index, ZIP archive and two clones with different `core.autocrlf` values. Normalization is confined to the separate export, and the manifest lists affected files; the working source is not rewritten.

## Included and excluded

Included: Go code/tests/migrations, Unity source/project/approved resources, needed current and inherited config/contracts, curated docs, supported frontend source and current CI/launch tools.

Excluded: real credentials/accounts, database data/backups, cached dependencies, Unity Library, executable packages, VHDX, local logs, internal handoffs, obsolete status snapshots, deleted archive references and independent historical experiments outside the supported Demo scope.

The optional AI lobby image, its meta and provider-specific records are excluded from public export because redistribution rights were not established. The original development files are retained locally; public builds use the legal fallback. Original CC0 art/audio and font notices retain their own licenses.

## Verify before publishing

1. Run the source audit and inspect its exact candidate-file report, not only `.gitignore`.
2. From the exported source, follow the backend quickstart and run tests/vet. Confirm explicit isolated integration tests run without skips.
3. Rebuild any executable distributed from changed sources and keep source/config/build bindings intact.
4. Review licenses and the exact Release file inventory; never upload a private directory or database.
5. Only after review, initialize the authorized repository, create truthful commits and publish. Future GitHub CI must run on the actual pushed commit before claiming a green badge.

The owner confirmed public-release rights and MIT code licensing on 2026-11-05. That confirmation does not relicense third-party resources. Public-source preparation requires an actual exported-tree audit and clone checks; a policy file alone is not completion. Binary Release verification is separate.
