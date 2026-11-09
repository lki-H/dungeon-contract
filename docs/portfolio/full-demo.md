# Building the complete Demo

The full game requires Windows, Unity `6000.3.25f1` with Windows build support and a valid local license, PowerShell 7, Go, Python 3 and Docker Desktop. Unity is not needed for the separate [backend quickstart](backend-quickstart.md).

## Prepare source and storage

Run the backend `Prepare`, `ServicesStart`, `Initialize`, and `CheckServices` actions first. Set the actual Editor path in the same PowerShell session:

```powershell
$env:DC_UNITY_PATH = 'D:/Unity/6000.3.25f1/Editor/Unity.exe'
./deploy/portfolio.ps1 -Action PrepareUnity
```

`PrepareUnity` configures a project-owned external Library directory only when safe. It does not delete an existing Library, replace a different junction or install/activate Unity. If credentials or storage exist, retain the original private directory.

## Build and check

Run one heavy operation at a time:

```powershell
./deploy/dc-v8.ps1 -Action Checks
./deploy/dc-v8.ps1 -Action Build
```

The scripts reject insufficient commit/physical memory before starting, cap workers, preserve owned-process identity and produce new receipts. Source, configuration and contract changes invalidate older receipts; rebuild rather than bypassing validation. A new source export contains no old build or probe receipts.

Build the LAN carrier by repackaging the newly verified ordinary build (no second Unity compilation):

```powershell
. ./deploy/dc-common.ps1
$paths = Get-DcPaths
$receiptPointer = Get-Content -LiteralPath (Join-Path $paths.Project 'docs/game-demo/v8-current-normal-build.json') -Raw | ConvertFrom-Json
./deploy/dc-v8-lan.ps1 -Action Build -VerifiedBuildEvidence $receiptPointer.receipt
```

Use the immutable evidence receipt named by that pointer, not the mutable copy inside `builds`. A fresh independent LAN compile is also supported with `./deploy/dc-v8-lan.ps1 -Action Build`; its complete player files are checked against its own LAN receipt because Unity generates a new build GUID per compile. Both builds still require matching source/configuration fingerprints.

The command reports the complete player-package directory. Send that entire directory to friends; never send `private`, the database, or an isolated launch script. This action creates a package, not a human test result.

## Start and stop

- Local play: run `start-v8.cmd` after the LAN roles have been built.
- Private/LAN/Radmin host: run `start-v8-lan-host.cmd`, provide this computer's exact IPv4 and the exact allowed player addresses including the host.
- Player: run `start-v8-player.cmd` from the complete built player directory and enter the host address.
- Stop players normally, then press Q in the host window. `portfolio.ps1 -Action ServicesStop` stops storage while retaining data.

Only private, loopback or Radmin addresses are supported; the tools do not install VPN software or change firewall rules. The [LAN guide](../game-demo/radmin-human-multiplayer-guide.md) explains TCP12880 and UDP12777. Clients and host must use one version and package.

## Public resources

The public source omits the optional AI decorative lobby background and provider-specific provenance. The client uses an existing legal fallback when that image is absent. All other original resources, resource licenses and Unity meta files are retained. Runtime body/weapon geometry and gameplay numbers are unchanged by the optional background fallback.

The code MIT license does not relicense Unity, fonts or dependencies. Exact binary dependency notices and a new package check are required when releasing a newly built executable.
