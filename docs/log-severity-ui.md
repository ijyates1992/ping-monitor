# Log severity UI presentation

This document defines the **UI presentation rule** for logged items across the web app.

It does not change event generation, security enforcement, alerting, or state-machine behavior.

## Site-wide colour mapping

All logged-item views use the same visual severity mapping:

- `log-info` (green): info / up / success
- `log-warning` (orange): warning
- `log-error` (red): error / down / failure

These are presentation-only classes used for consistent operator scanning in light and dark themes.

## Event log mapping

For event-log backed views (recent events, endpoint history, agent history):

- UI severity maps directly from `EventLog.Severity`:
  - `info` -> `log-info`
  - `warning` -> `log-warning`
  - `error` -> `log-error`

No message-text parsing is used for severity colour selection.

## Security auth log mapping

`SecurityAuthLog` rows do not store a severity field, so UI severity is derived explicitly:

- Successful authentication attempt -> `log-info`
- Failed attempt with deny/block/lockout semantics -> `log-error`
  - `ip_temporarily_blocked`
  - `ip_permanently_blocked`
  - `account_locked`
  - `account_temporarily_locked`
  - `login_not_allowed`
  - `disabled_agent`
  - `revoked_key`
- Other failed authentication attempts -> `log-warning`

This keeps failed-but-expected validation failures distinct from explicit denial/enforcement failures.

## Views covered

- Status page recent events panel
- Endpoint history events table
- Agent history events table
- Admin security user authentication attempts
- Admin security agent authentication attempts
