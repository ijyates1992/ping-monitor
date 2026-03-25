# Implementation plan

## Current status

The platform foundation is in place:

- startup gate exists for MySQL-backed application startup
- authenticated agent API endpoints exist for hello/config/heartbeat/results
- result ingestion and core control-plane/execution-plane wiring exist
- server-side state engine and alert lifecycle are still pending

---

## Phase 1: Foundation
Repository structure, .NET 10 web application shell, Python outbound agent shell, and core configuration/docs alignment are in place.

## Phase 2: Auth/config endpoints
Authenticated agent hello/config/heartbeat/results handling with persisted agent records and configuration delivery is in place.

## Phase 3: Agent hello/config flow
Agent startup flow, cached configuration handling, and explicit heartbeat/config refresh behaviour are in place.

## Phase 4: Result ingestion
Raw check-result persistence, batch idempotency, and auditable ingestion outcomes are in place.

## Phase 5: State engine
Implement server-side assignment-scoped state calculation and suppression rules from the monitoring state machine, including multi-dependency evaluation using direct parent `DOWN` status only.

## Phase 6: Alerting
Add server-side alert lifecycle handling driven by state transitions.

## Phase 7: UI
Continue the .NET web application control-plane UI without moving monitoring interpretation into the agent.
