# AGENTS.md – Automation & AI Rules

These instructions apply to the entire repository.

This project manages **live network monitoring infrastructure**.  
Correctness, safety, and predictability take priority over elegance.

---

## Platform constraints (must read)

- Before making any changes related to the web app or agent, read `docs/PLATFORM_CONSTRAINTS.md` and follow its requirements.
- The platform constraints are authoritative; do not violate them without:
  - an explicit new issue, and
  - a corresponding update to `docs/PLATFORM_CONSTRAINTS.md`.

---

## Core Principles

- Do not optimise or refactor behaviour unless explicitly instructed
- Do not change semantics to "improve" UX or flow
- Prefer boring, explicit code over clever abstractions
- Assume changes may affect live systems

---

## Scope & Boundaries

- This platform **does not perform network control or remediation**
- It is strictly **observability and alerting**
- Monitoring logic must remain **check-type agnostic** (ICMP, TCP, HTTP, etc.)
- The web app is the **control plane**, agents are the **execution plane**

---

## Safety Rules (Critical)

- Destructive actions must require **explicit user intent**
- Typing-to-confirm is mandatory for:
  - Deleting endpoints
  - Removing agents
  - Disabling monitoring globally
  - Clearing historical data
- Never weaken safety mechanisms
- Default behaviour must fail-safe

---

## Authentication & Security

- Assume agents may run on untrusted or remote networks
- All agent communication must be over **TLS (HTTPS)**
- Do not introduce public-facing auth flows unless instructed
- Never hard-code secrets or credentials
- Use environment variables or secure storage for all sensitive values

### Agent identity

- Each agent must have:
  - unique `agent_id`
  - authentication credential (API key or equivalent)
- Agent identity must be validated on every request
- Never trust IP address as identity

---

## Mobile-First UI Rules

- Design for phone first, desktop second
- No hover-only controls
- Large tap targets
- Readable at a glance
- If it cannot be safely used one-handed, it is too complex

---

## UI screenshots and visual verification

- Capture UI screenshots for UI-related changes to accelerate review
- Attach screenshots to pull request descriptions or follow-up comments
- Do not commit screenshots or other binary assets by default
- Only commit large binaries when explicitly requested

---

## Observability Rules

- Silent failure is unacceptable
- Every skipped check must have a reason
- Every state transition must be traceable
- Every alert must be auditable
- Logs are more important than dashboards
- When in doubt, add visibility, not automation

---

## Monitoring Model Rules

- The system must distinguish between:
  - **DOWN** (actual failure)
  - **SUPPRESSED** (dependency failure)
  - **UP**
  - **UNKNOWN**
- Suppression must be a **first-class state**, not a UI-only concept
- Dependency logic must:
  - prevent alert storms
  - be deterministic and explainable
- Circular dependencies must never be allowed

---

## Agent Architecture Rules

- Agents execute checks, they do not interpret results
- Agents must:
  - perform checks (e.g. ICMP ping)
  - report raw results
  - maintain heartbeat
- Agents must NOT:
  - generate alerts
  - calculate uptime
  - apply dependency logic

### Scheduling model

- Agents run checks locally based on server-provided configuration
- Server defines configuration, agent executes it
- Agent must tolerate temporary loss of connectivity to server

---

## What NOT to Do

- Do not embed check-specific logic into the platform core
- Do not assume continuous connectivity between agent and server
- Do not auto-resolve or auto-suppress alerts beyond defined rules
- Do not hide dangerous actions behind single clicks
- Do not introduce unnecessary complexity "for later"

---

## Testing Expectations

- Assume live impact
- Test failure scenarios explicitly:
  - endpoint down
  - dependency failure
  - agent offline
- Prefer real-world validation over mocks for monitoring behaviour
- Validate mobile usability for all critical actions

---

## Issue and Pull Request Traceability

- All changes must be associated with at least one GitHub issue
- If no suitable issue exists, create one before starting implementation work
- Apply all relevant labels when creating issues
- PR descriptions must link all relevant issues using:
  - "Fixes #<issue>"
  - "Closes #<issue>"
  - "Resolves #<issue>"
- Every PR must link to a GitHub issue — no exceptions
- Do not open PRs without issue linkage
- Never invent issue numbers

### Issue discovery and pull request linkage

- Scan existing open issues before starting work
- If multiple issues are related:
  - Link all relevant issues in the PR
  - Use:
    - "Fixes"/"Closes" for completed work
    - "Refs" for partial work
- Treat PRs without proper linkage as non-compliant

### GitHub authentication and issue creation

- This repository allows use of GitHub Personal Access Tokens (PATs)
- Codex should attempt to create issues automatically when required
- If issue creation fails:
  - report the failure clearly
  - do not silently skip
  - request user action if needed

---

## Data Integrity Rules

- Monitoring data must never be silently dropped
- Partial failures must be recorded explicitly
- Time-series data must be append-only where possible
- Deletion of monitoring history must require explicit confirmation

---

## Alerting Rules

- Alerts must only trigger on confirmed state transitions
- Alerts must respect:
  - failure thresholds
  - recovery thresholds
  - dependency suppression
- Suppressed endpoints must not generate alerts
- Alert behaviour must be predictable and explainable

---

## Future-Proofing Rules

- Monitoring relationships (dependencies) must be separate from topology/diagram data
- Do not couple monitoring logic to UI representation
- Design models to support:
  - multiple agents per endpoint (future)
  - multiple check types
- Avoid premature abstraction — but do not block future expansion

---

## Related repositories (reference only)

This project may integrate with or be conceptually related to other repositories.

- Do NOT modify external systems from this repository
- External integrations must be handled via APIs or defined contracts
- This project must remain independent and monitoring-focused

---
