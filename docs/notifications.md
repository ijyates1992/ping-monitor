# Notifications Design

## Overview

Notifications inform operators about meaningful monitoring events that require awareness or action.

This design is operator-focused and aligned with the control-plane role of the web application. Notifications are for monitoring outcomes (state/lifecycle events), not for network control or remediation.

---

## Notification channels

### Browser notifications (initial implementation)

The first implementation phase is browser notifications delivered while an operator has the web application open.

### Telegram (future)

Telegram delivery is planned as a future notification channel.

### Email (future)

Email delivery is planned as a future notification channel.

---

## Notification events

Initial intended notification event types:

- endpoint down
- endpoint recovered
- agent offline
- agent online

Future scope may include security-oriented notifications (for example, authentication/security events), but these are not part of the first implementation.

---

## Settings model

The system will support notification settings.

Initial settings scope:

- browser notifications enabled/disabled
- test notification action for operator verification

Future settings scope:

- channel-specific settings for Telegram and email
- additional controls as new channels are introduced

---

## Browser notification scope (phase 1)

Phase 1 browser notifications are intentionally simple:

- work while the web app is open in the browser
- require browser notification permission from the operator
- do not require service workers or full push infrastructure yet
- provide the first, lowest-complexity notification channel

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

The next implementation step is a dedicated notification settings page in the web application.

That page should start with browser notification controls and provide clear placeholders for future Telegram and email settings.
