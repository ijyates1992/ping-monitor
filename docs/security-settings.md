# Security settings (phase 1)

## Purpose

This phase adds operator-managed security configuration and blocked IP management for authentication paths.

It covers:

- persisted security settings for **agent authentication** and **user authentication**
- persistent blocked IP records for agent/user auth types
- manual IP block and unblock from the admin security page

It does **not** yet implement full automatic enforcement of temporary/permanent IP blocks or account lockouts.

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

In this phase, account lockout values are configurable and persisted, but lockout policy enforcement remains a follow-up phase.

## Manual IP block / unblock behaviour

- Operators can manually add a block for a valid IP address and selected auth type.
- Block type for manual blocks is recorded as `Manual`.
- Existing active blocks for the same `(AuthType, IpAddress)` are not duplicated.
- Unblock is implemented as an audited soft-removal (`RemovedAtUtc`, `RemovedByUserId`) rather than hard deletion.

## Auditability

Manual block and unblock actions are persisted as security IP block records and event log entries.
Settings updates are also event logged.

## Phase boundary

This phase intentionally focuses on:

- settings persistence
- operator UI for settings
- blocked IP list and manual management

Automatic auth enforcement, automatic expiry handling pipeline behavior, and lockout execution are next-phase work.
