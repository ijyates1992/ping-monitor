# Windows IIS build and setup

This guide covers building, publishing, and hosting the **.NET 10** web application from this repository on **Windows IIS**.

It is intentionally limited to the deployment path supported by this repository:

- Windows Server with IIS
- ASP.NET Core hosted behind IIS
- MySQL as the only supported database engine
- startup gate used for first-run configuration and schema/admin bootstrap

This document does **not** cover Docker, Linux, SQLite, or PostgreSQL deployments.

## 1. Prerequisites on the IIS server

Install and verify the following on the Windows server that will host the site:

1. **IIS** with the role services required for hosting an ASP.NET Core site.
2. **.NET 10 Hosting Bundle** for ASP.NET Core on IIS.
3. Access to a reachable **MySQL** server/database.
4. A local administrator or equivalent account on the IIS server so you can complete the startup gate locally on that machine.

Practical IIS/ASP.NET Core notes:

- The web app should be deployed from **published output**, not directly from the repository source tree.
- If the **.NET 10 Hosting Bundle** was installed **before** IIS was installed, run the Hosting Bundle installer again in repair/reinstall mode after IIS is present so the IIS hosting components are registered correctly.
- IIS hosting for ASP.NET Core depends on the Hosting Bundle/ASP.NET Core Module being installed correctly.

## 2. Build from the repository root

The repository already provides a Windows PowerShell build script. Run it from the repository root:

```powershell
./scripts/build.ps1
```

Useful parameters already supported by the script:

```powershell
./scripts/build.ps1 -Configuration Release
./scripts/build.ps1 -Configuration Release -SkipTests
./scripts/build.ps1 -Configuration Release -SkipPythonChecks
```

What the script does today:

- verifies the pinned .NET SDK from `global.json`
- restores the web project
- builds the web project
- runs .NET tests when test projects exist
- runs lightweight Python syntax checks for the agent unless skipped

The script does **not** publish the site for IIS. Publishing is a separate step.

## 3. Build and publish the web app for IIS

The web application project is:

```text
src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj
```

Build the web app in Release mode:

```powershell
dotnet build ./src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj --configuration Release
```

Publish the web app to a deployment folder:

```powershell
dotnet publish ./src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj --configuration Release --output ./artifacts/publish/PingMonitor.Web
```

Deploy **the published output** from `./artifacts/publish/PingMonitor.Web` to the IIS server. Do **not** point IIS at the raw repository checkout.

## 4. Copy published files to the IIS server

Choose a stable folder on the Windows server for the deployed application, for example:

```text
C:\inetpub\PingMonitor\site
```

Copy the contents of the publish output into that folder.

Recommended approach:

- keep the repository/source code elsewhere or off the server entirely
- copy only the published files used by IIS
- treat the IIS folder as the deployed application root

## 5. Create and configure the IIS site

In IIS Manager:

1. Create a new site for Ping Monitor.
2. Set the **Physical path** to the published application folder, for example `C:\inetpub\PingMonitor\site`.
3. Bind the site to the required hostname/port for your environment.
4. Use HTTPS in line with the repository's platform constraints.

### Application pool

Use a dedicated application pool for the site.

Practical settings:

- **No Managed Code** is the usual IIS setting for ASP.NET Core behind the Hosting Bundle.
- Use the default integrated pipeline mode unless your environment has a specific reason to differ.
- Run the site under a dedicated app pool identity where possible so folder permissions are explicit and predictable.

## 6. Filesystem and permissions

The application stores startup-gate database configuration locally under the startup-gate storage directory.

By default this is configured as:

```text
App_Data/StartupGate
```

Because the default is a relative path, the application resolves it under the web app content root. In a typical IIS deployment this means a path under the published site folder, for example:

```text
C:\inetpub\PingMonitor\site\App_Data\StartupGate
```

Practical requirements:

- the IIS application pool identity must be able to **read and write** the deployed site folder area used for startup-gate storage
- the app pool identity must be able to create `App_Data\StartupGate` if it does not already exist
- if you change the startup-gate storage location through configuration, make sure the IIS identity has equivalent access there

On Windows/IIS, the startup-gate password file is protected with **DPAPI machine protection** when saved.

## 7. Server-local setup actions required by the startup gate

The startup gate is part of the intended deployment flow for this repository.

On first run, the application checks for:

- MySQL configuration
- successful MySQL connectivity
- required schema presence/version
- at least one admin user in the `Admin` role

If any of those checks fail, the app runs in **startup gate mode** instead of normal mode.

### What remote users see

While startup gate mode is active:

- normal application endpoints are unavailable
- GET requests are redirected to `/startup-gate`
- remote users can view diagnostics only
- remote users cannot save configuration, apply schema changes, or create the initial admin user

### What must be done locally on the server

Use **local/loopback access on the IIS server itself** to open the site, for example:

- `http://localhost`
- `http://127.0.0.1`
- `http://[::1]`
- or the local site URL as resolved on the server when IIS still presents the request as local

From that local session, use `/startup-gate` to complete the required setup actions:

1. enter the MySQL settings
2. save the configuration
3. apply the schema explicitly
4. create the initial admin account

This local-only write restriction is intentional and is central to the repository's deployment model.

## 8. MySQL expectations

This application supports **MySQL only**.

Before first run, make sure the target MySQL server is already available and reachable from the IIS host.

The startup gate collects:

- host
- port
- database name
- username
- password

The application constructs the MySQL connection string internally from those values.

This guide does not cover MySQL server installation or hardening beyond ensuring that the database is reachable and the supplied credentials are valid for the target database.

## 9. First-run flow on IIS

A practical first deployment flow is:

1. Install IIS.
2. Install or repair the .NET 10 Hosting Bundle.
3. Build and publish the web app.
4. Copy published files to the IIS site folder.
5. Create the IIS site and application pool.
6. Ensure the app pool identity can write to the startup-gate storage location.
7. Start the site.
8. From the server itself, browse to `/startup-gate` locally.
9. Enter the MySQL connection details.
10. Use the gate to apply the schema.
11. Use the gate to create the first admin user.
12. Confirm the app leaves startup gate mode and loads normally.

## 10. Local configuration/runtime notes

Keep these deployment details in mind:

- `appsettings.json` in the published output includes the startup-gate defaults, including `StartupGate:StorageDirectory = App_Data/StartupGate` and `DefaultMySqlPort = 3306`.
- The startup gate stores non-secret database settings in a local JSON file and stores the password separately.
- The saved password is not shown back through the startup-gate UI after it is stored.
- Schema creation/update is explicit and user-triggered; the app does not auto-apply schema changes during normal production startup.

## 11. Troubleshooting

### App fails to start under IIS

- Confirm IIS is installed.
- Confirm the **.NET 10 Hosting Bundle** is installed on the server.
- If IIS was installed after the Hosting Bundle, repair/re-run the Hosting Bundle installer.

### Landing page times out after startup gate is complete

If startup-gate setup succeeds locally but the public landing page at `https://your-hostname/` hangs or times out after that, verify that the deployed application is running a build that processes forwarded proxy headers before HTTPS redirection.

Why this matters:

- the application is expected to run behind IIS with HTTPS exposed to users
- without forwarded header processing, an app behind a proxy can misread the original request scheme/host
- that can cause incorrect HTTPS redirect behavior after startup-gate completion, which may appear in browser diagnostics as a timeout or an unsafe attempt to reload the deployed URL from `chrome-error://chromewebdata/`

Practical checks:

- confirm IIS is serving the latest published output, not an older publish folder
- recycle the IIS application pool after deploying the updated publish output
- verify the site binding and certificate are correct for the public hostname
- if another reverse proxy sits in front of IIS, make sure it preserves standard forwarded headers

### IIS reports `Failed to gracefully shutdown application "MACHINE/WEBROOT/APPHOST/PING MONITOR"`

This IIS/ANCM message indicates the worker process did not stop inside the configured shutdown window during recycle/stop.

What this repository now does:

- ships an explicit `web.config` with `shutdownTimeLimit="60"` so IIS allows a longer graceful-stop window for hosted background services

Operational checks:

- confirm IIS is serving the latest published output that includes this `web.config`
- recycle the application pool after deployment
- if you still see this message, review background-service logs to identify long-running operations that ignore cancellation

### Site keeps landing on `/startup-gate`

That means one or more startup checks still fail. Review the diagnostics shown by the gate and verify:

- MySQL configuration has been saved
- the MySQL server is reachable from the IIS host
- the credentials are correct
- the schema has been applied
- the initial admin account has been created

### Database connection failure

- Re-check host, port, database name, username, and password entered into the startup gate.
- Verify firewall/routing between the IIS host and MySQL.
- Confirm the MySQL account has access to the target database.

### Schema not applied yet or schema mismatch remains

- Use local access on the server to run the startup-gate schema action.
- Remember schema changes are explicit; they are not applied automatically during startup.

### Initial admin is still missing

- Use local access on the server to create the first admin user.
- The gate will remain active until an admin exists in the `Admin` role.

### Permission errors in the deployed folder

- Verify the IIS application pool identity can read the site folder.
- Verify it can create/write under `App_Data\StartupGate` or your configured startup-gate storage path.

### Logs and diagnostics

- Start with the diagnostics shown on `/startup-gate`, because the gate reports configuration, connectivity, schema, admin, and access-mode status directly.
- If you have IIS/app logging enabled in your environment, review those logs alongside the startup-gate diagnostics for startup failures.
