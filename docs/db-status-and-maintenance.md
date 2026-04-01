# DB status and maintenance

## Purpose

`/admin/database` provides operator-facing database and runtime buffering visibility for the control-plane web app.

This page is for safe diagnostics and capacity awareness in live environments.

## Phase 1 scope (read-only)

Phase 1 is visibility-only and does not include maintenance execution.

The page shows:

- authoritative schema version from `AppSchemaInfo`
- database/provider/server identity details (without secrets)
- table inventory and MySQL metadata-backed row/size visibility
- result-buffer runtime status and flush diagnostics

## Initial metrics

The initial implementation exposes:

- schema version
- database name and provider/server metadata
- total table count
- per-table approximate row counts (`information_schema.tables.table_rows`)
- total database size (`data_length + index_length`)
- per-table data/index/total size
- result-buffer configuration and runtime stats:
  - enabled state
  - max batch size
  - flush interval
  - max queue size
  - current queue depth
  - enqueue count
  - flush count
  - persisted result count
  - dropped result count
  - last flush time
  - last flush summary

## Future phases

Destructive or structural maintenance actions (for example optimize/rebuild/prune/repair) are intentionally deferred to future phases and must remain explicitly confirmed and auditable when introduced.
