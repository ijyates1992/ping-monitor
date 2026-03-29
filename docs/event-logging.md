# Event Logging Specification

## Purpose

The event logging system provides a clear, structured record of meaningful system activity for:

- Operational visibility
- Debugging and troubleshooting
- Historical analysis of endpoint and agent behaviour
- Supporting future alerting and audit features

This is **not** a raw telemetry or metrics system. It is focused on **important state and lifecycle events only**.

---

## Scope

Event logging covers:

- Endpoint state changes and control behaviour
- Agent lifecycle and connectivity events

It does **not** include:

- Raw ping/check results
- High-frequency telemetry data
- Debug-level internal application logs

---

## Design Principles

- **Signal over noise** – only log meaningful events
- **Human-readable first** – messages should be operator-friendly
- **Structured but simple** – avoid overengineering
- **Single unified log** – one event table for all event types
- **Searchable and filterable** – designed for UI-driven queries
- **Safe growth** – extensible without breaking existing data

---

## Event Model

All events are stored in a single table.

### EventLog

| Field              | Type        | Description |
|-------------------|------------|-------------|
| EventLogId        | Guid / PK  | Unique event identifier |
| OccurredAtUtc     | DateTime   | When the event occurred |
| EventCategory     | string     | High-level grouping (see below) |
| EventType         | string     | Specific event type |
| Severity          | string     | Severity level |
| AgentId           | Guid?      | Related agent (if applicable) |
| EndpointId        | Guid?      | Related endpoint (if applicable) |
| AssignmentId      | Guid?      | Related assignment (if applicable) |
| Message           | string     | Human-readable summary |
| DetailsJson       | string?    | Optional structured metadata |

---

## Event Categories

| Category   | Description |
|------------|------------|
| endpoint   | Endpoint state and monitoring behaviour |
| agent      | Agent lifecycle and communication |
| system     | (Reserved for future use) |

---

## Severity Levels

| Severity | Description |
|----------|------------|
| info     | Normal operational events |
| warning  | Unexpected but non-critical conditions |
| error    | Failures or critical issues |

---

## Event Types (Initial Set)

### Endpoint Events (initial)

| Event Type                  | Description |
|----------------------------|------------|
| endpoint_state_changed     | Endpoint changed state (e.g. UP → DOWN) |
| endpoint_suppression_applied | Monitoring suppression applied due to dependency |
| endpoint_suppression_cleared | Suppression lifted |

---

### Agent Events (initial)

| Event Type                  | Description |
|----------------------------|------------|
| agent_authenticated        | Agent successfully authenticated |
| agent_became_online        | Agent transitioned to online state |
| agent_became_stale         | Agent transitioned to stale (missed heartbeat threshold) |
| agent_became_offline       | Agent transitioned to offline (extended heartbeat loss) |
| agent_config_fetched       | Agent fetched configuration (reserved for optional use) |

---

## Message Guidelines

Messages must be:

- Clear and human-readable
- Context-aware (include endpoint/agent name where possible)
- Concise but informative

### Examples

- `Endpoint "Google DNS" changed state from UP to DOWN`
- `Endpoint "DNS Server 2" went down.`
- `Endpoint "DNS Server 2" recovered after 00:09:38 downtime.`
- `Endpoint "API Server" suppressed due to dependency failure`
- `Agent "Warton-Node-1" became stale (no heartbeat for 60s)`
- `Agent "Lab-VM-3" authenticated successfully`

---

## DetailsJson Usage

`DetailsJson` may include structured data such as:

- previous state
- new state
- response time (if relevant)
- reason for suppression
- timing thresholds

This field must:
- remain optional
- not be required for UI rendering
- avoid excessive size or complexity

---

## What is NOT Logged

To prevent noise and database bloat, the following are explicitly excluded:

- Every successful endpoint check
- Routine successful `agent_heartbeat_received` events
- Every retry attempt
- Raw latency/metrics streams
- Debug-level internal application logs

These belong in **metrics or telemetry systems**, not event logs.

---

## UI Integration

## Browser notification integration (phase 1)

The browser notification feed uses event-log records as its source of truth.

Only a small, explicit subset of meaningful events is eligible for browser notification delivery in phase 1:

- endpoint down
- endpoint recovered
- agent became offline
- agent became online

Routine heartbeat and other noisy/non-actionable events are intentionally excluded from browser notification delivery.

### Status Page

- Displays **recent events**
- Fixed-size scrollable panel
- Shows latest N events (e.g. 50–100)
- Sorted newest first

---

### Endpoint History Page

- Full event history for a specific endpoint
- Filterable by:
  - text search
  - event type
  - date range

---

### Agent History Page

- Full event history for a specific agent
- Same filtering capabilities as endpoint history

---

## Query Requirements

The system must support efficient queries by:

- time range
- endpoint
- agent
- event type
- severity

Appropriate indexing should be applied to:

- OccurredAtUtc
- EndpointId
- AgentId

---

## Retention (Future Consideration)

Event log retention is **not enforced in the initial implementation**, but the design should allow for:

- time-based pruning (e.g. keep last 30–90 days)
- size-based limits
- archival/export in the future

---

## Future Enhancements (Out of Scope for Initial Implementation)

- Alerting based on events
- Event streaming (real-time updates)
- Export (CSV/JSON)
- Correlation across agents/endpoints
- System-level audit logging
- Integration with external logging platforms

---

## Summary

The event logging system provides:

- A unified, structured event stream
- High-signal operational visibility
- Strong foundation for future alerting and analytics

It is intentionally **simple, focused, and operator-friendly**.
