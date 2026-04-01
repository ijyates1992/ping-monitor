# DB status and maintenance

## Purpose

`/admin/database` is an admin-only operations page for:

- database status visibility
- deliberate, targeted historical-data pruning
- operator-triggered MySQL backup export

This page is intentionally operational (not a general DB admin console).

## DB status section

The page shows:

- provider/database/server metadata (without secrets)
- schema version visibility
- table inventory and MySQL metadata-backed size/row estimates
- result-buffer runtime diagnostics

## Pruning tools (phase 2 scope)

Pruning is **destructive and irreversible**.

Supported prune targets in this phase:

- `SecurityAuthLogs` older than cutoff
- `EventLogs` older than cutoff
- `CheckResults` older than cutoff
- `StateTransitions` older than cutoff

Not in scope for pruning here:

- users, agents, endpoints, assignments, notification settings
- startup-gate/configuration tables
- backup files/catalog
- active security enforcement records (`SecurityIpBlocks`, Identity lockout state)

Prune workflow:

1. operator selects target + age (days)
2. server calculates UTC cutoff and preview eligible row count
3. operator types `PRUNE`
4. server validates confirmation and executes targeted delete
5. result summary includes deleted row count

## DB backup tool (phase 2 scope)

Backup action is **operator-triggered only**.

Method in this phase:

- MySQL logical SQL export using `mysqldump`
- creates timestamped `.sql` files under server-side `App_Data/DbBackups` by default
- backup path is not publicly served static content
- `DatabaseMaintenance:MySqlDumpExecutablePath` can be set to a full executable path (recommended on Windows hosts where MySQL tools are not on `PATH`)

The page lists created backup files (name, UTC time, size) and supports download.

If required tooling is unavailable or backup fails, the page reports failure honestly with operator-usable diagnostics.

## Auditability and limitations

Maintenance actions are auditable via event logs:

- prune preview requested
- prune started/completed (including counts)
- backup started/completed/failed

Limitations in this phase:

- no in-app DB restore workflow yet
- no retention policy automation for DB backup files yet
- no schema-edit/repair/rebuild actions
