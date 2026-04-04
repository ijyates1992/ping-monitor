# Network Monitoring Web App

## Why this exists

I built this because I wanted a simple, reliable way to monitor connectivity between multiple systems without relying on third-party services or overcomplicated enterprise tooling.

Most of the existing solutions I looked at were either:
- too heavy and full of features I didn’t need
- too basic and missing useful features
- tied to cloud platforms or subscriptions
- or just not very transparent about what they were actually doing

So like most things, I ended up building my own.

This project is designed to be:
- self-hosted
- easy to understand
- predictable in how it behaves
- and focused on doing one job well — monitoring endpoint connectivity and surfacing useful information about it

---

## A quick note

I’m not a professional software developer — my background is more in systems, electronics, and practical engineering. This project is very much built from a “make it work properly in the real world” mindset rather than trying to be clever or academic.

Because of that:
- the design is intentionally simple where possible
- features are added based on real-world need rather than theory
- clarity and reliability are prioritised over complexity
- the code is written almost entirely with the help of AI tools

---

## What to expect

This is an early preview release.

It appears stable in testing so far, but testing is far from complete and there may still be issues that haven’t been discovered yet. You can expect:
- improvements to the UI
- additional features over time
- occasional rough edges

If you’re happy running something that’s actively being developed and improving, it should already be usable.

---

## What this is (and isn’t)

This is:
- a lightweight, self-hosted network monitoring tool
- designed for visibility and control
- suitable for small to medium deployments

This is not:
- a full enterprise monitoring platform
- a SaaS product
- or something trying to do everything

It’s intentionally focused.

---

## Architecture

### Control Plane (Web App)

- ASP.NET Core application
- Stores configuration, results, and state
- Evaluates endpoint health
- Applies dependency suppression logic
- Generates alerts
- Provides UI and reporting

### Execution Plane (Agents)

- Lightweight Python agents
- Run on remote networks/sites
- Execute checks locally (ICMP ping in v1)
- Report raw results to the server
- Maintain heartbeat

**Important:**  
Agents collect facts. The server decides meaning.

---

## Key Features

### Endpoint Monitoring

- ICMP ping checks
- Configurable intervals and timeouts
- Failure and recovery thresholds
- Per-endpoint alert control
- Selectable built-in endpoint icons for quick visual identification (`generic`, `switch`, `firewall`, `server`, `router`, `printer`, `pc`, `laptop`, `nas`, `access-point`, `camera`, `phone`)
- Icons are operational UI hints only; v1 does not support custom or uploaded icons

### Dependency-Aware Alerting

Prevents alert storms when infrastructure fails.

Example:
- Switch goes down
- All connected devices become **SUPPRESSED**
- Only the switch triggers an alert

States:

- `UP`
- `DOWN`
- `SUPPRESSED`
- `UNKNOWN`

### Agent-Based Monitoring

- Monitor networks from multiple locations
- Works across NAT/VPN boundaries
- No inbound connectivity required for agents
- Scales naturally with additional agents

### Uptime Tracking

- Historical check results
- State transitions tracked over time
- Designed for accurate uptime reporting (SLA-friendly)

### Observability First

- No silent failures
- All actions are traceable
- Status page includes a recent events panel for meaningful endpoint/agent activity
- Endpoint history is available at `/endpoints/{endpointId}/history`
- Agent history is available at `/agents/{agentId}/history`
- Event logging is intentionally focused on meaningful state/activity events (not every raw successful check result)

### Configuration Backup and Restore

- Admin backup page supports server-side JSON configuration backups with source classification (`manual`, `uploaded`, `automatic_scheduled`, `automatic_config_change`).
- Admin backup page supports uploading JSON configuration backups and server-side delete management (single and bulk delete with typed confirmation for bulk).
- Accepted uploads are validated, retained on the server, and added to the managed backup list.
- Automatic configuration backups support a simple daily schedule plus configuration-change coalescing, with retention pruning applied to automatic backups by default.
- Restore is explicit and preview-first from existing server-side backup files.
- Restore supports:
  - **merge mode** (insert missing + update matching; no deletes)
  - **replace mode** (destructive, section-scoped delete + restore; requires typing `REPLACE`)
- Operational data restore is intentionally not implemented.
- Logs are treated as first-class outputs

---

## Core Concepts

### Endpoint

A monitored host (IP or hostname).

### Agent

A remote worker that performs checks from a specific network location.

### Monitor Assignment

Defines:
- which agent checks which endpoint
- how often
- thresholds and behaviour

### Dependency

Defines direct parent/child relationships between endpoints (an endpoint may have multiple direct parents).

Used to suppress alerts when a root device fails.

Suppression rule (server-side only): if an endpoint is failing and at least one direct parent is `DOWN`, the endpoint state becomes `SUPPRESSED`. Direct parents in `SUPPRESSED` do not cascade suppression.

### State

Each endpoint (per agent) has a state:

- UP
- DOWN
- SUPPRESSED (due to dependency)
- UNKNOWN

---

## Dependency Model

- Dependencies form a **directed graph (no cycles)**
- Suppression is evaluated per-agent
- Child endpoints are eligible for suppression when at least one direct parent is `DOWN` in the same agent scope
- Only direct parents in `DOWN` suppress; `SUPPRESSED` does not cascade
- Suppression is a **real state**, not just UI logic

---

## Project Goals

### Short Term (v1)

- ICMP monitoring via agents
- Endpoint + dependency management
- Basic dashboard
- Alerting (email / webhook / Telegram)
- Uptime tracking

### Medium Term

- Multiple check types (TCP, HTTP, DNS)
- Tagging and grouping
- Improved reporting
- Multi-agent support per endpoint

### Long Term

- Network diagrams / topology view
- Correlated alerts across agents
- Advanced alert routing
- SNMP integration (optional)

---

## Design Principles

- Fail-safe by default
- No hidden behaviour
- No implicit assumptions
- Explicit state transitions
- Deterministic alerting
- Separation of concerns:
  - agent = execution
  - server = logic

---

## Tech Stack

- .NET 10 (ASP.NET Core web application)
- Python agents
- REST API (agent communication)
- MySQL (used in all environments)
- Razor / Blazor (UI)

---

## Security Model

- Agents authenticate with API keys
- All communication over HTTPS
- No trust based on IP address
- Secrets stored via environment variables

---

## Getting Started

> Initial setup instructions are still being expanded as the platform foundation is completed.

Expected flow:

1. Run web application
2. Create agent record
3. Install and register agent
4. Create endpoints
5. Assign endpoints to agent
6. Configure dependencies
7. Start monitoring

---

## Build Script

For a repository-root build on Windows, run the PowerShell script from the repo root:

```powershell
./scripts/build.ps1
```

Supported parameters:

- `-Configuration Debug|Release`
- `-SkipTests`
- `-SkipPythonChecks`

---

## Additional documentation

- Build and release packaging guide: `docs/build-and-release.md`
- Notification setup guide (browser, SMTP, Telegram): `docs/notifications-setup.md`

The script restores and builds the main .NET 10 web project, runs .NET tests when test projects exist, and performs a lightweight Python syntax check for the agent. It does **not** publish or deploy the application.

For the supported Windows/IIS deployment flow, including publish output and startup-gate first-run setup, see `docs/iis-build-and-setup.md`.

## Development Workflow

- All changes must be linked to GitHub issues
- Follow rules defined in `AGENTS.md`
- Do not introduce behaviour changes without explicit instruction
- Prefer clarity over abstraction

---

## Status

🚧 Foundational implementation in progress

Startup gate and agent API endpoints are in place, with MySQL used in all environments. The core control-plane and execution-plane foundation exists, while the server-side state engine and alert lifecycle remain to be completed.

## Access roles (v1)

- `Admin`: full read/write access to control-plane pages and actions.
- `User`: read-only access; visible endpoints are the union of explicit endpoint grants and endpoints in explicitly granted groups.
- Visibility enforcement is server-side in query/service logic (not UI-only hiding).

---

## License

This project is licensed under the GNU Affero General Public License v3 (AGPLv3).
You are free to use, modify, and self-host this software. If you modify the software and make it available over a network (e.g. as a hosted service), you must also make your source code available under the same license.
