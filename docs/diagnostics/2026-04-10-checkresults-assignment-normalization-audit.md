# CheckResults `AgentId` / `EndpointId` Removal Audit and Design Review (Planning Only)

Date: 2026-04-10  
Scope: repository-wide impact audit for planned `CheckResults` storage normalization (`AssignmentId` retained; `AgentId` + `EndpointId` removed from `CheckResults` rows).

---

## 1) Executive summary

This audit confirms that `CheckResults.AgentId` and `CheckResults.EndpointId` are currently redundant denormalized columns for persisted raw result rows: `MonitorAssignments` already provides a stable `(AssignmentId -> AgentId, EndpointId)` mapping.

Key findings:

- **Write path currently writes all three identifiers** (`AssignmentId`, `AgentId`, `EndpointId`) in both direct and buffered ingestion paths.
- **Read/query paths over `CheckResults` are already assignment-centric** (almost all runtime queries filter by `AssignmentId` + time window).
- **No hot-path query currently filters `CheckResults` directly by `EndpointId` or `AgentId`.**
- The current `CheckResults` index `(AgentId, BatchId)` appears **non-essential for active read paths** and overlaps with `ResultBatches`-based idempotency.
- A safe migration should be **two-phase** (compatibility first, then schema drop) to protect startup-gate and live-system safety.

Recommendation: proceed with the refactor, but do it in a staged migration with explicit schema-version gating and MySQL-validated query plans.

---

## 2) Audit method and evidence sources

Performed repo-wide searches and direct file inspections for:

- `CheckResults`, `CheckResult`, `MonitorAssignments`
- `.AgentId`, `.EndpointId`, `AssignmentId`
- LINQ and raw SQL paths touching `CheckResults`
- startup gate / schema version logic
- buffering + ingestion + metrics + maintenance paths

Primary code/docs inspected:

- `src/WebApp/PingMonitor.Web/Data/PingMonitorDbContext.cs`
- `src/WebApp/PingMonitor.Web/Models/CheckResult.cs`
- `src/WebApp/PingMonitor.Web/Models/MonitorAssignment.cs`
- `src/WebApp/PingMonitor.Web/Services/ResultIngestionService.cs`
- `src/WebApp/PingMonitor.Web/Services/BufferedResults/BufferedResultFlushBackgroundService.cs`
- `src/WebApp/PingMonitor.Web/Services/Metrics/RollingAssignmentWindowStore.cs`
- `src/WebApp/PingMonitor.Web/Services/Endpoints/EndpointPerformanceQueryService.cs`
- `src/WebApp/PingMonitor.Web/Services/DatabaseStatus/DatabaseMaintenanceService.cs`
- `src/WebApp/PingMonitor.Web/Services/StartupGate/StartupSchemaService.cs`
- `docs/data-model.md`, `docs/result-buffering.md`, `docs/db-status-and-maintenance.md`, `docs/startup-gate.md`, `docs/PLATFORM_CONSTRAINTS.md`

---

## 3) A. Schema/model audit

### 3.1 Current `CheckResults` schema (EF model)

Current mapped columns (model):

- `CheckResultId` (PK)
- `AssignmentId` (required, max 64)
- `AgentId` (required, max 64)
- `EndpointId` (required, max 64)
- `CheckedAtUtc`
- `Success`
- `RoundTripMs`
- `ErrorCode` (nullable)
- `ErrorMessage` (nullable)
- `ReceivedAtUtc`
- `BatchId` (required, max 128)

Current configured indexes:

- `IX_CheckResults_AgentId_BatchId` on (`AgentId`, `BatchId`)
- `IX_CheckResults_AssignmentId_CheckedAtUtc` on (`AssignmentId`, `CheckedAtUtc`)

### 3.2 Current `MonitorAssignments` schema (EF model)

Current mapped columns:

- `AssignmentId` (PK)
- `AgentId` (required)
- `EndpointId` (required)
- check config + thresholds + metadata fields

Current relevant index:

- unique index on (`AgentId`, `EndpointId`)

### 3.3 Why `CheckResults.AgentId` and `CheckResults.EndpointId` are redundant

Given `MonitorAssignments.AssignmentId` uniquely identifies a row containing both `AgentId` and `EndpointId`, each `CheckResults` row can derive:

- `AgentId = MonitorAssignments.AgentId`
- `EndpointId = MonitorAssignments.EndpointId`

from `CheckResults.AssignmentId` by join (or pre-resolved assignment lookup).

The current triple-write duplicates static assignment metadata on every telemetry row.

### 3.4 Index obsolescence and candidate replacement indexes

Likely obsolete after column removal:

- `IX_CheckResults_AgentId_BatchId` (cannot exist without `AgentId`; also not used by current read paths)

Likely retained/required:

- `IX_CheckResults_AssignmentId_CheckedAtUtc` (critical for hot-path metrics/performance reads)

Candidate additions (post-refactor; optional, query-driven):

1. **Keep/consider widening assignment-time index for deterministic sort-heavy latest lookups**
   - Candidate: (`AssignmentId`, `CheckedAtUtc`, `ReceivedAtUtc`, `CheckResultId`)
   - Use case: latest-result retrieval currently sorts by those tie-breakers.

2. **No immediate endpoint/agent index on `CheckResults` recommended**
   - Prefer assignment-based filtering for runtime.
   - For occasional agent/endpoint historical queries, join via `MonitorAssignments` first.

Relevant join-path indexes to verify/add on `MonitorAssignments`:

- PK/unique on `AssignmentId` (already key)
- unique (`AgentId`, `EndpointId`) (already present)
- optional non-unique (`EndpointId`, `AssignmentId`) if endpoint-scoped history queries become common
- optional non-unique (`AgentId`, `AssignmentId`) if agent-scoped history queries become common

---

## 4) B. Write-path audit (where `CheckResults` rows are created)

### 4.1 Raw ingestion path (`ResultIngestionService`)

Location: `ResultIngestionService.IngestAsync`

Current behavior:

- Validates assignment ownership (`assignmentId` must belong to authenticated agent).
- Validates payload endpoint matches assignment endpoint (`result.endpointId == assignment.EndpointId`).
- Creates `CheckResult` objects with explicit:
  - `AssignmentId = assignment.AssignmentId`
  - `AgentId = agent.AgentId`
  - `EndpointId = assignment.EndpointId`
- Persists directly (buffer disabled) or enqueues buffered objects (buffer enabled).

Assessment:

- `AgentId`/`EndpointId` are available at write time for convenience/denormalization.
- They are **not required for idempotency**; idempotency marker is persisted in `ResultBatches` keyed by (`AgentId`, `BatchId`).

### 4.2 Buffered flush path

Locations:

- `BufferedCheckResult` currently carries `AgentId`/`EndpointId`
- `BufferedResultFlushBackgroundService.PersistAndEnqueueAssignmentsAsync` maps buffered row -> `CheckResult` and persists

Current behavior:

- Buffered payload includes duplicated static identifiers.
- Flush persists same duplicated columns.

Assessment:

- Buffer schema can be simplified once DB schema is simplified; no independent need for duplicated columns in buffer object.

### 4.3 Manual insert / maintenance paths

No separate manual insert path for `CheckResults` found beyond ingestion and buffered flush.

Maintenance paths (`DatabaseMaintenanceService`) only count/delete by `CheckedAtUtc`; no dependence on `AgentId` or `EndpointId` columns.

### 4.4 Restore/import assumptions

DB backup/restore is SQL-level (`mysqldump` + SQL restore) and schema-version gated at startup.

Implication:

- Any column drop is a schema compatibility change that must align with startup-gate schema versioning.
- Cross-version restore scenarios must be handled intentionally (old backup into new app or vice versa).

---

## 5) C. Read/query-path audit for `CheckResults.AgentId` / `CheckResults.EndpointId`

### 5.1 Direct query usage findings

Repository search found `CheckResults` read queries in:

- `RollingAssignmentWindowStore`
- `EndpointPerformanceQueryService`
- `DatabaseMaintenanceService` (prune count/delete)

All current query filters are assignment/time based or time-only prune based:

- `Where(x => x.AssignmentId == ... )`
- `Where(x => x.CheckedAtUtc < cutoff)`

No current runtime query in repo filters `CheckResults` directly by:

- `x.AgentId`
- `x.EndpointId`

### 5.2 Query impact categories

#### Status page / live views

- No direct status page query uses `CheckResults.AgentId`/`EndpointId`; status views use assignment/state/rollup services.
- Impact: low, provided assignment-centric paths remain intact.

#### Endpoint management / admin pages

- Endpoint performance page reads `CheckResults` by assignment + time.
- Impact: none for removed columns.

#### Metrics / rollups / summaries

- Rolling window hydration and latest-result reads are assignment-centric.
- Impact: none for removed columns; index on (`AssignmentId`, `CheckedAtUtc`) remains critical.

#### State evaluation

- State evaluation reads latest result via rolling store using assignment id.
- Impact: none for removed columns in `CheckResults`.

#### Background services

- Buffered flush persists rows and enqueues assignment ids.
- Impact: write DTO/model updates required; semantics unchanged.

#### DB maintenance / pruning / export / restore

- Prune queries unaffected (time-based).
- Backup/restore compatibility and schema version transitions are high-impact operational concerns.

#### Diagnostics / logging / reports

- No direct `CheckResults` endpoint/agent projections found.
- If future diagnostics require endpoint/agent from raw history rows, they must join assignments.

### 5.3 Affected files (directly relevant to this refactor)

1. Schema/model definitions:
   - `Data/PingMonitorDbContext.cs`
   - `Models/CheckResult.cs`

2. Write paths:
   - `Services/ResultIngestionService.cs`
   - `Services/BufferedResults/BufferedCheckResult.cs`
   - `Services/BufferedResults/BufferedResultFlushBackgroundService.cs`

3. Read/metrics paths (verify compile + query plans):
   - `Services/Metrics/RollingAssignmentWindowStore.cs`
   - `Services/Endpoints/EndpointPerformanceQueryService.cs`
   - `Services/DatabaseStatus/DatabaseMaintenanceService.cs`

4. Startup gate/schema compatibility:
   - `Services/StartupGate/StartupSchemaService.cs`
   - `Options/StartupGateOptions.cs`
   - `appsettings*.json` (`RequiredSchemaVersion`)

5. Documentation requiring update during implementation phase:
   - `docs/data-model.md`
   - `docs/agent-api.md` (if ingestion contract changes are considered)

---

## 6) D. Query redesign recommendations (post-refactor)

### 6.1 General design rule

Prefer assignment-centric querying for hot paths.

- Hot path: `CheckResults` filtered by `AssignmentId` (+ time window)
- Derived `AgentId`/`EndpointId`: resolve from `MonitorAssignments` when needed for display/reporting

### 6.2 Patterns by use case

1. **Single-assignment runtime queries (hot path)**
   - Pattern: direct `CheckResults` filter on assignment + time
   - No join needed
   - Backed by `IX_CheckResults_AssignmentId_CheckedAtUtc`

2. **Endpoint-scoped history (occasional/admin)**
   - Pattern A (recommended): resolve assignment IDs for endpoint once, then query `CheckResults` by `AssignmentId IN (...)`
   - Pattern B: direct join `CheckResults -> MonitorAssignments` and filter on `MonitorAssignments.EndpointId`
   - Choose A for controlled assignment set + caching potential; B for ad-hoc SQL/reporting.

3. **Agent-scoped history (occasional/admin)**
   - Same as endpoint pattern, but filter assignments by `AgentId`.

4. **Cross-assignment reports**
   - Prefer join with explicit projection; ensure indexes on join/filter columns (`MonitorAssignments.AssignmentId` key; optional endpoint/agent composite indexes as needed).

### 6.3 Hot vs occasional query guidance

- **Hot runtime:** avoid joins when assignment id is already known.
- **Occasional admin/reporting:** joins acceptable; optimize only when measured.

---

## 7) E. Performance and risk analysis

### 7.1 Likely performance/storage benefits

1. **Storage reduction per row**
   - Removes two `varchar(64)` columns from very high-volume table.
   - Effective savings include row payload + associated index footprint.

2. **Index-size reduction**
   - Dropping `(AgentId, BatchId)` index on `CheckResults` reduces index storage and write maintenance overhead.

3. **Write-path benefit**
   - Slightly smaller row payload and fewer indexed bytes per insert batch.

### 7.2 Likely overhead/regression risk

1. **Join overhead for agent/endpoint scoped historical queries**
   - Added join or pre-resolution step where previously denormalized columns could have been filtered directly.
   - Current codebase has minimal such usage, so practical impact appears low.

2. **Regression risk if future developers assume direct columns exist**
   - Requires clear data-model and query guidance.

### 7.3 Startup gate / maintenance / compatibility risks

- Startup-gate compatibility relies on required schema version and explicit schema actions.
- If app code and schema are not rolled out in lockstep, ingestion failures are likely.
- Backup restore across versions must be explicitly handled/tested.

### 7.4 Migration risks

- Rolling deployment risk: code expecting dropped columns against old/new schema mismatch.
- Any latent SQL/reporting outside repo (if any external tooling queries `CheckResults.AgentId`/`EndpointId`) will break.

---

## 8) F. Recommended migration strategy

## Recommendation: **two-phase compatibility then schema drop**

A one-step refactor is higher-risk for live systems with startup-gate/schema version controls.

### Phase 1: Compatibility-first application update

- Update application write paths to stop relying on `CheckResult.AgentId`/`EndpointId` in business logic.
- Keep DB columns temporarily.
- Ensure all reads needing agent/endpoint derive via assignments where required.
- Keep runtime behavior unchanged from operator perspective.
- Add regression checks for:
  - ingestion (buffered and non-buffered)
  - state evaluation
  - endpoint performance page
  - prune path

### Phase 2: Schema drop + cleanup

- Drop obsolete `CheckResults` columns (`AgentId`, `EndpointId`) and obsolete indexes.
- Bump required schema version.
- Update startup-gate schema compatibility and validation paths as needed.
- Remove transitional code/DTO properties and update docs.

### Validation checklist (must be explicit in implementation PR)

- MySQL translation checks for all touched LINQ queries.
- Startup gate smoke checks:
  - schema compatibility state transitions
  - operational mode after migration
- Buffered ingestion flush under startup-gate constraints.
- Backup/restore verification with new schema version.

---

## 9) G. Optional follow-on storage opportunities (out of scope)

1. `BatchId` storage strategy review
   - `BatchId` is repeated on every row; evaluate optional surrogate/batch-header design only if write volume justifies complexity.

2. Error text deduplication
   - `ErrorMessage` can be large and repetitive.
   - Consider dictionary/dedupe approach only with clear operational benefit and retention policy alignment.

3. Retention/rollup policy refinement
   - `CheckResults` growth remains high-volume by design.
   - Additional retention tiers (raw vs summarized) may provide larger storage gains than column normalization alone.

---

## 10) Explicit unknowns

- No evidence of external BI/reporting queries in this repo; external consumers may still depend on dropped columns.
- Real production cardinality/selectivity statistics are not available in this audit; final index decisions should be verified with MySQL `EXPLAIN` on representative datasets.

---

## 11) Final recommendation

- **Is this refactor worth doing now?**
  - Yes, if paired with disciplined two-phase migration and schema-version rollout controls.

- **One phase or two?**
  - Strongly recommend **two phases** (compatibility first, schema drop second).

Reasoning:

- High-write telemetry table benefits from narrower rows.
- Existing runtime read paths are already assignment-oriented.
- Two-phase rollout minimizes operational risk under startup-gate and live monitoring constraints.

