# Manual Application Updater (v1) — Design Specification

## Executive summary

This document defines a **manual, operator-driven updater** for the Ping Monitor web application on **Windows + IIS**.

The v1 design intentionally uses a two-part model:

1. **In-app update coordinator** (control-plane orchestration and operator UX)
2. **External updater process** (privileged stop/swap/start operations)

This split is required for safety and operational predictability under IIS. The running ASP.NET Core app must **not** overwrite its own binaries in-place.

The updater will support the following high-level flow:

- check GitHub releases for latest compatible package
- compare installed version to candidate version
- download release zip + checksum
- verify checksum
- stage update plan/artifacts locally
- invoke external updater to stop IIS app/site, swap files, restart
- detect post-restart outcome (normal mode vs Startup Gate)
- show explicit operator-facing status and failure details

The design is conservative and optimized for live-system safety, clear auditability, and Startup Gate compatibility.

---

## Scope and boundaries

### In scope (v1)

- Manual admin-triggered update only
- Windows + IIS-hosted web app focus
- GitHub release discovery/download
- SHA-256 checksum verification (required when checksum is available)
- Staged update plan persisted before cutover
- External updater invocation from app coordinator
- IIS stop/swap/start flow with maintenance/offline guard
- Post-update status reporting including Startup Gate outcome
- Explicit status + operator-readable failure reasons

### Out of scope (v1)

- Automatic apply/install of updates
- Release rings/channels (beta/stable/etc.)
- Full rollback implementation
- Automatic database rollback
- Automatic schema migration apply during update cutover
- Self-overwrite by the running web app process
- Non-IIS/Linux host support (future notes only)
- Non-GitHub package sources / general package managers

### Design constraints carried from current platform docs

- Startup Gate remains authoritative for schema compatibility and bootstrap flow.
- Schema apply remains explicit operator action; not auto-applied during updater cutover.
- Environment-specific local configuration and persisted app data must be preserved.
- Operational behavior must be deterministic, explicit, and auditable.

---

## Current-state assumptions from repository

This spec relies on current repository packaging/runtime behavior:

1. **Release package format**
   - Build script expects release tag-like versions `Vx.x.x`.
   - Release archive name format: `PingMonitor-<Version>-<Runtime>.zip` (example `PingMonitor-V0.1.0-win-x64.zip`).
   - Archive root includes:
     - `app/` (deployable app payload)
     - `docs/`
     - `config-samples/`
     - `manifest.json`
   - SHA-256 file is produced (`SHA256.txt`) containing hash + zip filename.

2. **Version display/metadata**
   - Application version is surfaced through application metadata (`ApplicationMetadata.Version`) and exposed in UI (About page).
   - Release packaging injects version metadata into published `appsettings.json`.

3. **Startup Gate semantics**
   - App startup checks include DB config, DB connectivity, schema compatibility, and admin bootstrap.
   - Schema mismatch keeps app in Startup Gate mode.
   - Schema changes are explicit, not automatic.

4. **Persistent local storage under app content root by default**
   - Startup Gate DB settings/password: `App_Data/StartupGate`.
   - Configuration backup storage: `App_Data/Backups`.
   - DB maintenance backup storage: `App_Data/DbBackups`.

These assumptions are foundational for v1 behavior and should be validated during implementation kickoff.

---

## Proposed architecture

## Components

### 1) In-app update coordinator (new web-app module)

Responsibilities:

- authorize update actions (admin-only)
- query pinned GitHub release source
- parse/select candidate release assets
- compare installed vs candidate version
- download zip/checksum to staging area
- verify checksum and package structure
- create and persist an **update session record** + immutable **update plan**
- invoke external updater with plan file path/session id
- poll/read external updater status updates
- expose operator UI/API status timeline and terminal result
- classify post-restart result as:
  - success in normal mode
  - success but Startup Gate required (schema/admin/config gating)
  - failed

Non-responsibilities (v1):

- direct IIS stop/start commands
- in-process file replacement of live app payload
- schema apply/migration orchestration

### 2) External updater process (new Windows-side executable or PowerShell bootstrapper)

Responsibilities:

- run out-of-process from web app worker lifecycle
- read signed/validated update plan from disk
- place application in maintenance/offline state (`app_offline.htm`)
- stop IIS app pool/site according to configured strategy
- perform atomic-ish staged swap of app payload files
- preserve protected/local data paths
- restart app pool/site
- write stepwise status + diagnostics to status file/log for coordinator
- return explicit exit code

Non-responsibilities (v1):

- deciding which GitHub release to install
- downloading from GitHub
- schema migration execution
- rollback orchestration

### 3) Update session state store

Use filesystem-backed session records for v1 (to avoid DB dependency during Startup Gate transitions and during DB outages):

- suggested root: `App_Data/Updater/sessions/<session-id>/`
- artifacts:
  - `session.json` (operator request + immutable plan snapshot)
  - `status.json` (current stage + timestamps + error detail)
  - `logs/updater.log` (append-only textual execution log)
  - `downloads/*.zip`, `downloads/SHA256.txt`
  - `staging/extracted/...`

Rationale:

- survives app restarts
- readable even if DB unavailable/startup-gated
- aligns with Startup Gate isolation expectations

---

## Why external updater is required

The updater must not be designed as in-process self-overwrite due to:

1. **File locking and process lifetime**
   - IIS worker and loaded modules can lock files in deploy directory.
   - Replacing running binaries from same process is unreliable and can leave partial state.

2. **IIS hosting behavior**
   - App recycle/restart can terminate in-app update work mid-flight.
   - Stop/swap/start requires control beyond app request lifetime.

3. **Maintenance safety**
   - `app_offline.htm` + explicit site/app pool stop reduces partial-exposure risk during swap.

4. **Recovery posture**
   - External process can continue and emit status even while app is offline.

The coordinator + external updater split is therefore required for predictable operations.

---

## Release source and package assumptions

## GitHub source pinning

v1 must use explicit, pinned source configuration:

- `owner`: `ijyates1992`
- `repo`: `ping-monitor`
- allowed release type: GitHub Releases only
- default selection: latest non-draft, non-prerelease release (unless operator explicitly selects another allowed release)

Future-proof note: keep configuration as explicit settings, but lock to single known repo in v1.

## Required release assets (v1)

At minimum for `win-x64` install:

- release zip: `PingMonitor-<Version>-win-x64.zip`
- checksum file: `SHA256.txt` (expected to contain line mapping target zip filename to SHA-256)

## Expected package structure validation

Before updater cutover, coordinator validates downloaded zip extracts with:

- top-level `app/` directory exists
- top-level `manifest.json` exists
- (optional but recommended) `manifest.version` matches selected release tag
- extracted `app/` contains expected host artifacts (for IIS deployable payload)

If validation fails, session enters terminal failed state before any IIS operations.

---

## End-to-end v1 update flow

1. **Operator initiates update**
   - Admin opens update page and clicks **Check for updates** / **Install selected version**.

2. **Coordinator fetches release metadata from GitHub**
   - Reads configured owner/repo.
   - Requests releases metadata.
   - Filters unsupported entries (draft/prerelease unless explicitly allowed).

3. **Coordinator determines current + candidate versions**
   - Current from application metadata version.
   - Candidate from selected GitHub release/tag.
   - If equal, show “Already up to date”.

4. **Coordinator resolves target assets**
   - Selects exact runtime zip and checksum asset.
   - Fails if required zip asset missing.

5. **Coordinator downloads assets to session staging**
   - Writes to `App_Data/Updater/sessions/<id>/downloads`.
   - Uses temporary filenames + finalize rename on success.

6. **Coordinator verifies checksum**
   - Parses `SHA256.txt`, finds matching zip entry.
   - Computes SHA-256 of downloaded zip.
   - Must match exactly; otherwise fail hard.

7. **Coordinator validates package layout**
   - Extracts zip to session staging.
   - Confirms required root entries (`app/`, `manifest.json`).
   - Generates immutable update plan (`session.json`) including source hashes and destination path.

8. **Coordinator invokes external updater**
   - Launches updater process with plan path/session id.
   - Immediately marks session as `handoff_started`.

9. **External updater enters maintenance/offline**
   - Writes `app_offline.htm` to site root (maintenance marker).

10. **External updater stops IIS runtime**
   - Preferred sequence (v1 default):
     - stop **site** (traffic gate)
     - stop **app pool** (worker lock release)
   - Record exact command output and timings.

11. **External updater stages replacement**
   - Verifies extracted payload still intact.
   - Uses replace strategy scoped to payload files only (see boundaries section).

12. **External updater replaces app files**
   - Copies extracted `app/` contents into deploy payload root.
   - Preserves excluded persistent paths/files.
   - Uses temp + rename where practical; no delete of protected folders.

13. **External updater restarts IIS**
   - Start app pool, then site.
   - Remove `app_offline.htm` after successful restart trigger.

14. **External updater emits completion status**
   - Writes terminal status and exits with code.

15. **Coordinator/post-update status detection**
   - On app availability, coordinator inspects startup state endpoint/view model:
     - Normal mode => `updated_successfully`
     - Gate mode due schema/admin/config => `updated_requires_startup_gate_action`
   - UI shows explicit actionable next step (for example: “Apply schema update in Startup Gate locally on server”).

---

## IIS-specific operational design

## Stop/start strategy (v1)

Use both site and app pool operations for conservative behavior:

- **Stop site first**: reduces client request exposure during swap.
- **Stop app pool second**: ensures worker shutdown and lock release.
- Perform swap.
- **Start app pool**, then **start site**.

If either stop/start command fails, updater marks terminal failure with exact failed stage.

## `app_offline.htm` usage

- Create `app_offline.htm` before stop/swap to present consistent maintenance response.
- Keep it present until file replacement is complete and restart initiated.
- Remove once restart sequence succeeds.
- If failure occurs before restart completion, leave explicit status and optionally keep offline marker to avoid serving partial state.

## Avoiding partial-update exposure

- Never copy directly from network stream into live root.
- Always download and validate in separate staging directory.
- Replace only after package + checksum validation passes.
- Use explicit stage transitions and abort on first integrity/IO failure.

## Minimal privileges required

External updater runtime identity must have:

- read/write/delete (as needed) on site deploy directory (except forbidden boundaries)
- permission to create/delete `app_offline.htm`
- permission to stop/start designated IIS site and app pool
- read access to updater session plan/staging folders

No broader admin privileges than needed should be granted.

---

## Startup Gate and schema compatibility model

## v1 rule

**Updater replaces app files only.**

Schema compatibility resolution remains in existing Startup Gate flow.

## Why schema logic stays out of external updater (v1)

- Startup Gate already defines deterministic, local-only, explicit schema actions.
- Embedding schema apply in external updater would duplicate operational authority and risk bypassing established safeguards.
- Schema update may require operator timing/validation; auto-run during binary swap is high risk.

## Required updater result states

After restart, report one of:

1. `success_normal_mode` — app started and passed Startup Gate checks.
2. `success_startup_gate_required` — app started but gate is active (for example schema mismatch).
3. `failed_startup` — app did not become reachable/healthy in timeout.

For state (2), UI guidance must explicitly direct operator to local Startup Gate actions and show detected failing stage when available.

This preserves Startup Gate semantics and avoids hidden schema changes.

---

## File replacement and persistence boundaries

## Installation model for v1

Define explicit boundaries to avoid destructive overwrite:

- **Deploy payload root (overwrite target):** current IIS physical path contents corresponding to release `app/` payload.
- **Persistent local data (must preserve):**
  - `App_Data/StartupGate/**`
  - `App_Data/Backups/**`
  - `App_Data/DbBackups/**`
  - any updater session directory under `App_Data/Updater/**`

## Configuration preservation rules

v1 should provide explicit policy (default conservative):

- Preserve existing environment-specific config file(s) by default.
- Install release-shipped config file as `*.new` candidate if conflict is detected.
- Never overwrite secrets-bearing local files without explicit operator intent.

Recommended initial v1 file policy:

- overwrite binaries/static runtime files from package payload
- preserve `App_Data/**` entirely
- preserve local overrides for configuration secrets
- emit conflict list in update session log/status

## Notes for package structure dependence

Current release zip contains top-level `app/`; updater swaps only from that folder into deploy root. `docs/` and `config-samples/` are not required for runtime cutover and should not be mixed into live web root unless explicitly chosen by operator workflow.

---

## Security and trust model

## Authorization

Only authenticated admins with explicit update permission can:

- check releases
- stage downloads
- execute install handoff

Require explicit confirmation before install handoff (typed confirmation recommended).

## Source trust

- Hard pin owner/repo in configuration (v1 default single trusted repo).
- Only consume GitHub release assets from that repo.
- Reject ad-hoc arbitrary URL updates in v1.

## Integrity requirements

- SHA-256 verification is mandatory when checksum asset is present.
- If expected checksum entry missing or parse invalid, fail closed.
- Do not allow “continue anyway” in v1.

## Runtime prerequisite for apply operations

- `pwsh` (PowerShell 7) is a required prerequisite for any updater operation that launches the external bootstrapper.
- Update checks and release staging can continue without `pwsh`.
- Apply/handoff operations must fail closed with an explicit operator-facing message when `pwsh` is unavailable.

---

## Automatic checks and optional auto-stage

v1 supports periodic automatic release checks and optional automatic staging, while keeping install/apply strictly manual.

Configuration knobs:

- `EnableAutomaticUpdateChecks`
- `AutomaticUpdateCheckIntervalMinutes`
- `AutomaticallyDownloadAndStageUpdates`
- `AllowDevBuildAutoStageWithoutVersionComparison`
- `AllowPreviewReleases`

Behavior:

- Automatic checks run on a background schedule only when Startup Gate is in operational mode.
- New applicable release detection writes updater events (change-based, no per-loop spam).
- If `AutomaticallyDownloadAndStageUpdates` is enabled, newly detected releases are downloaded, checksum-verified, and staged automatically.
- DEV builds still discover latest applicable release, but semantic version comparison is skipped; by default auto-stage is suppressed unless `AllowDevBuildAutoStageWithoutVersionComparison` is explicitly enabled.
- Automatic staging is bounded to avoid repeated re-attempt loops for the same unchanged release tag.
- Automatic apply/install is not performed.

---

## Bootstrapper source selection for apply

When applying a staged release:

1. Prefer the bootstrapper script from the staged release ZIP (`app/Updater/run-staged-update-bootstrapper.ps1` by default).
2. Extract it to an updater apply-session temp folder under updater storage.
3. Launch that extracted staged bootstrapper.
4. If staged extraction/use fails in a recoverable way, fall back to the installed bootstrapper path.

The selected source is persisted for operator visibility:

- `staged_release`
- `installed_fallback`

## Secret handling

- If GitHub token is used, store outside source code (env var/secure config).
- Never log token values.
- Status output should redact sensitive headers/credentials.

---

## Failure handling model

Design goal: fail early, fail closed, and provide operator-actionable diagnostics.

## Failure scenarios and expected behavior

1. **GitHub unavailable / API error**
   - Session => failed at `release_lookup`.
   - No local file/IIS changes.

2. **Release assets missing/incomplete**
   - Session => failed at `asset_resolution`.
   - No IIS changes.

3. **Partial download/network interruption**
   - Session => failed at `download`.
   - Cleanup temp files; keep logs.

4. **Checksum mismatch / checksum parse failure**
   - Session => failed at `verification`.
   - No IIS changes.

5. **Zip extract or package validation failure**
   - Session => failed at `staging_validation`.
   - No IIS changes.

6. **External updater launch failure**
   - Session => failed at `handoff`.
   - No IIS changes.

7. **IIS stop failure**
   - Session => failed at `iis_stop`.
   - Leave explicit status and guidance; do not perform swap.

8. **File replacement failure mid-swap**
   - Session => failed at `file_replace`.
   - Preserve detailed file-level error list.
   - v1 has no automatic rollback; operator guidance must be explicit.

9. **IIS restart failure**
   - Session => failed at `iis_start`.
   - Show exact command/stage and recovery steps.

10. **App unreachable after restart timeout**
    - Session => failed at `post_start_validation`.

11. **App starts in Startup Gate mode**
    - Session => `success_startup_gate_required` (not a transport failure).
    - Show required local Startup Gate follow-up.

12. **Updater interrupted (host reboot/process kill)**
    - On next coordinator load, session remains in last known non-terminal stage with `interrupted` marker.
    - Operator can inspect logs and choose manual remediation.

---

## Operator status/UX requirements (v1)

UI need not be polished, but must expose clear operational states.

## Minimum data shown

- installed version
- latest available version (or selected target)
- update session id
- current stage
- stage timestamps
- terminal status (success/failure/gate-required)
- concise error message + expandable diagnostics
- link/path to updater logs

## Core operator states

- `UpToDate`
- `UpdateAvailable`
- `PreparingDownload`
- `Downloading`
- `VerifyingChecksum`
- `Staging`
- `ReadyToInstall`
- `Installing (External Updater Running)`
- `Restarting`
- `Success`
- `SuccessStartupGateRequired`
- `Failed`

## Startup Gate specific presentation

When post-update mode is gate:

- show “Update installed, Startup Gate action required”
- include detected failing stage if available (schema/config/admin)
- direct operator to `/startup-gate` with local-access requirement reminder

---

## Data model (v1 proposal)

Use simple, explicit records serialized as JSON in session folder.

### `UpdateSession`

- `SessionId` (GUID/string)
- `RequestedAtUtc`
- `RequestedByUserId`
- `CurrentVersion`
- `TargetVersion`
- `Source` (owner/repo/release id/tag)
- `Status` (enum)
- `CurrentStage` (enum)
- `ErrorCode` / `ErrorMessage`
- `LastUpdatedAtUtc`

### `UpdatePlan`

- immutable once handoff begins
- `SessionId`
- `DeployRootPath`
- `StagingExtractedAppPath`
- `PreservePathRules[]`
- `ConfigConflictPolicy`
- `ExpectedZipSha256`
- `PackageManifestSnapshot`
- `IisSiteName`
- `IisAppPoolName`
- `TimeoutSettings`

### `UpdateExecutionEvent` (append-only log entries)

- timestamp
- stage
- severity
- message
- optional details object

This model leaves room for future rollback metadata extension.

---

## Future rollback path (not in v1)

Rollback implementation is explicitly **out of scope for v1**.

To keep a clean future path, v1 should still capture:

- prior installed version metadata
- prior package identity/hash (if known)
- full update session timeline
- optional hook points for future pre-swap app directory snapshot
- optional hook points for future pre-update DB backup trigger

Potential future rollback action (v2+):

- select previous successful version/session
- validate retained package/snapshot
- execute inverse stop/swap/start with explicit typed confirmation
- (optional) coordinated DB restore workflow as separate explicit operation

No automatic rollback in v1.

---

## Recommended implementation phases

1. **Phase 0 — Spec finalization (this document)**
   - confirm owner/repo/runtime assumptions and operational policy choices.

2. **Phase 1 — Read-only release check + status surface**
   - add admin update page/API for current vs latest version.
   - no install action yet.

3. **Phase 2 — Download/staging/checksum verification**
   - session store + staged artifact pipeline.
   - package structure validation.

4. **Phase 3 — External updater bootstrapper integration**
   - define update plan contract and handoff mechanism.
   - no destructive swap yet (dry-run logging mode first).

5. **Phase 4 — IIS stop/swap/start implementation**
   - conservative stop/swap/start with maintenance marker.
   - preserve-path policy enforcement.

6. **Phase 5 — Post-start outcome classification**
   - detect normal vs Startup Gate vs failed start.
   - expose operator guidance and diagnostics.

7. **Phase 6 — Hardening + regression checks**
   - deterministic failure-path tests/checklists.
   - interrupted-session handling.

8. **Future Phase (v2+) — Rollback design/implementation**
   - separate issue/spec/implementation sequence.

---

## Open decisions to resolve before implementation begins

1. Exact external updater form for v1:
   - PowerShell script vs small .NET console executable.
2. IIS operation primitive:
   - `Stop-Website/Start-Website` + `Stop-WebAppPool/Start-WebAppPool` vs `appcmd`.
3. Config conflict policy defaults:
   - exact set of files always preserved vs operator prompts.
4. Health/start validation endpoint and timeout values.
5. Whether to persist sessions only on disk or mirror to DB after normal startup.

These should be captured in the implementation issue/PR checklist.

---

## v1 acceptance criteria (design-level)

- Manual admin-triggered update from pinned GitHub releases.
- Candidate package is downloaded, checksum-verified, and staged before cutover.
- External updater performs IIS stop/swap/start; app does not self-overwrite.
- Persistent/local data boundaries are preserved.
- Startup Gate behavior remains authoritative for schema/admin/config actions.
- Operator receives explicit end-state: success, success-with-gate-required, or failure with diagnostics.
- Rollback not implemented, but future path is preserved via session metadata and clear extension points.
