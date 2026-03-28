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
- Endpoint metadata (tags, attributes if applicable)
- Agent ↔ Endpoint assignments
- Other structural monitoring configuration (if added in future)

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
  "formatVersion": 1,
  "appVersion": "1.0.3",
  "backupName": "prod-before-vlan-work",
  "notes": "Taken before changing core switch and endpoint dependency layout. Known-good production config.",
  "exportedAtUtc": "2026-03-28T09:15:00Z",
  "exportedBy": "admin@example.com",
  "machineName": "SERVER01",
  "sections": {
    "agents": [...],
    "endpoints": [...],
    "assignments": [...],
    "identity": [...]
  }
}
```

## Restore behaviour

- Restore is **configuration-only**.
- Restore must be **previewed before apply**.
- Preview must display backup metadata including `notes` prominently before restore is run.
- Restore is driven from existing **server-side backup files** in the configured `Backup:StoragePath`.
- Restore supports these sections:
  - `agents`
  - `endpoints`
  - `assignments`
  - `identity` (optional and explicit, sensitive)
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

## Historical phase note

- Phase 1 introduced export-only backup creation and listing.
- Restore/upload workflows were intentionally deferred in that phase.
