# Performance metrics rollup (24h assignment summary)

## Why this exists

The status page previously recomputed 24-hour metrics by scanning raw `CheckResults` and `StateTransitions` on each page load. That caused repeated high I/O and slow status-page responses as history volume grew.

This document defines the persisted 24-hour rollup used by the status page.

## Source of truth

Raw history remains the source of truth:

- `CheckResults` remains the immutable raw check record.
- `StateTransitions` remains the auditable state-transition history.

`AssignmentMetrics24h` is a persisted read model optimized for status-page summaries.

## Rolling summary model

Per assignment (`AssignmentId` PK), `AssignmentMetrics24h` stores:

- `WindowStartUtc`
- `WindowEndUtc`
- `UptimeSeconds` (UP + DEGRADED)
- `DowntimeSeconds` (DOWN)
- `UnknownSeconds` (UNKNOWN)
- `SuppressedSeconds` (SUPPRESSED)
- `LastRttMs` (latest successful RTT in-window)
- `LastSuccessfulCheckUtc`
- `UpdatedAtUtc` (summary freshness)

## Update strategy

`AssignmentMetrics24hService` updates summaries in the authoritative server-side processing path:

- after assignment state evaluation completes, the evaluated assignment summary is recomputed and persisted
- dependency-triggered child reevaluation also recomputes child summaries (same evaluation flow)
- duplicate or invalid result batches do not mutate summaries, because ingestion exits before evaluation in those cases

Status-page reads use persisted rows from `AssignmentMetrics24h`, not 24-hour raw-history scans.

If a row is missing, the status query path triggers a targeted rebuild for that assignment and then reads the persisted row.

## Rebuild / backfill

A deterministic rebuild path is provided:

- targeted rebuild for one assignment
- full rebuild for all assignments

Rebuilds recompute from raw history (`CheckResults`, `StateTransitions`, `EndpointStates`) and overwrite persisted rollup rows.

## First-implementation limitations

- RTT rollup currently persists last successful RTT only (no persisted 24h avg/high/low/jitter in this phase).
- Window is fixed to the last 24 hours.
- Summary writes are recompute-per-affected-assignment (explicit and correct-first), not a global background aggregation pipeline.
