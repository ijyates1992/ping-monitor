# Security settings

## Purpose

Security settings provide operator-managed authentication protection configuration and blocked IP management for authentication paths.

It covers:

- persisted security settings for **agent authentication** and **user authentication**
- persistent blocked IP records for agent/user auth types
- manual IP block and unblock from the admin security page
- manual unlock of currently locked-out users from the admin security page
- automatic enforcement of temporary/permanent IP blocking
- automatic enforcement of temporary user account lockout
- operator filtering of user/agent authentication attempt logs by UTC range, search text, and success visibility (successful attempts hidden by default)

## Authentication settings

Security settings are stored as explicit fields in minutes/counts.

### Agent authentication settings

- `AgentFailedAttemptsBeforeTemporaryIpBlock`
- `AgentTemporaryIpBlockDurationMinutes`
- `AgentFailedAttemptsBeforePermanentIpBlock`

### User authentication settings

- `UserFailedAttemptsBeforeTemporaryIpBlock`
- `UserTemporaryIpBlockDurationMinutes`
- `UserFailedAttemptsBeforePermanentIpBlock`
- `UserFailedAttemptsBeforeTemporaryAccountLockout`
- `UserTemporaryAccountLockoutDurationMinutes`

### Security auth log retention settings

- `SecurityLogRetentionEnabled`
- `SecurityLogRetentionDays`
- `SecurityLogAutoPruneEnabled`

Retention settings apply only to authentication-attempt logs (`SecurityAuthLogs`) and must not change active enforcement state.

## IP block vs account lockout

- **IP block** applies to requests coming from a source IP address for a selected auth type (`User` or `Agent`).
- **Account lockout** applies to a specific user account and is configured separately from IP block thresholds/duration.

Account lockout is enforced through ASP.NET Identity lockout fields (`LockoutEnd`, access-failed counters), not through a separate parallel lockout store.

## Manual IP block / unblock behaviour

- Operators can manually add a block for a valid IP address and selected auth type.
- Block type for manual blocks is recorded as `Manual`.
- Existing active blocks for the same `(AuthType, IpAddress)` are not duplicated.
- Manual unblock is allowed only for currently active blocks (not already removed and not expired).
- Unblock is implemented as an audited soft-removal (`RemovedAtUtc`, `RemovedByUserId`) rather than hard deletion.
- Manual unblock requires explicit typed confirmation (`UNBLOCK`) before mutation.

## Manual user unlock behaviour

- Manual unlock is allowed only for users currently in an active lockout state (`LockoutEnd > now`).
- Unlock uses ASP.NET Identity lockout fields directly (`SetLockoutEndDateAsync(..., null)`).
- Access-failed count is reset after a successful manual unlock to avoid immediate re-lock from stale counters.
- Manual unlock requires explicit typed confirmation (`UNLOCK`) before mutation.

## Automatic enforcement behavior

### Enforcement order

Agent authentication request order:

1. Resolve source IP.
2. Check for an active IP block for `AuthType = Agent`.
3. If blocked, reject immediately and write a failed auth log entry.
4. If not blocked, perform normal credential validation.
5. On failed auth, evaluate failed-attempt thresholds and create/update block state as needed.

User login request order:

1. Resolve source IP.
2. Check for an active IP block for `AuthType = User`.
3. If blocked, reject immediately and write a failed auth log entry.
4. If not IP-blocked and user can be identified, check user lockout status.
5. If user is locked, reject and write a failed auth log entry.
6. If not blocked/locked, perform normal credential validation.
7. On failed auth, evaluate IP block thresholds and user lockout thresholds.

### Temporary vs permanent IP block decision

- Failed attempts are evaluated in a deterministic rolling lookback window of recent auth log data.
- If permanent threshold is met or exceeded, create a `Permanent` block (no expiry).
- Otherwise, if temporary threshold is met or exceeded, create a `Temporary` block with expiry set to `now + configured duration`.
- Existing active block rows for the same `(AuthType, IpAddress)` are not duplicated.
- Existing permanent/manual blocks remain authoritative.

### Expiry behavior

- Temporary IP blocks stop enforcing once `ExpiresAtUtc <= now`.
- Expired temporary IP blocks may remain in history for audit purposes.
- Active enforcement checks only consider records where `RemovedAtUtc` is null and expiry is still in the future (or no expiry for permanent/manual blocks).
- User temporary lockout automatically expires when current UTC time passes the stored lockout end value.

## Auditability

Manual and automatic block/unblock actions are persisted as security IP block records and event log entries.
Manual user unlock actions (success and rejected attempts) are event logged with operator identity context when available.
Settings updates are also event logged.

## Reverse proxy header trust requirements

Authentication IP-based protections (temporary/permanent IP block and failed-attempt thresholding) depend on accurate source IP resolution.

- Only trust `X-Forwarded-*` headers from explicitly configured proxy IPs/networks.
- Do not run with unrestricted forwarded-header trust in public deployments.
- If no trusted proxies are configured, forwarded headers should remain limited to default local proxy trust only.

## Security auth log prune controls

- Security page exposes current retention values, computed cutoff, and current eligible auth-log count.
- Manual prune requires typed confirmation (`PRUNE`) before deletion.
- Manual prune deletes only `SecurityAuthLogs` rows older than cutoff.
- Pruning is destructive and irreversible.
- Pruning does not remove active IP block rows and does not unlock users.
- If `SecurityLogAutoPruneEnabled` is present but not wired to automatic execution in the current release, operators must use manual prune.
