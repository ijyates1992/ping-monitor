# Result buffering for raw monitoring ingestion

## Purpose

High-frequency agent result ingestion can generate many small `CheckResults` writes.  
To reduce MySQL write amplification, the web app buffers accepted raw result rows in memory and flushes them in explicit batches.

## Scope boundary (authoritative)

Buffered path applies **only** to raw agent-ingested `CheckResults` rows accepted through `/api/v1/agent/results`.

The following remain direct writes and are **not buffered**:

- state transition rows
- 24h summary/rollup rows (`AssignmentMetrics24h`)
- event logs
- security/auth logs
- admin/operator actions
- settings/profile changes
- backup/restore actions

## Durability model

Buffering is intentionally **best-effort** for this telemetry path:

- accepted raw rows are first queued in memory
- unsafe process termination can lose queued-but-unflushed raw rows
- this tradeoff is accepted for this high-frequency path to improve DB efficiency

Idempotency protection is preserved by persisting the accepted `ResultBatches` marker before enqueue, so duplicate batch replay is rejected even while raw rows are still buffered.

## Flush policy

- Primary trigger: flush when queue depth reaches configured batch size
- Fallback trigger: periodic flush on long interval (default 60 seconds)
- Drain behavior: when multiple batches are pending, flush in bounded chunks until thresholds are satisfied

## Overflow policy

Queue is bounded (`ResultBufferMaxQueueSize`) to avoid unbounded memory growth.

When full, the oldest buffered raw rows are dropped first, newer telemetry is preserved, and a warning is logged with dropped-count visibility.

## Shutdown behavior

On graceful shutdown, the background flusher performs a best-effort final drain of buffered rows without hanging shutdown indefinitely.

## State evaluation and rollup updates

After each batch is successfully persisted to `CheckResults`, affected assignments are evaluated through the normal server-side state evaluation flow.

That same flow refreshes `AssignmentMetrics24h`, so rollups remain consistent with persisted raw history and state transitions.
