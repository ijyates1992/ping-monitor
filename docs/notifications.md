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

### SMTP email (future)

SMTP-backed email delivery is planned as a future notification channel.

---

## Notification events

Initial intended notification event types:

- endpoint down
- endpoint recovered
- agent offline
- agent online

Future scope may include security-oriented notifications (for example, authentication/security events), but these are not part of the first implementation.

For phase 1 browser delivery, notification events are sourced from the persisted event log (not raw check-result ingestion). Only meaningful event types are eligible for browser delivery.

---

## Settings model

The system will support notification settings.

Initial settings scope:

- browser notifications enabled/disabled
- browser notifications enabled/disabled per supported event type
- test notification action for operator verification

Future settings scope:

- channel-specific settings for Telegram and SMTP email
- additional controls as new channels are introduced

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

- global browser notifications enabled in app settings, and
- the specific event type enabled in app settings.

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

The next implementation step is a dedicated notification settings page in the web application.

That page should start with browser notification controls and provide clear placeholders for future Telegram and SMTP email settings.
