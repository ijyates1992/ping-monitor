# Security logging

## Purpose

Security logging provides auditable, persistent authentication-attempt logging for operators.

It covers:

- user authentication attempts
- agent authentication attempts
- a read-only admin page for reviewing recent attempts

Automatic active controls for temporary/permanent IP blocking and temporary user lockout are enforced as defined in `docs/security-settings.md`.

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

## Security logs/settings page (phase 1)

A read-only admin page exposes recent authentication attempts in two panels:

- user authentication attempts
- agent authentication attempts

Each entry shows:

- UTC date/time
- source IP
- subject identifier
- success/failure
- failure reason (if present)

Default behavior is failure-focused:

- successful attempts are hidden by default
- operator can enable a simple toggle to include successful attempts


## Operator manual enforcement-clear actions

Manual unblock and manual unlock workflows must generate auditable security records.

At minimum, event records include:

- timestamp (event log occurrence time)
- operator identity when available
- action type (`manual_ip_unblock`, `manual_user_unlock`)
- target identifier (IP block id/IP address or user id/user name)
- success/failure outcome and failure reason for rejected requests

These records are written without secrets and are intended for operator audit trails and incident review.
