# Network Monitoring Web App

A lightweight, agent-based network monitoring platform with a .NET 10 control plane and Python execution agents.

This system focuses on **reliable uptime monitoring**, **clean alerting**, and **predictable behaviour**, without the complexity of a full NMS.

---

## Overview

This project provides:

- Central web application (control plane)
- Distributed agents (execution plane)
- ICMP-based monitoring (initially)
- Dependency-aware alerting (prevents alert storms)
- Uptime tracking and historical data
- Mobile-friendly dashboard

The design prioritises:

- correctness over cleverness  
- visibility over automation  
- simplicity over feature bloat  

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

### Configuration Backup and Restore

- Admin backup page supports server-side JSON configuration backups.
- Restore is explicit and preview-first from existing server-side backup files.
- Restore supports:
  - **merge mode** (insert missing + update matching; no deletes)
  - **replace mode** (destructive, section-scoped delete + restore; requires typing `REPLACE`)
- Replace mode, operational data restore, and backup upload are intentionally not implemented yet.
- Logs are treated as first-class outputs

---

## What This Is NOT

- Not a full network management system (NMS)
- Not an auto-remediation platform
- Not SNMP-heavy or topology-driven (yet)
- Not a replacement for enterprise tools like PRTG, Zabbix, etc.

This is intentionally focused on:
> **“Is it up, and if not, why?”**

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

---

## License

TBD


## Access roles (v1)

- `Admin`: full read/write access to control-plane pages and actions.
- `User`: read-only access; visible endpoints are the union of explicit endpoint grants and endpoints in explicitly granted groups.
- Visibility enforcement is server-side in query/service logic (not UI-only hiding).
