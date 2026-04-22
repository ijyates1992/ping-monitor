# Manual Updater Bootstrapper (Stage 3a)

This document describes the standalone **external** updater script for Windows/IIS introduced in Stage 3a.

Script path:

- `scripts/run-staged-update-bootstrapper.ps1`

## Scope

Stage 3a supports only external stop/swap/start execution.

Included:
- consume Stage 2 staged metadata (`staged-update.json`)
- validate staged package and install target
- extract staged release zip to a temporary folder
- place site into maintenance mode (`app_offline.htm`)
- stop IIS site/app pool (if provided)
- replace app payload conservatively
- restart IIS site/app pool (if previously stopped)
- write human-readable logs and structured status JSON

Not included:
- in-app launch/invocation wiring
- rollback
- schema migration apply
- scheduled/automatic updates

## Input contract

The script reads Stage 2 metadata from `-StagedMetadataPath` and accepts explicit operator-supplied runtime targets.

Required parameters:
- `-StagedMetadataPath` (typically `App_Data/Updater/state/staged-update.json`)
- `-InstallRootPath` (IIS physical path for deployed app payload)

Optional parameters:
- `-SiteName` (IIS website name)
- `-AppPoolName` (IIS application pool name)
- `-ExpectedReleaseTag` (safety assertion)
- `-StatusJsonPath` (defaults beside staged metadata)
- `-LogPath` (defaults beside staged metadata)
- `-PreserveRelativePaths` (defaults to `App_Data`, `appsettings.json`, `appsettings.*.json`)
- `-DryRun` (validation + extraction + plan reporting without stop/swap/start)

Stage 2 metadata fields required by this script:
- `ReleaseTag`
- `StagedZipPath`
- `ChecksumVerified` (must be `true`)

## Conservative payload replacement policy

The script only deploys from the release ZIP `app/` folder into the provided install root.

By default it preserves these existing paths in install root:
- `App_Data/**`
- `appsettings.json`
- `appsettings.*.json`

The replacement process:
1. Extract ZIP to temp path.
2. Validate extracted `app/` payload root.
3. Remove stale files/folders from install root **excluding preserved paths**.
4. Copy payload files into install root **excluding preserved paths**.

## Startup Gate compatibility

The bootstrapper does not run schema updates and does not bypass startup gate.

After restart, schema/admin/config compatibility remains handled by Startup Gate.

## Example invocations

Dry run:

```powershell
pwsh ./scripts/run-staged-update-bootstrapper.ps1 \
  -StagedMetadataPath "C:\inetpub\PingMonitor\site\App_Data\Updater\state\staged-update.json" \
  -InstallRootPath "C:\inetpub\PingMonitor\site" \
  -SiteName "PingMonitor" \
  -AppPoolName "PingMonitorPool" \
  -ExpectedReleaseTag "V0.5.0" \
  -DryRun
```

Execution:

```powershell
pwsh ./scripts/run-staged-update-bootstrapper.ps1 \
  -StagedMetadataPath "C:\inetpub\PingMonitor\site\App_Data\Updater\state\staged-update.json" \
  -InstallRootPath "C:\inetpub\PingMonitor\site" \
  -SiteName "PingMonitor" \
  -AppPoolName "PingMonitorPool" \
  -ExpectedReleaseTag "V0.5.0"
```

## Output artifacts

By default (unless overridden), outputs are written beside the staged metadata file:
- `external-updater.log`
- `external-updater-status.json`

`external-updater-status.json` includes:
- start/completion timestamps
- current stage
- release tag, zip path, install path
- stage timeline entries
- final success/failure + error details
