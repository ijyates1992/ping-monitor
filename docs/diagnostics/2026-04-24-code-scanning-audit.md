# Code Scanning Audit – 2026-04-24

Related issue: #455.

## Source
Open GitHub code scanning alerts for `ijyates1992/ping-monitor` were retrieved on 2026-04-24 via GitHub REST API.

## Alert-by-alert assessment

| Alert(s) | Rule | Location | Summary | Applicability | Decision / rationale |
|---|---|---|---|---|---|
| #28, #27, #26 | `cs/web/missing-token-validation` | `AgentResultsController.cs:34`, `AgentHelloController.cs:32`, `AgentHeartbeatController.cs:36` | Missing CSRF token validation | Partially applicable | These are machine-to-machine agent ingestion endpoints using header API-key/bearer authentication, not cookie/browser workflows. Added explicit `[IgnoreAntiforgeryToken]` to make the trust model explicit and satisfy analyzer intent without changing API contract. |
| #25 | `cs/web/missing-x-frame-options` | `web.config:1` | Missing `X-Frame-Options` header | Applicable | Added `X-Frame-Options: DENY` in IIS `web.config` `customHeaders` as minimal hardening; no API/schema/updater behavior change. |
| #24, #23, #22, #21 | `cs/log-forging` | `StartupAdminBootstrapService.cs:58, 95, 102, 106` | Log entries created from user input | Applicable | Sanitized user-controlled username values before writing to logs by escaping CR/LF. |
| #20, #19 | `cs/log-forging` | `SecurityOperatorActionService.cs:261, 229` | Log entries created from user input | Applicable | Sanitized `securityIpBlockId`, `failureReason`, `operatorUserId`, `userId`, `userName` in warning logs. |
| #18 | `cs/log-forging` | `ResultIngestionService.cs:209` | Log entries created from user input | Applicable | Sanitized `BatchId`, `AgentId`, and assignment IDs array before logging on state-evaluation error path. |
| #17, #16, #15, #14 | `cs/log-forging` | `DatabaseMaintenanceService.cs:963, 499, 474, 456` | Log entries created from user input | Applicable | Sanitized all request/file/message string values written to maintenance restore logs; no restore flow logic changes. |
| #13 | `cs/log-forging` | `DatabaseMaintenanceProgressTracker.cs:105` | Log entries created from user input | Applicable | Sanitized snapshot file path before logging read failure. |
| #12, #11, #10, #9, #8 | `cs/log-forging` | `ConfigurationRestoreService.cs:145, 144, 71, 58, 57` | Log entries created from user input | Applicable | Sanitized file ID references in restore/preview logs; kept restore behavior unchanged. |
| #7 | `cs/log-forging` | `ConfigurationRestorePreviewService.cs:58` | Log entries created from user input | Applicable | Sanitized file ID in preview log. |
| #6, #5 | `cs/log-forging` | `ConfigurationBackupManagementService.cs:42, 47` | Log entries created from user input | Applicable | Sanitized `FileId` in delete and not-found logs. |
| #4, #3 | `cs/log-forging` | `ConfigurationBackupDocumentLoader.cs:138, 125` | Log entries created from user input | Applicable | Sanitized `FileId` and validation message in document validation logs. |
| #2, #1 | `cs/log-forging` | `StartupGateController.cs:72` | Log entries created from user input | Applicable | Sanitized host/database-name inputs in startup gate database save-attempt log. |

## Minimal-fix notes

- A centralized `LogValueSanitizer.ForLog(string?)` helper was introduced to escape CR (`\r`) and LF (`\n`) before logging external/operator-supplied values.
- No database schema changes were made.
- No Startup Gate ordering/isolation behavior was changed.
- No agent API contract shape or route changes were made.
- No updater/package/manifest behavior was changed.

## Validation commands and results

- `dotnet build src/WebApp/PingMonitor.Web/PingMonitor.Web.csproj -v minimal` ✅
- `dotnet test src/WebApp/PingMonitor.Web.Tests/PingMonitor.Web.Tests.csproj -v minimal` ✅

(Executed with .NET SDK `10.0.104` installed locally in this environment.)
