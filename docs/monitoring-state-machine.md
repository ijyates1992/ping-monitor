# Monitoring state machine

## Overview

This document defines how endpoint state is calculated from raw monitoring results.

It is the authoritative source for:

- endpoint state values
- state transition rules
- threshold behaviour
- dependency suppression rules
- agent-offline handling
- alert-trigger conditions
- recovery-trigger conditions

This document is intentionally explicit. Monitoring state must be deterministic, explainable, and auditable.

---

## Design principles

- Raw results are facts
- State is derived by the server
- Agents do not calculate endpoint state
- Agents do not apply dependency suppression
- Suppression is a first-class state
- State transitions must be deterministic
- Alerting must be based on confirmed state transitions
- Missing data must not be silently treated as healthy
- Agent health and endpoint health must be tracked separately

---

## Core concepts

### Raw check result

A raw check result is a single factual observation submitted by an agent.

Examples:

- ICMP ping succeeded in 3 ms
- ICMP ping timed out

A raw check result is not itself an incident or a state.

---

### Endpoint state

Endpoint state is the server’s interpretation of an endpoint’s status for a given agent assignment.

State is derived from:

- raw results
- thresholds
- dependency relationships
- agent health context

---

### Alert state

Alert state is derived from endpoint state transitions.

---

### Agent health state

Agent health describes whether the agent itself is online.

Agent health is separate from endpoint state.

---

## State model

State is tracked **per assignment (agent + endpoint)**.

### Endpoint states

- `UNKNOWN`
- `UP`
- `DEGRADED`
- `DOWN`
- `SUPPRESSED`

---

## State definitions

### `UNKNOWN`

Insufficient trusted data to determine state.

Typical causes:
- no results yet
- agent offline
- startup

---

### `UP`

Endpoint is reachable and meets all success criteria.

---

### `DEGRADED`

Endpoint is reachable, but quality is below acceptable thresholds.

Examples:
- high latency
- packet loss
- intermittent instability

Important:

- endpoint is still reachable
- this is not a failure state
- this does not imply outage

### v1 behaviour

- `DEGRADED` is actively evaluated when degraded evaluation is enabled.
- Default degraded settings are:
  - baseline lookback: 24 hours
  - current evaluation window: 1 hour
  - packet loss increase threshold: +20 percentage points
  - RTT increase threshold: +20%
  - jitter increase threshold: +20%
  - minimum samples: 10 completed results in both baseline and current windows
- Baseline comparison excludes the current window so current degradation does not dilute its own baseline.
- Jitter comparison uses successful RTT samples only and is ignored when either window has insufficient successful samples.
- `DEGRADED` is non-failure and only applies when the assignment would otherwise be `UP`.

---

### `DOWN`

Endpoint is considered failed based on failure threshold.

---

### `SUPPRESSED`

Endpoint is failing, but failure is attributed to a dependency.

Important:

- this is a real state
- alerts are suppressed
- endpoint may still be unreachable

---

## Assignment-scoped state

State must be calculated per:

- agent
- endpoint

Different agents may observe different states.

---

## Threshold semantics

### Failure threshold

Number of consecutive failures required before `DOWN`.

### Recovery threshold

Number of consecutive successes required before `UP`.

### Counter behaviour

- success resets failure count
- failure resets success count

---

## Initial state behaviour

Initial state is:

- `UNKNOWN`

Transitions:

- success → toward `UP`
- failure → toward `DOWN` or `SUPPRESSED`

---

## Transition rules

### Allowed transitions

- `UNKNOWN → UP`
- `UNKNOWN → DOWN`
- `UNKNOWN → SUPPRESSED`
- `UNKNOWN → DEGRADED` (future use)

- `UP → DOWN`
- `UP → SUPPRESSED`
- `UP → DEGRADED`

- `DEGRADED → UP`
- `DEGRADED → DOWN`
- `DEGRADED → SUPPRESSED`

- `DOWN → UP`
- `DOWN → SUPPRESSED`
- `DOWN → DEGRADED` (future use)

- `SUPPRESSED → UP`
- `SUPPRESSED → DOWN`
- `SUPPRESSED → DEGRADED` (future use)

---

## Transition logic (v1)

### Core evaluation order

1. assignment enabled
2. agent health valid
3. update counters
4. evaluate thresholds
5. evaluate dependency
6. determine base state
7. evaluate degraded only when base state is `UP`
8. determine final state

---

### Pseudologic

```text
if assignment disabled:
    state = UNKNOWN

else if agent not trustworthy:
    state = UNKNOWN

else:
    failureReached = consecutiveFailure >= failureThreshold
    recoveryReached = consecutiveSuccess >= recoveryThreshold
    directParentDown = any(direct parent state == DOWN in same agent scope)

    if recoveryReached:
        state = UP

    else if failureReached and directParentDown:
        state = SUPPRESSED

    else if failureReached:
        state = DOWN

    else:
        state = current state

    if state == UP and degraded evaluation is enabled and recent RTT, packet loss, or jitter exceeds baseline thresholds:
        state = DEGRADED
```

---

## Dependency model

### Rule (v1)

- an endpoint may have zero, one, or many direct parent dependencies
- endpoint is eligible for `SUPPRESSED` only when:
  - failure threshold is reached, and
  - at least one direct parent is currently `DOWN` in the same agent scope
- only direct parents in `DOWN` cause suppression
- direct parents in `SUPPRESSED` do **not** cascade suppression
- suppression is derived by the server only (never by the agent)

---

## Alerting rules

### Alerts open on:

- `→ DOWN`

### Alerts suppressed on:

- `SUPPRESSED`

### Recovery alerts:

- `DOWN → UP`

### v1 degraded behaviour

- `DEGRADED` does NOT trigger alerts by default.
- State transition events are recorded when entering or leaving `DEGRADED`, including the RTT, packet-loss, and/or jitter reason.

---

## Agent health interaction

Agent offline must NOT imply endpoint failure.

### Behaviour:

- stale agent → endpoints remain temporarily
- prolonged outage → endpoints become `UNKNOWN`

---

## Missing data rules

- missing data ≠ success
- missing data ≠ failure

Default:

- `UNKNOWN`

---

## Disabled assignments

Disabled endpoints:

- not monitored
- state becomes `UNKNOWN`
- do not alert
- do not suppress others

---

## Reevaluation triggers

State must be recalculated when:

- new result arrives
- dependency changes
- agent health changes
- assignment changes

---

## Example scenarios

### Normal outage

- fail ×3 → `DOWN`
- success ×2 → `UP`

---

### Dependency suppression

- switch `DOWN`
- printer fails → `SUPPRESSED`
- switch recovers → printer reevaluated

---

### Agent offline

- agent stops reporting
- endpoints → eventually `UNKNOWN`

---

## Invariants

- agents never submit state
- suppression is server-only
- `SUPPRESSED` never alerts
- `DOWN` always respects threshold
- agent offline ≠ endpoint down
- system must be deterministic

---

## Degraded extension rules

Degraded remains:

- non-failure
- policy-driven
- optionally alertable
- lower priority than `UNKNOWN`, `SUPPRESSED`, and `DOWN`

---

## Summary

**Agents submit facts. The server derives state.**

- `UP` = healthy
- `DEGRADED` = reachable but poor quality
- `DOWN` = confirmed failure
- `SUPPRESSED` = dependency-caused failure
- `UNKNOWN` = insufficient data

The system must remain predictable, auditable, and explainable at all times.

---

## v1 uptime interpretation note (read model)

- Operational uptime summaries use assignment-scoped state history over a rolling 24-hour window.
- Time in `UP` and `DEGRADED` is counted as up-time.
- Time in `DOWN`, `SUPPRESSED`, and `UNKNOWN` is not counted as up-time.
- This note defines read-side reporting semantics only; it does not change transition rules above.
