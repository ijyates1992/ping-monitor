# Implementation plan

## Phase 1: Skeleton
Create the repository structure, .NET 10 web API shell, Python outbound agent shell, and minimal configuration/docs alignment.

## Phase 2: Auth/config endpoints
Implement authenticated agent hello/config/heartbeat/results handling with persisted agent records and configuration delivery.

## Phase 3: Agent hello/config flow
Add agent startup flow, cached configuration handling, and explicit heartbeat/config refresh behaviour.

## Phase 4: Result ingestion
Persist raw check results, batch idempotency, and auditable ingestion outcomes.

## Phase 5: State engine
Implement server-side assignment-scoped state calculation and suppression rules from the monitoring state machine.

## Phase 6: Alerting
Add server-side alert lifecycle handling driven by state transitions.

## Phase 7: UI
Add the .NET web application control-plane UI without moving monitoring interpretation into the agent.
