# Startup-gate restore failure diagnostic (2026-04-06)

Related issue: https://github.com/ijyates1992/ping-monitor/issues/370

## What was validated
- Installed prerequisites in container: MySQL 8.0, .NET SDK 10.0.104 (matching `global.json`), and PowerShell 7.6.
- Built and ran web app (`dotnet run --no-launch-profile`).
- Completed startup-gate flow:
  - saved DB configuration
  - applied schema
  - created initial admin
- Verified landing page (`/`) loaded to sign-in page (startup-gate no longer active).
- Logged in and created DB backup from `/admin/database/maintenance`.
- Dropped/recreated schema.
- Attempted restore from startup-gate DATABASE restore form.

## Result
- Restore request `POST /startup-gate/database-backup/restore` returned HTTP 500.
- Restore did not complete.

## Root-cause diagnosis
Observed in server logs:
- `DatabaseMaintenanceService.RestoreBackupAsync` starts pre-restore backup.
- Pre-restore backup path writes event logs.
- With schema dropped, table `EventLogs` does not exist.
- Unhandled exception bubbles and returns HTTP 500:
  - `Microsoft.EntityFrameworkCore.DbUpdateException`
  - inner `MySql.Data.MySqlClient.MySqlException: Table 'pingmonitor.EventLogs' doesn't exist`

## Why this happens
Startup-gate restore is a recovery path for missing schema, but the current restore flow assumes normal runtime tables already exist (specifically `EventLogs`) before restore executes.

## Repro essentials
1. Complete startup-gate setup and create full DB backup.
2. `DROP DATABASE pingmonitor; CREATE DATABASE pingmonitor ...;`
3. In `/startup-gate`, restore DATABASE backup with confirmation `RESTORE`.
4. Observe HTTP 500 and stack trace in logs.
