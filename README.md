# Network Monitoring Web App

A lightweight, agent-based network monitoring platform built with .NET.

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

- Lightweight .NET worker services
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

Defines parent/child relationships between endpoints.

Used to suppress alerts when a root device fails.

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
- Child endpoints never alert if a parent is down
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

## Tech Stack (Planned)

- .NET (ASP.NET Core + Worker Services)
- REST API (agent communication)
- MySQL or PostgreSQL (production)
- SQLite (optional for development)
- Razor / Blazor (UI)

---

## Security Model

- Agents authenticate with API keys
- All communication over HTTPS
- No trust based on IP address
- Secrets stored via environment variables

---

## Getting Started (Planned)

> Initial setup instructions will be added as the project structure is created.

Expected flow:

1. Run web application
2. Create agent record
3. Install and register agent
4. Create endpoints
5. Assign endpoints to agent
6. Configure dependencies
7. Start monitoring

---

## Development Workflow

- All changes must be linked to GitHub issues
- Follow rules defined in `AGENTS.md`
- Do not introduce behaviour changes without explicit instruction
- Prefer clarity over abstraction

---

## Status

🚧 Early development / design phase

Core architecture is being defined before implementation begins.

---

## License

TBD
