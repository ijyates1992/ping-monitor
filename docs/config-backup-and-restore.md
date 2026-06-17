# Configuration Backup and Restore

## Overview

This document defines the design and behaviour of the configuration backup and restore system for the monitoring platform.

The goal is to provide:

- Safe and reliable **production configuration backups**
- Fast and repeatable **development environment seeding**
- A clear and versioned **portable configuration format**

This feature is strictly limited to **configuration data only**. It is not intended to replace full database backups.

---

## Scope

### Included (Configuration Data)

The following entities are included in configuration backups:

- Agents
- Endpoints
- Groups (including endpoint group memberships)
- Endpoint dependencies
- Agent ↔ Endpoint assignments
- Security settings
- Notification infrastructure settings (SMTP/Telegram/global notification config)
- User notification settings
- Endpoint metadata (tags, attributes if applicable)
- Network diagrams, including diagram canvas/viewport settings, nodes, visual links, link media/speed/LACP metadata, and link VLAN metadata
- AI Assistant provider/settings/tool-limit configuration (`ai_assistant_settings`)

---

### Optional (Explicit Selection Required)

- Identity data (users, roles)

This is optional due to security and environment portability concerns.

---

### Excluded (Operational Data)

The following are **never included**:

- Endpoint check results
- Historical state transitions
- Alert history
- Logs
- Performance metrics
- Any transient or runtime-only state
- AI user memories (`AiUserMemories`) because they are user-owned assistant context rather than portable configuration
- Scheduled AI task rows (`AiScheduledTasks`) because they are user-owned operational schedules
- Event-triggered AI task definitions and run history (`AiEventTriggeredTasks`, `AiEventTriggeredTaskRuns`) because they are user-owned operational automation/reporting state

---

## File Format

### Format Choice

Backups are stored as **JSON**.

Reasons:
- Human-readable
- Easy to diff and version
- Native support in .NET
- Simple to validate and extend

---

### File Structure

Each backup file must be self-describing and versioned.

Example:

```json
{
  "formatVersion": 3,
  "appVersion": "1.0.3",
  "backupName": "prod-before-vlan-work",
  "notes": "Taken before changing core switch and endpoint dependency layout. Known-good production config.",
  "exportedAtUtc": "2026-03-28T09:15:00Z",
  "exportedBy": "admin@example.com",
  "machineName": "SERVER01",
  "sections": {
    "agents": [...],
    "endpoints": [...],
    "groups": {
      "groups": [...],
      "endpointMemberships": [...]
    },
    "dependencies": [...],
    "assignments": [...],
    "security_settings": {...},
    "notification_settings": {...},
    "user_notification_settings": [...],
    "ai_assistant_settings": {...},
    "identity": [...],
    "network_diagrams": {
      "diagrams": [...]
    }
  }
}
```

## Restore behaviour

- Restore is **configuration-only**.
- Restore must be **previewed before apply**.
- Preview must display backup metadata including `notes` prominently before restore is run.
- Restore is driven from existing **server-side backup files** in the configured `Backup:StoragePath`.
- Backup upload accepts JSON configuration backup files and validates them before acceptance.
- Accepted uploaded files are retained in `Backup:StoragePath` and become part of the managed backup set.
- Uploaded files are treated exactly like locally created backup files for preview and restore.
- Restore supports these sections:
  - `agents`
  - `endpoints`
  - `groups`
  - `dependencies`
  - `assignments`
  - `security_settings`
  - `notification_settings`
  - `user_notification_settings`
  - `ai_assistant_settings`
  - `identity` (optional and explicit, sensitive)
  - `network_diagrams`
- Network diagrams are restored as configuration/documentation data only. Diagram restore does not create endpoints, monitor assignments, endpoint dependencies, alerts, or any network-device configuration.
- Monitored endpoint diagram nodes keep matching endpoint IDs when those endpoints exist. When endpoints are restored in the same operation and are remapped by existing endpoint merge logic, monitored diagram node endpoint references are remapped to the restored endpoint IDs.
- If a monitored diagram node references an endpoint that cannot be resolved, restore keeps the diagram node with `EndpointId` cleared and reports a warning. The restore process never creates a monitored endpoint solely because a diagram references it and never attaches a diagram node to an endpoint by name alone.
- Diagram visual links remain documentation only and do not create monitoring dependencies. Links whose source/target nodes cannot be restored are skipped with warnings; VLAN metadata for skipped links is also skipped.
- Restore supports two modes:
  - `merge`
  - `replace`
- Merge mode updates matching records and inserts missing records.
- Merge mode does **not delete existing configuration**.
- Replace mode is destructive for the **selected sections only**:
  - selected sections are deleted using explicit section-specific ordering
  - selected sections are then imported from backup data
  - non-selected sections are not modified
- Replace mode requires exact typed confirmation text: `REPLACE`.
- Replace requests with missing/incorrect confirmation must be rejected.
- Identity replace is not supported in this phase; identity restore remains merge-only and explicit.
- Restore does **not** import operational data (results, state transitions, alerts, logs, metrics, runtime state).
- Replace mode for `network_diagrams` deletes/replaces only diagram tables (`NetworkDiagramLinkVlans`, `NetworkDiagramLinks`, `NetworkDiagramNodes`, `NetworkDiagrams`) and must not delete endpoints, agents, monitor assignments, endpoint dependencies, check results, states, alerts, events, or logs.
- Upload does **not** trigger restore automatically; restore remains an explicit separate operator action.
- Upload supports **JSON only**. Invalid or unsupported files must be rejected and must not be stored.

## Managed backup set note

- The managed backup set includes:
  - locally created backups
  - accepted uploaded JSON configuration backups
- Both sources are listed together and use the same preview and restore paths.


## Backup source classification

Each managed backup is classified with a source value for operational visibility and retention handling:

- `manual` - created explicitly by an operator from the admin backup page
- `uploaded` - accepted uploaded JSON backup file
- `automatic_scheduled` - created by the daily automatic backup schedule
- `automatic_config_change` - created automatically after configuration changes are coalesced

Source classification is part of managed backup metadata and is shown in the admin backup list.

## Delete management

Managed backups can be deleted from the admin UI with explicit confirmation:

- Single-file delete requires typed confirmation text: `DELETE`
- Bulk delete requires typed confirmation text: `DELETE`
- Delete operations are restricted to files in the configured `Backup:StoragePath`
- Path traversal and arbitrary file delete attempts must be rejected

## Automatic backup behaviour

Automatic backups remain configuration-only and use the existing export pipeline.

### Scheduled backups

- Daily scheduled backups are supported using a simple configured local time
- Scheduled backups are created with source `automatic_scheduled`
- Scheduling remains intentionally simple (no cron dependency)

### Configuration-change backups

- Configuration changes in monitored configuration areas can trigger automatic backup creation
- Config-change backups are created with source `automatic_config_change`
- Debounce/coalescing is required so bursts of changes result in one backup for the coalescing window

## Retention behaviour

Retention is safe and predictable by default:

- Retention pruning applies to automatic backups by default
- Manual backups are not silently auto-pruned by default
- Uploaded backups are not silently auto-pruned by default
- Pruning may run after automatic backup creation and/or during maintenance passes
- Max-count pruning for automatic backups is the baseline policy; optional age-based pruning may be added when needed

## Network diagram area/location boxes

Network diagram area/location boxes are included in the `network_diagrams` backup section with their diagram, nodes, visual links, and VLAN link metadata. Restore recreates area boxes as diagram-only visual annotations. Restoring or replacing network diagram data must not create monitoring groups, endpoint dependencies, alerts, endpoints, assignments, or agents, and deleting/replacing diagram data removes only diagram records such as areas, nodes, visual links, VLAN metadata, and diagrams.
