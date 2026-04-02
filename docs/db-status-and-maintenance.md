# DB status and maintenance

## Purpose

Database operations are intentionally split across two admin pages:

- `/admin/database/status` = **DB status** (read-only visibility)
- `/admin/database/maintenance` = **DB maintenance** (operator actions/tools)

`/admin/database` redirects to `/admin/database/maintenance` for compatibility.

## DB status (read-only)

The DB status page shows visibility only and does not include destructive actions.

Sections:

- database overview (provider, database name, host, server version, connection health)
- schema version visibility (`current / required`)
- total DB size and table count
- table details from MySQL `information_schema` (row count is labeled approximate)
- DB/result-buffer runtime status

DB/result-buffer runtime status includes:

- result buffering enabled/disabled
- configured max queue size
- current queue depth
- configured max batch size
- configured flush interval
- last flush completion time (UTC)
- last flush persisted/attempted counts
- dropped-result count
- flush failure indicator/last flush error (if present)

A cache hit-rate metric is **not** shown as a measured value because no cache hit-rate instrumentation currently exists.

## DB maintenance (actions/tools)

The DB maintenance page is action-oriented and keeps all existing maintenance tools:

- prune preview/count
- prune execution (typed confirmation required)
- DB backup creation (`mysqldump` logical export)
- backup file listing/download

DB maintenance intentionally does **not** include full database overview or table-inventory status sections.

## Scope guardrail

Do not move maintenance actions onto DB status.
Do not make DB status destructive.
Do not remove existing maintenance capabilities when refining page structure.
