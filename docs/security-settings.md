# Security settings

## Purpose

Security settings provide operator-managed authentication protection configuration and blocked IP management for authentication paths.

It covers:

- persisted security settings for **agent authentication** and **user authentication**
- persistent blocked IP records for agent/user auth types
- manual IP block and unblock from the admin security page
- automatic enforcement of temporary/permanent IP blocking
- automatic enforcement of temporary user account lockout

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

## IP block vs account lockout

- **IP block** applies to requests coming from a source IP address for a selected auth type (`User` or `Agent`).
- **Account lockout** applies to a specific user account and is configured separately from IP block thresholds/duration.

Account lockout is enforced through ASP.NET Identity lockout fields (`LockoutEnd`, access-failed counters), not through a separate parallel lockout store.

## Manual IP block / unblock behaviour

- Operators can manually add a block for a valid IP address and selected auth type.
- Block type for manual blocks is recorded as `Manual`.
- Existing active blocks for the same `(AuthType, IpAddress)` are not duplicated.
- Unblock is implemented as an audited soft-removal (`RemovedAtUtc`, `RemovedByUserId`) rather than hard deletion.

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
Settings updates are also event logged.
