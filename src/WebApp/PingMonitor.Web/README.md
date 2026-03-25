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

## Development state-engine test notes

With the development seed enabled, the app creates one agent plus two ICMP assignments:

- `assignment-dev-gateway` for the parent endpoint `endpoint-dev-gateway`
- `assignment-dev-printer` for the child endpoint `endpoint-dev-printer`, which depends on the gateway

Use `POST /api/v1/agent/results` with the seeded agent credentials to post raw results only.

- To drive `UNKNOWN -> UP`, submit consecutive successful results until the assignment reaches its `recoveryThreshold`.
- To drive `UP -> DOWN`, submit consecutive failed results until the assignment reaches its `failureThreshold`.
- To drive child suppression, first drive `assignment-dev-gateway` to `DOWN`, then submit enough failed results for `assignment-dev-printer` to reach its failure threshold; the child should transition to `SUPPRESSED` while the parent remains `DOWN`.

In development, inspect current assignment state and transition history at:

- `GET /internal/dev/state/assignments/assignment-dev-gateway`
- `GET /internal/dev/state/assignments/assignment-dev-printer`

## Operational status page

In normal startup mode, the first operational status page is available at `GET /status`.
The default landing page at `GET /` routes to the same `StatusController.Index` page so the control plane has an operational landing route immediately after startup-gate completion.

It shows the current server-derived state for each `MonitorAssignment`, including endpoint, target, agent instance, state, counters, dependency parents, suppression source, check type, and enabled flags. The page is assignment-scoped, so the same endpoint can appear more than once for different agents. `SUPPRESSED` is derived on the server from dependency evaluation and is never agent-supplied.

## Admin settings page

In normal startup mode, `GET /admin` provides a server-rendered settings page for:

- `SiteUrl` used by generated agent `.env` files as `SERVER_URL`
- default endpoint timing/threshold values used to prefill `GET /endpoints/new`

These values are persisted in the `ApplicationSettings` table and can be updated from `POST /admin`.

## Manage agents page

In normal startup mode, `GET /agents` provides a server-rendered operational page for registered agents.

Per-agent actions include:

- `POST /agents/{id}/enable` to allow future authenticated agent use
- `POST /agents/{id}/disable` to block future authenticated agent use without deleting assignments or history
- `POST /agents/{id}/rotate-package` to rotate the agent API credential and download a fresh package

Credential rotation replaces the stored API key hash and returns a ZIP with a new `.env` file. The new package must be redeployed to the agent host for continued connectivity.
