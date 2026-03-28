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

## Phase 1 implementation notes

- This phase implements **export only**.
- Restore and upload workflows are intentionally not implemented yet.
- Backup name is required for export.
- Notes are stored in the backup document metadata (`notes`) and displayed in backup listings.
- Backup files are stored server-side under the configured `Backup:StoragePath` location (default `App_Data/Backups`), which is outside public static web content.
- Backup format remains JSON and configuration-only.
