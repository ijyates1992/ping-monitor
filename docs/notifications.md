# Notifications Design

## Overview

Notifications inform operators about meaningful monitoring events that require awareness or action.

This design is operator-focused and aligned with the control-plane role of the web application. Notifications are for monitoring outcomes (state/lifecycle events), not for network control or remediation.

---

## Notification channels

### Browser notifications (initial implementation)

The first implementation phase is browser notifications delivered while an operator has the web application open.

### Telegram (phase 1, polling inbound)

Telegram delivery is supported as a per-user notification channel in phase 1.

Phase 1 transport mode is polling inbound updates via the Telegram Bot API. Webhook mode remains optional future scope.

### SMTP email (phase 1)

SMTP-backed email delivery is included in phase 1 as an operator-configured channel.

---

## Notification events

Initial intended notification event types:

- endpoint down
- endpoint recovered
- agent offline
- agent online

Future scope may include security-oriented notifications (for example, authentication/security events), but these are not part of the first implementation.

For phase 1 browser and SMTP delivery, notification events are sourced from the persisted event log (not raw check-result ingestion). Only meaningful event types are eligible for delivery.

---

## Settings model

The system will support notification settings.

Initial settings scope is split by ownership:

### Admin-owned channel infrastructure settings

- SMTP server settings (host, port, TLS mode, username, password secret, from address/display name)
- SMTP infrastructure test-send action for operator verification

### User-owned profile notification preferences

- browser notifications enabled/disabled
- browser notifications enabled/disabled per supported event type
- cached browser permission state
- SMTP notifications enabled/disabled
- SMTP notifications enabled/disabled per supported event type
- quiet hours enabled/disabled (per-user delivery suppression window)
- quiet hours start local time (daily)
- quiet hours end local time (daily)
- quiet hours time zone basis
- quiet hours channel toggles:
  - suppress browser notifications during quiet hours
  - suppress SMTP notifications during quiet hours

Additional phase 1 per-user Telegram settings:

- Telegram notifications enabled/disabled
- Telegram notifications enabled/disabled per supported event type
- Telegram account linking status (verified only delivery)

Future settings scope:

- webhook mode activation controls
- additional controls as new channels are introduced

## Quiet hours / suppression windows (phase 1)

Quiet hours are a **per-user daily notification-delivery suppression window** for operator convenience.

Phase 1 scope:

- each authenticated user manages their own quiet-hours values in their profile
- browser delivery respects that user's quiet-hours browser suppression toggle
- SMTP delivery eligibility respects each user's quiet-hours SMTP suppression toggle
- Telegram delivery eligibility respects each user's quiet-hours Telegram suppression toggle

Critical semantics:

- quiet hours suppress **notification delivery only**
- quiet hours do **not** suppress event logging
- quiet hours do **not** suppress state calculation or state transitions
- quiet hours do **not** change alert/state generation logic

Window behaviour:

- evaluated as a daily local-time window
- evaluated using the configured `QuietHoursTimeZoneId` from notification settings (with UTC fallback if invalid)
- supports windows that cross midnight (for example, `22:00` → `07:00`)
- disabled quiet hours means normal delivery behaviour

Phase 1 delivery policy:

- notifications suppressed by quiet hours are **dropped** (not queued for later delivery)
- operators still have historical visibility via event logs

SMTP secret handling requirements:

- SMTP password must not be echoed back to operators in UI
- SMTP password must not be written to logs
- Stored SMTP secrets must use protected storage appropriate for the deployment model
- Leaving password blank during edit should preserve the existing stored secret unless explicit clear is requested

---

## Browser notification scope (phase 1)

Phase 1 browser notifications are intentionally simple:

- work while the web app is open in the browser
- require browser notification permission from the operator
- require browser notifications to be enabled in app settings
- do not require service workers or full push infrastructure yet
- provide the first, lowest-complexity notification channel

Supported browser-deliverable event types in phase 1:

- endpoint down
- endpoint recovered
- agent offline
- agent online

Phase 1 browser settings are explicit and require both:

- browser notifications enabled in the current authenticated user's profile, and
- the specific browser event type enabled in that same user profile.

Browser permission remains separate and must still be granted by the operator in the browser.

This phase does not include background push delivery when the app/browser is closed.

---

## Future extensibility

The notifications model should remain explicit and maintainable as scope grows.

Planned future expansion:

- Telegram delivery
- email delivery
- richer subscription controls (for example per-endpoint and per-agent preferences)

Future channel additions should reuse a common event model and avoid coupling notification logic to a single channel.

---

## UI direction

Phase 1 per-user ownership starts with a dedicated authenticated `/profile` page in the web application.

That page includes:

- account details (username display + email update)
- password change
- user-owned browser and SMTP notification preferences
- user-owned quiet-hours preferences

The admin notification settings page remains for channel infrastructure (SMTP transport/server configuration), while per-user delivery preferences live in each user profile.
