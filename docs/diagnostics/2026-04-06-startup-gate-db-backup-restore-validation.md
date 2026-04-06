# Startup-gate DATABASE backup/restore validation (2026-04-06)

Refs: #380

## Environment

- Date (UTC): 2026-04-06
- Host OS: Ubuntu 24.04.3 LTS
- Repository path: `/workspace/ping-monitor`

## Objective

Validate end-to-end startup-gate recovery workflow using MySQL and the built-in DATABASE maintenance backup tool:

1. install required runtime/tooling (.NET 10, PowerShell, MySQL)
2. build and run web app
3. complete startup-gate setup
4. verify `/` landing page load
5. create DATABASE backup from `/admin/database/maintenance`
6. drop schema
7. restore through startup gate (`/startup-gate/database-backup/restore`)
8. verify startup gate exits and landing page loads

## Commands and outcomes

### 1) Install runtime/tooling

- Installed Microsoft repo and packages:
  - `dotnet-sdk-10.0`
  - `powershell`
  - `mysql-server`
  - `mysql-client`
- Verified:
  - `.NET SDK 10.0.105`
  - `PowerShell 7.6.0`
  - `mysql 8.0.45`
  - `mysqldump 8.0.45`

### 2) MySQL runtime startup in container

`service mysql start` failed in this container because the init script path uses shell behavior that does not complete correctly in this non-systemd environment (`eval: [[: not found`, `export: Illegal option -a`).

Workaround used:

- Start MySQL with `mysqld_safe` directly.
- Remove stale socket lock files from previous failed starts.
- Keep server running and verify with `mysqladmin ... ping`.

### 3) Database bootstrap for app

Executed SQL:

- `CREATE DATABASE pingmonitor;`
- create/grant local DB user `pingmonitor`

Credentials used for local validation:

- host: `127.0.0.1`
- port: `3306`
- database: `pingmonitor`
- username: `pingmonitor`

### 4) Build and run web app

Because repo `global.json` pins `10.0.104` with roll-forward disabled while host SDK installed as `10.0.105`, a temporary local-only edit was used during validation to run build commands, then reverted immediately after testing.

Build result:

- `dotnet restore src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj` ✅
- `dotnet build src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj -c Release` ✅

Run command:

- `dotnet run --no-build -c Release --project src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj`

### 5) Startup-gate setup flow

Using local loopback requests and anti-forgery token flow:

- POST `/startup-gate/database` with MySQL settings ✅
- POST `/startup-gate/schema/apply` ✅
- POST `/startup-gate/admin` (first admin bootstrap) ✅

Observed gate status after setup:

- failing stage: `None`
- schema status: `Compatible`
- admin status: `Present`

### 6) Landing page validation

After login, `GET /` returned the operational status page and rendered normally.

### 7) DATABASE backup creation from maintenance page

From `/admin/database/maintenance`:

- POST `/admin/database/backup/create` with `BackupMode=Full` and confirmation ✅
- Backup created: `db-backup-pingmonitor-20260406-225745.sql`

### 8) Schema drop and startup-gate restore

Database reset:

- `DROP DATABASE pingmonitor;`
- `CREATE DATABASE pingmonitor;`

After drop, app correctly entered startup gate:

- failing stage: `Schema`
- schema status: `Missing`

Restore action:

- POST `/startup-gate/database-backup/restore` with selected file id and confirmation `RESTORE` ✅

Post-restore status:

- failing stage: `None`
- schema status: `Compatible`
- admin status: `Present`

### 9) Final landing page check after restore

`GET /` loaded successfully and rendered status page content.

## Conclusion

The startup-gate restore workflow succeeded end-to-end in this validation run.

No restore failure was reproduced, so no failure-debug issue was opened.
