# Startup Gate Service Audit (code-based)

Date: 2026-04-06
Scope: `src/WebApp/PingMonitor.Web`
Method: static code analysis only (registrations, middleware flow, hosted service loops, guard checks, and DB call paths).

## Summary

- Five hosted/background services are registered in `Program.cs` and will start when the host starts, regardless of request middleware state.
- Startup Gate enforcement is implemented as HTTP middleware, so it gates request handling paths but does **not** prevent hosted services from starting.
- Only one hosted service (`TelegramPollingBackgroundService`) explicitly checks startup-gate runtime mode before doing work.
- Four hosted services can execute while Startup Gate is active; three of those can perform DB work if they have pending work or scheduled triggers.
- Startup-gate controller/service paths are intentionally executable during gate mode and can perform DB access (schema/admin/bootstrap/backup/restore/status checks).

## Startup flow evidence (why hosted services can run during gate)

1. Hosted services are registered via `AddHostedService(...)` in `Program.cs`.
2. Startup Gate is enforced in an HTTP middleware block (`app.Use(async ...)`) that evaluates status and redirects/503s non-`/startup-gate` requests.
3. Middleware has no mechanism to pause host-level `IHostedService` startup; therefore hosted services begin independently of gate mode.

## Hosted/background services audit

| Service/Class | Registration | Starts while gate active? | Does work while gate active? | DB access while gate active? | Guarded by Startup Gate? | Code evidence / notes |
|---|---|---|---|---|---|---|
| `ConfigurationAutoBackupBackgroundService` | `AddHostedService(sp => sp.GetRequiredService<ConfigurationAutoBackupBackgroundService>())` | **Yes** | **Yes** (scheduled loop and signal-driven backup creation) | **Yes** (calls backup/retention services that query DB) | **No** | Loop runs every 15s and may call `CreateAutomaticBackupAsync`; no runtime gate check. |
| `AgentStatusTransitionBackgroundService` | `AddHostedService<AgentStatusTransitionBackgroundService>()` | **Yes** | **Yes** (polls every 30s) | **Yes** (queries `Agents`, writes status/events) | **No** | `ExecuteAsync` always calls `EvaluateTransitionsAsync`; no startup-gate condition. |
| `BufferedResultFlushBackgroundService` | `AddHostedService<BufferedResultFlushBackgroundService>()` | **Yes** | **Conditionally yes** (flush loop always active; flush work only if buffer has pending items or fallback due) | **Conditionally yes** (on flush, writes `CheckResults`, updates metrics, enqueues state processing) | **No** | No startup-gate check in loop; DB work in `PersistAndEnqueueAssignmentsAsync`. |
| `AssignmentProcessingBackgroundService` | `AddHostedService<AssignmentProcessingBackgroundService>()` | **Yes** | **Conditionally yes** (runs when queue has pending items) | **Conditionally yes** (calls `IStateEvaluationService`) | **No** | No startup-gate check; processes queue whenever signaled/non-empty. |
| `TelegramPollingBackgroundService` | `AddHostedService<TelegramPollingBackgroundService>()` | **Yes** | **No operational polling while gate active** | **No** (while blocked by gate mode) | **Yes** | Explicitly pauses when `IStartupGateRuntimeState.CurrentMode != StartupMode.Normal`; only resumes polling in normal mode. |

## Non-hosted services that still run during Startup Gate (request-driven)

These are not background workers, but they are operational during gate mode through the `/startup-gate` route and can perform DB work before normal operation is available:

- `StartupGateService` (`IStartupGateService`) evaluates DB config, opens MySQL connection, runs schema and admin checks each evaluation.
- `StartupSchemaService` / `StartupAdminBootstrapService` invoked by Startup Gate flows (status checks + schema apply/admin creation actions).
- `IDatabaseMaintenanceService` is used by startup-gate upload/restore/list flows and can run DB backup/restore operations.

## Confirmed safe during Startup Gate

- `TelegramPollingBackgroundService` is correctly no-op for operational polling while gate mode is active because it hard-checks runtime mode and delays/continues without invoking `ITelegramPollingService.PollOnceAsync`.

## Likely regressions / services starting too early

From code, these services start and can perform operational work while gate mode is active, without explicit startup-gate guards:

1. `AgentStatusTransitionBackgroundService` (DB reads/writes + event log writes).
2. `ConfigurationAutoBackupBackgroundService` (scheduled/signal backups and retention work via DB-backed services).
3. `BufferedResultFlushBackgroundService` (DB writes if buffered items exist).
4. `AssignmentProcessingBackgroundService` (state evaluation path if queue has assignments).

## Unclear / needs runtime validation

Code establishes capability and call paths, but runtime evidence is still recommended for:

- Whether buffered/queue-driven services actually have pending work during specific gate stages (depends on prior in-memory state and runtime traffic timing).
- Whether auto-backup scheduled times or config-change signals are present early in startup for a given deployment profile.
- Whether startup-gate runtime mode transitions are delayed in low-traffic scenarios, since runtime state updates occur in request middleware.

## Files analysed

- `src/WebApp/PingMonitor.Web/Program.cs`
- `src/WebApp/PingMonitor.Web/Services/StartupGate/StartupGateRuntimeState.cs`
- `src/WebApp/PingMonitor.Web/Services/StartupGate/StartupGateService.cs`
- `src/WebApp/PingMonitor.Web/Services/AgentStatusTransitionBackgroundService.cs`
- `src/WebApp/PingMonitor.Web/Services/BufferedResults/BufferedResultFlushBackgroundService.cs`
- `src/WebApp/PingMonitor.Web/Services/Background/AssignmentProcessingBackgroundService.cs`
- `src/WebApp/PingMonitor.Web/Services/Telegram/TelegramPollingBackgroundService.cs`
- `src/WebApp/PingMonitor.Web/Services/Telegram/TelegramPollingService.cs`
- `src/WebApp/PingMonitor.Web/Services/Backups/ConfigurationAutoBackupBackgroundService.cs`
- `src/WebApp/PingMonitor.Web/Services/Backups/ConfigurationBackupService.cs`
- `src/WebApp/PingMonitor.Web/Controllers/StartupGateController.cs`
- `src/WebApp/PingMonitor.Web/Services/DynamicPingMonitorDbContextFactory.cs`
