# Validation run report — 2026-04-06

Refs #365

## Scope executed

- Installed required runtime dependencies in the Linux environment:
  - MySQL 8.0 (`mysql-server`, client tools)
  - .NET SDK 10.0.104
  - PowerShell 7.6.0
- Built and ran the web app.
- Completed startup-gate setup flow (database config, schema apply, initial admin).
- Verified landing page behavior.
- Used DB maintenance backup tool to create a full logical backup.
- Dropped the application schema and attempted startup-gate database restore.

## Environment notes

- `dotnet build` failed with default settings due missing `Microsoft.NETCore.App.Host.ubuntu.24.04-x64` package resolution.
- Build succeeded with explicit RID override: `-p:RuntimeIdentifier=linux-x64`.
- MySQL package post-install service management in this container is constrained by `policy-rc.d`; `mysqld` was started manually for runtime validation.

## Setup procedure outcomes

1. Startup gate opened at `/startup-gate`.
2. Database configuration save succeeded (host `localhost`, db `pingmonitor`, user `pinguser`).
3. Schema apply succeeded.
4. Initial admin creation succeeded.
5. Landing page `/` redirected to login page (`/Account/Login?ReturnUrl=%2F`) as expected once startup gate reached normal mode.

## DB backup outcome

- Full backup creation from `/admin/database/maintenance` succeeded:
  - `db-backup-pingmonitor-20260406-060818.sql`
  - 38,854 bytes

## Drop + startup-gate restore attempt

Steps performed:

1. Dropped schema/database (`DROP DATABASE pingmonitor`) and recreated empty database.
2. Confirmed `/` redirected back to `/startup-gate` (schema failure mode).
3. Attempted restore via `/startup-gate/database-backup/restore` with typed confirmation `RESTORE`.

Observed result:

- Restore endpoint returned HTTP 500 (unhandled exception).
- Exception chain shows event logging write is attempted before restore executes and fails because `EventLogs` table does not exist:
  - `Table 'pingmonitor.EventLogs' doesn't exist`
  - Failure path includes `DatabaseMaintenanceService.RestoreBackupAsync` and `EventLogService.WriteAsync`.

## Findings summary

- ✅ Setup and backup paths work end-to-end.
- ⚠️ Startup-gate restore path does **not** successfully recover from a fully dropped schema in this run.
- ⚠️ Failure mode is an unhandled 500 instead of an operator-safe handled error/result.

## Follow-up

- Filed issue #365 with reproduction and failure details.
