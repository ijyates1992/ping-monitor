# State Evaluation DB Read Regression

Date: 2026-05-29
Issue: #480

## Root Cause

`StateEvaluationService.EvaluateDegradedAsync` queried `CheckResults` directly for the full degraded baseline lookback window every time an assignment evaluated to base `UP`.

Because normal result ingestion enqueues assignment state evaluation repeatedly, this made degraded analysis perform repeated reads over the high-volume raw result table. The query was assignment-scoped and indexed, but it still ran continuously in the hot evaluation path and scaled with the number of samples in the configured lookback window.

## Affected Code Paths

- Up/down state evaluation remained counter-based and used the current state cache plus the rolling latest-result cache.
- Dependency suppression used topology/current-state caches and did not scan dependency or result history per endpoint.
- Unknown-on-agent-offline used `Agent.LastSeenUtc` / `Agent.LastHeartbeatUtc` plus assignment/state metadata, not heartbeat-history scans.
- Status/dashboard summaries used `AssignmentMetrics24h` summary rows and did not trigger state re-evaluation.
- Status and endpoint management pages could trigger missing-summary backfills, which could hydrate rolling metrics from `CheckResults` during page refresh.
- Degraded RTT, packet-loss, and jitter analysis was the regression path because it read `CheckResults` directly inside state evaluation.

Large-table reads that remain are outside continuous state evaluation:

- raw result writes during ingestion;
- rolling-cache cold hydration/warmup;
- explicit endpoint performance and event-log query pages;
- explicit database maintenance/count/prune operations;
- startup-gate schema checks.

## Fix

- Removed the direct `CheckResults` degraded-analysis query from `StateEvaluationService`.
- Extended `RollingAssignmentWindowStore` to keep rolling 24h success/failure result samples, not only successful RTT samples.
- Added cache-provided degraded metrics for:
  - baseline sample count;
  - current sample count;
  - packet loss;
  - average RTT;
  - jitter and jitter sample count.
- Updated result ingestion so the unbuffered path also updates rolling metrics before state evaluation.
- Cached degraded settings within each state-evaluation service scope to avoid repeated singleton settings reads during a batch.
- Clamped degraded evaluation windows to the rolling 24h cache boundary instead of extending runtime reads beyond the cache.
- Removed page-refresh metric backfills from status and endpoint management queries; they now read existing summary rows only.
- Added debug-level state-evaluation logging with evaluated assignment count, elapsed time, state-change count, and degraded metrics source.

## Validation

- `dotnet test src\WebApp\PingMonitor.Web.Tests\PingMonitor.Web.Tests.csproj`
  - Passed: 72 / 72

Regression barriers added:

- A degraded evaluator test proves cache-provided metrics drive degraded decisions.
- A state-evaluation source test fails if `StateEvaluationService` reintroduces direct `CheckResults` access.
- Summary page source tests fail if status or endpoint management queries reintroduce rolling metric refreshes.
