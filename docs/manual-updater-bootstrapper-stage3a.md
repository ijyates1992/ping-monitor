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

## Current launch preference (updater polish pass)

When update apply is initiated from the web app:

1. the updater first attempts to extract and launch the bootstrapper script from the staged release package (`app/Updater/run-staged-update-bootstrapper.ps1` by default)
2. if that staged extraction cannot be used, the updater falls back to the installed bootstrapper path configured in app settings

Apply remains explicit/manual and is blocked when PowerShell 7 (`pwsh`) is unavailable.

## Input contract

The script reads Stage 2 metadata from `-StagedMetadataPath` and accepts explicit operator-supplied runtime targets.

Required parameters:
- `-StagedMetadataPath` (typically `App_Data/Updater/state/staged-update.json`)
- `-InstallRootPath` (IIS physical path for deployed app payload)

Optional parameters:
- `-SiteName` (IIS website name)
- `-AppPoolName` (IIS application pool name)
- `-ExpectedReleaseTag` (safety assertion)
- `-StatusJsonPath` (optional; runtime-safe location is enforced if this path is under install root)
- `-LogPath` (optional; runtime-safe location is enforced if this path is under install root)
- `-PreserveRelativePaths` (defaults to `App_Data`, `appsettings.json`, `appsettings.*.json`; runtime-critical Startup Gate/data paths are always preserved)
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

Additionally, these runtime-owned paths are always preserved even if the caller passes a narrower custom `-PreserveRelativePaths` list:
- `App_Data/StartupGate/**` (includes Startup Gate DB configuration files such as `dbsettings.json` and `dbpassword.bin`)
- `App_Data/Backups/**`
- `App_Data/DbBackups/**`
- `App_Data/Updater/**`

### Preserve / replace / merge categories

The updater now treats files in explicit ownership categories:

1. **Runtime/environment-owned (preserve)**  
   Local operational state and credentials/configuration persisted by the running app.

2. **Release-owned (replace)**  
   Binaries/static payload files from the staged `app/` release folder.

3. **Mixed-ownership (merge)**  
   `appsettings.json` is preserved for environment values, then patched with release-owned `ApplicationMetadata.Version` from the staged package so About/Startup Gate version reflects the newly installed release.

The replacement process:
1. Extract ZIP to temp path.
2. Detect payload root using one of the supported layouts:
   - `<extract-root>/app`
   - `<extract-root>/<single-package-folder>/app`
3. Validate detected package structure (`app/` required, `manifest.json` logged if present).
4. Remove stale files/folders from install root **excluding preserved paths**.
5. Copy payload files into install root **excluding preserved paths**.
6. Patch preserved `appsettings.json` with `ApplicationMetadata.Version` from staged release `appsettings.json`.

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

By default the bootstrapper evaluates configured output paths against `InstallRootPath`:
- if a configured/default output path is **outside** install root, it is used directly
- if a configured/default output path is **inside** install root (replaceable during swap), runtime writes are redirected to a per-run temp session folder under `%TEMP%`

Runtime artifacts:
- `external-updater.log`
- `external-updater-status.json`

When runtime redirection is active, final status is mirrored back to the originally requested status path after execution (best effort).

`external-updater-status.json` includes:
- start/completion timestamps
- current stage
- release tag, zip path, install path
- stage timeline entries
- final success/failure + error details
