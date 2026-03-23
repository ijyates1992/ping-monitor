# PLATFORM_CONSTRAINTS

## Overview

This document defines the non-negotiable constraints of the platform.

These constraints are **authoritative**.

They exist to ensure:

- architectural consistency  
- predictable behaviour  
- long-term maintainability  
- safe operation in real environments  

No implementation may violate these constraints unless:

1. a new GitHub issue is created explicitly proposing the change  
2. this document is updated accordingly  

---

## Runtime constraints

### Web application

- The control-plane web application must target **.NET 10**
- Changes to the web application runtime require an explicit issue and an update to this document

### Agent

- The execution-plane agent must be implemented in **Python**
- Changes to the agent runtime require an explicit issue and an update to this document

---

## Network model

### Inbound connectivity

- The system must require **only HTTPS (TCP 443)** inbound to the web application
- No other inbound ports are allowed by default

### Agent connectivity

- Agents must operate using **outbound-only connections**
- Agents must initiate all communication with the server
- The server must never initiate direct connections to agents

### Disallowed

- No inbound ports on agents  
- No WebSocket or long-lived inbound connections  
- No direct server-to-agent RPC  

---

## Architecture model

### Control plane vs execution plane

- The web application is the **control plane**
- Agents are the **execution plane**

### Responsibilities

#### Web application

- authentication and authorization  
- configuration management  
- result ingestion  
- state calculation  
- dependency evaluation  
- alert generation  
- uptime tracking  

#### Agent

- execute checks (e.g. ICMP)  
- schedule checks locally  
- submit raw results  
- send heartbeat  

### Critical rule

**Agents collect facts. The server decides meaning.**

---

## Agent constraints

### Implementation

- Agent must be implemented in **Python**
- Agent must be lightweight and self-contained
- Agent must run without requiring inbound network access

### Configuration

- Agent must be configured via environment variables (`.env`)
- Required:
  - `SERVER_URL`
  - `INSTANCE_ID`
  - `API_KEY`

### Behaviour

Agents must NOT:

- calculate endpoint state  
- apply dependency suppression  
- generate alerts  
- modify monitoring configuration locally  

---

## API constraints

### Protocol

- All communication must use **HTTPS**
- JSON request/response format only

### Authentication

- Every request must include:
  - `X-Instance-Id`
  - `Authorization: Bearer <API_KEY>`

### Versioning

- API must be versioned (`/api/v1/...`)
- Breaking changes require a new version

---

## Monitoring constraints

### Check types

- v1 supports **ICMP only**
- Additional check types must not alter core behaviour

### Scheduling

- Agents own scheduling of checks
- Server provides timing configuration
- Server must not directly trigger checks

---

## State model constraints

State behaviour is defined in:

- `docs/monitoring-state-machine.md`

The implementation must follow it exactly.

### Critical rules

- State is derived only on the server  
- Suppression is a real state  
- State is per **agent assignment**, not global  

### Disallowed

- Agent-side state calculation  
- Agent-side suppression  
- Implicit state transitions  

---

## Dependency constraints

- Dependencies must form a **directed acyclic graph (no cycles)**
- Only direct parent in `DOWN` state suppresses child
- Suppression must be evaluated server-side only

---

## Data constraints

### Storage

- MySQL is the only supported database engine in all environments
- The web application must not introduce SQLite, in-memory, or alternate relational database paths without an explicit issue and a matching update to this document
- Raw monitoring results must be stored
- State transitions must be auditable

### Integrity

- Data must not be silently dropped  
- Partial failures must be recorded  
- Historical data must be append-only where possible  

---

## Alerting constraints

- Alerts must be generated **only by the server**
- Alerts must be driven by **state transitions**
- Suppressed endpoints must not generate alerts

### Disallowed

- Agent-side alerting  
- Alert generation from raw results without state evaluation  

---

## Agent identity and security

- Each agent must have a unique `INSTANCE_ID`
- API keys must be:
  - generated server-side  
  - securely random  
  - stored hashed  
  - revocable  

### Disallowed

- IP-based trust  
- shared API keys across agents  
- plaintext credential storage  

---

## Offline behaviour constraints

### Agent offline

- Agent offline must NOT imply endpoint failure
- Endpoint state must not automatically become `DOWN`

### Missing data

- Missing data must default to `UNKNOWN`, not `UP` or `DOWN`

---

## Implementation constraints

### Simplicity

- Prefer explicit, simple implementations  
- Avoid premature abstraction  
- Do not introduce complexity for hypothetical future use  

### Determinism

- Given the same inputs, the system must produce the same outputs  

### Observability

- All significant actions must be logged  
- Silent failure is not acceptable  

---

## Non-goals (v1)

The following are explicitly out of scope:

- server-initiated agent communication  
- inbound agent APIs  
- auto-remediation  
- SNMP-based discovery  
- topology inference  
- distributed consensus between agents  
- machine learning-based state evaluation  

---

## Future extensibility rules

Future features must not violate existing constraints.

Examples:

- adding HTTP checks must not move state logic into the agent  
- adding diagrams must not alter dependency logic  
- adding degradation must not redefine `DOWN`  

---

## Summary

This platform is intentionally simple and strict:

- agents execute  
- server interprets  
- state is deterministic  
- alerts are controlled  
- network exposure is minimal  

These constraints exist to keep the system reliable, predictable, and maintainable as it grows.
