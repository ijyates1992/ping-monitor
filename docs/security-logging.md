# Security logging

## Purpose

Security logging provides auditable, persistent authentication-attempt logging for operators.

It covers:

- user authentication attempts
- agent authentication attempts
- a read-only admin page for reviewing recent attempts

Automatic active controls for temporary/permanent IP blocking and temporary user lockout are enforced as defined in `docs/security-settings.md`.

Security auth log retention/pruning is configurable and applies **only** to authentication-attempt logs (`SecurityAuthLog` rows).

---

## What is logged

Authentication attempts are persisted as `SecurityAuthLog` rows.

Each row records one authentication attempt with:

- `SecurityAuthLogId`
- `OccurredAtUtc`
- `AuthType` (`User` or `Agent`)
- `SubjectIdentifier` (username/email for users, instance id for agents when available)
- `SourceIpAddress` (nullable)
- `Success`
- `FailureReason` (nullable)
- `UserId` (nullable)
- `AgentId` (nullable)
- `DetailsJson` (nullable, non-secret context only)

---

## User authentication attempt logging

The user login flow records both successful and failed login attempts.

Expected failure-reason examples:

- `unknown_user`
- `invalid_password`
- `account_locked`
- `login_not_allowed`
- `two_factor_required`
- `ip_temporarily_blocked`
- `ip_permanently_blocked`
- `account_temporarily_locked`

No passwords or secret material are logged.

---

## Agent authentication attempt logging

Agent API authentication records both successful and failed attempts for `/api/v1/agent/*` endpoints.

Expected failure-reason examples:

- `missing_instance_header`
- `missing_or_invalid_bearer_token`
- `unknown_instance`
- `disabled_agent`
- `revoked_key`
- `invalid_key`
- `ip_temporarily_blocked`
- `ip_permanently_blocked`

No bearer tokens, API keys, or secret material are logged.

---

## Security logs/settings page

A read-only admin page exposes recent authentication attempts in two panels:

- user authentication attempts
- agent authentication attempts

Each entry shows:

- UTC date/time
- source IP
- subject identifier
- success/failure
- failure reason (if present)

Filtering is explicit and server-side for both user and agent auth panels:

- UTC from/to date-time range (`OccurredAtUtc`)
- text search over practical fields (`SubjectIdentifier`, source IP, failure reason, related user/agent identifiers when available)
- success/failure visibility controls

Default behavior remains failure-focused:

- successful attempts are hidden by default
- operators can opt in to show successful attempts when required


## Operator manual enforcement-clear actions

Manual unblock and manual unlock workflows must generate auditable security records.

At minimum, event records include:

- timestamp (event log occurrence time)
- operator identity when available
- action type (`manual_ip_unblock`, `manual_user_unlock`)
- target identifier (IP block id/IP address or user id/user name)
- success/failure outcome and failure reason for rejected requests

These records are written without secrets and are intended for operator audit trails and incident review.

## Security auth log retention and pruning

Security auth log retention is controlled by security settings:

- `SecurityLogRetentionEnabled`
- `SecurityLogRetentionDays`
- `SecurityLogAutoPruneEnabled` (setting exists; automatic execution may be disabled/deferred by implementation)

### Prune eligibility (in scope)

Rows are eligible for prune when all are true:

- row is from `SecurityAuthLogs`
- row type is an authentication attempt log (user or agent auth)
- `OccurredAtUtc` is older than `UTC now - SecurityLogRetentionDays`
- retention is enabled and settings are valid

### Not eligible (out of scope)

Pruning must **not** touch:

- active blocked IP records (`SecurityIpBlocks`)
- user lockout enforcement state (ASP.NET Identity lockout fields)
- event logs
- monitoring results/state/history
- backup files

### Manual prune behavior

- Admin security page shows the current prune cutoff and eligible row count preview.
- Manual prune requires explicit typed confirmation: `PRUNE`.
- Confirmation is enforced server-side.
- Manual prune is destructive and irreversible.
- Manual prune writes auditable security events (request + completion) including cutoff and deleted row count.
