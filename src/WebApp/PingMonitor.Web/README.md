# PingMonitor.Web startup gate

The web app starts in **startup gate mode** whenever one of the required checks fails:

- MySQL configuration is missing
- the MySQL connection test fails
- the required schema is missing or incompatible
- no admin user exists in the `Admin` role

## Local-only writable actions

Writable setup actions are enabled only for loopback/local requests. The implementation treats a request as local only when `RemoteIpAddress` is loopback (`localhost`, `127.0.0.1`, `::1`) or exactly matches the server's `LocalIpAddress`. Remote requests see diagnostics only.

## MySQL configuration

Use `/startup-gate` locally to save:

- host
- port
- database name
- username
- password

The app builds the connection string internally. Non-secret values are stored in a local JSON file under the configured startup-gate storage directory. On Windows/IIS, the password is protected with DPAPI machine protection before it is stored.

## Schema apply

Schema creation and updates are explicit. The startup gate never auto-applies schema changes during normal startup. Use the **Create / apply required schema** action from a local request.

## First admin bootstrap

If no admin exists, the gate remains active. A local request can create the first admin by supplying username, email, password, and confirmation. The new user is placed into the `Admin` role, and the action is blocked once an admin already exists.

## Remote diagnostics

While the gate is active, remote requests can only view diagnostics. They cannot save database settings, apply schema changes, or create the initial admin.
