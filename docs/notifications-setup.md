# Notifications Setup Guide (Operator/Admin)

This guide explains how to configure notifications end-to-end in the current Ping Monitor implementation.

It is intended for operators and admins running the .NET 10 web app against MySQL.

---

## 1) Implementation status overview

### Implemented now

- **Per-user notification preferences** in `/profile`:
  - Browser channel enable/disable and per-event toggles
  - SMTP channel enable/disable and per-event toggles
  - Telegram channel enable/disable and per-event toggles
  - Quiet hours (delivery suppression only) and per-channel quiet-hours suppression toggles
- **Admin notification infrastructure settings** in `/settings/notifications`:
  - SMTP transport configuration and test send
  - Telegram bot token + inbound mode setting + poll interval setting
- **Telegram account linking** via one-time verification code and private bot chat.
- **Telegram inbound polling worker** running in the web app background service.

### Implemented with current limitations

- **Browser notifications** are phase-1 polling notifications while the app is open in the browser (no background service-worker push when browser/app is closed).
- **Telegram inbound mode** is effectively polling in phase 1. Webhook-related fields exist in data model/spec, but there is no webhook inbound controller/handler path documented in runtime code yet.
- **Telegram poll interval setting is saved**, but current polling worker loop runs every 10 seconds.
- **Current event-dispatch limitation:** event pipeline currently returns early after a successful SMTP send, so Telegram event delivery may be skipped when SMTP succeeds for that same event.
- **Notification source events** are an explicit subset of event-log events:
  - endpoint down
  - endpoint recovered
  - agent offline
  - agent online

### Optional / future scope

- Telegram webhook inbound mode (public HTTPS endpoint + secret validation) is optional future scope per specs.
- Additional notification event classes (for example security/auth events) are future scope.

---

## 2) Notification channels and ownership model

Ping Monitor currently has three operator-facing channels:

1. **Browser** (per-user, in-browser while open)
2. **SMTP email** (admin-configured transport + per-user opt-in)
3. **Telegram bot** (admin-configured transport + per-user linked account)

Settings ownership is intentionally split:

- **Admin-owned transport/infrastructure settings**: `/settings/notifications`
- **User-owned delivery preferences**: `/profile`

This means a channel can be configured globally by admin, but each user still controls whether they receive that channel and which event types they receive.

---

## 3) Prerequisites

Before configuring notifications:

- Web app is running and reachable over HTTPS.
- MySQL-backed startup is complete.
- You can sign in as:
  - an **admin** user (for `/settings/notifications`), and
  - a normal/authenticated user (for `/profile` preferences/linking).
- For SMTP: valid SMTP server credentials and sender identity.
- For Telegram: a Telegram bot token from BotFather.

---

## 4) Browser notification setup (per user)

### Where settings live

- Open **My profile**: `/profile`
- Use the **Notification preferences** section under **Browser**.

### Required conditions

Browser delivery requires **both**:

1. Browser permission = granted at browser level.
2. In-app preference = enabled for the user in `/profile`.

These are separate controls; either one being off prevents delivery.

### Steps

1. Sign in as the target user.
2. Go to `/profile`.
3. In **Notification preferences > Browser**:
   - Enable **Enable browser notifications**.
   - Enable desired event toggles:
     - Endpoint down
     - Endpoint recovered
     - Agent offline
     - Agent online
4. Save notification preferences.
5. In browser/site settings, allow notifications for the web app origin.

### How to test browser notifications

Phase-1 behavior is feed polling while the status page is open.

1. Keep Ping Monitor open in your browser (recommended: status page).
2. Trigger or wait for one eligible event (down/recovered/offline/online).
3. Confirm a browser notification appears.

If app preference is enabled but permission is denied/default, notification will not display.

---

## 5) SMTP setup

SMTP configuration is split between admin transport config and per-user preferences.

### 5.1 Admin SMTP transport configuration

#### Where settings live

- Admin page: `/settings/notifications`
- Section: **SMTP transport settings**

#### Required fields

When enabling SMTP infrastructure, configure:

- **Host** (`SmtpHost`)
- **Port** (`SmtpPort`)
- **TLS enabled/disabled** (`SmtpUseTls`)
- **Username** (`SmtpUsername`) (optional if relay allows anonymous/default credentials)
- **Password** (`SmtpPassword`) (required if username is provided)
- **From address** (`SmtpFromAddress`)
- **From display name** (`SmtpFromDisplayName`) (optional)

#### Secret handling behavior

- Stored SMTP password is not shown back in clear text.
- UI shows only whether a password is configured.
- Leave password blank during edits to retain existing stored value.
- Use **Clear stored SMTP password** only when intentionally removing it.

### 5.2 SMTP recipient ownership model (current implementation)

SMTP recipients are per-user and derived from user profile email addresses:

- User must have a valid email in account profile.
- User must enable SMTP notifications in `/profile`.
- User must enable relevant per-event SMTP toggles.
- User must not be suppressed by quiet-hours SMTP suppression at send time.

### 5.3 Enable SMTP notifications for a user

1. User opens `/profile`.
2. In **Notification preferences > SMTP**:
   - Enable **Enable SMTP notifications to my email**.
   - Enable desired event toggles.
3. Save notification preferences.
4. Ensure account email is valid in **Account details** on the same page.

### 5.4 Send a test email

1. Admin opens `/settings/notifications`.
2. Confirm SMTP fields are populated correctly.
3. Click **Send SMTP test email to my account**.
4. Test sends to the currently signed-in admin user’s account email.

### 5.5 Current SMTP limitations / notes

- SMTP test requires current signed-in user to have a valid email address.
- If SMTP infrastructure is disabled globally, no SMTP event notifications are sent.
- SMTP delivery uses event-log eligible events only (not raw check ingestion).

---

## 6) Telegram bot setup (admin)

A Telegram bot is required for Telegram notifications and account linking.

### 6.1 Create bot and obtain token

1. In Telegram, open **@BotFather**.
2. Run `/newbot`.
3. Follow prompts for bot name and username.
4. Copy bot token returned by BotFather.

Treat token as a secret credential.

### 6.2 Enter token in Ping Monitor

1. Sign in as admin.
2. Open `/settings/notifications`.
3. In **Telegram transport settings**:
   - Enable **Enable Telegram infrastructure**.
   - Set inbound mode to `Polling`.
   - Set poll interval value (saved as config; see implementation note below).
   - Paste token in **Telegram bot token**.
4. Save settings.

### 6.3 Token storage and visibility

- Token is stored as a protected secret value.
- UI indicates whether token is configured; it does not display existing token text.
- To remove stored token, use **Clear stored Telegram bot token**.

---

## 7) Telegram transport setup and mode expectations

### Current supported inbound behavior

- **Recommended and currently active mode**: `Polling`
- Polling worker runs as a hosted background service in the web app process.

### Webhook mode status

- Webhook mode is listed in spec/data model as optional future direction.
- Current runtime codebase does not expose a dedicated webhook receive endpoint/handler path.
- Do **not** plan production operations around webhook mode unless/until explicitly implemented and documented in repo runtime docs.

### Deployment recommendations

- **Development**: Polling (default).
- **LAN/internal deployments**: Polling (no public Telegram callback endpoint required).
- **Public deployments**: Polling remains safe default today; webhook should only be considered when implementation exists and a publicly reachable HTTPS endpoint is available.

### Poll interval note

- Admin UI includes poll interval setting.
- Current background loop runs every 10 seconds.
- Treat UI poll interval as stored configuration not yet fully governing runtime cadence.

---

## 8) User Telegram account linking (per user)

Phase 1 linking is private-chat based.

### Linking flow

1. User signs in and opens `/profile`.
2. In **Notification preferences > Telegram**, enable Telegram notifications and preferred event toggles, then save.
3. In **Telegram account linking**, click **Generate link code**.
4. Copy the active 8-digit code shown in profile.
5. In Telegram app, open private chat with the configured bot.
6. Send the code message exactly (8 digits).
7. Bot inbound polling processes the message and verifies code.
8. On success, user account becomes linked/verified and can receive Telegram notifications.

### Verification rules

- One-time code.
- Code expires quickly (currently 15 minutes).
- Private chat only in phase 1.
- Only verified linked Telegram accounts are eligible for delivery.

### Unlinking

- User can remove linked Telegram account from `/profile` using **Remove linked Telegram account**.

---

## 9) Per-event-type notification preferences (per user)

For Browser, SMTP, and Telegram channels, each user can independently toggle event types in `/profile`:

- Endpoint down
- Endpoint recovered
- Agent offline
- Agent online

Delivery eligibility requires:

- channel enabled for that user, and
- event toggle enabled for that channel/user, and
- channel infrastructure enabled/configured where applicable (SMTP/Telegram), and
- no active quiet-hours suppression for that channel/user.

---

## 10) Quiet hours / suppression windows

### Where configured

- User profile page `/profile` under **Quiet hours**.

### Semantics (current implementation)

Quiet hours affect **delivery only**:

- They do suppress outbound notifications (per user + per channel suppression toggle).
- They do **not** stop event logging.
- They do **not** stop monitoring state transitions.
- Suppressed notifications are **dropped**, not queued for later delivery.

### Time window behavior

- Daily local-time window.
- Supports crossing midnight (for example `22:00` to `07:00`).
- Uses configured timezone ID; invalid timezone falls back to UTC internally.

### Channels with quiet-hours suppression support

- Browser
- SMTP
- Telegram

Each has its own per-user quiet-hours suppression checkbox.

---

## 11) Troubleshooting quick reference

### Browser permission denied

- Check browser/site notification permission for app origin.
- Ensure user enabled browser channel in `/profile`.

### Browser enabled in app but nothing appears

- Confirm app tab/page remains open (phase 1 behavior).
- Confirm relevant event type toggle is enabled.
- Confirm event type is one of currently eligible ones.
- Confirm quiet-hours browser suppression is not active.

### SMTP test send fails

- Validate host/port/TLS/from address.
- If username set, ensure password is configured.
- Ensure current signed-in user has valid profile email.
- Check application logs for SMTP failure details.

### SMTP settings incomplete

- SMTP cannot send when required fields are invalid/missing.
- Re-save SMTP infrastructure in `/settings/notifications` after corrections.

### Telegram bot token invalid

- Re-copy token from BotFather and update `/settings/notifications`.
- Ensure Telegram infrastructure is enabled globally.

### Telegram polling not receiving messages

- Confirm inbound mode is `Polling`.
- Confirm bot token configured.
- Confirm web app process is running (polling hosted service is in-process).
- Check logs for Telegram polling warnings.

### Verification code expired

- Generate a new link code in `/profile` and resend in private bot chat.

### Verification code already used / not found

- Generate a fresh code and retry.
- Ensure exact 8-digit code is sent.

### Telegram account not linked

- Ensure code was sent in **private** chat (not group/channel).
- Confirm code not expired.
- Confirm polling worker is healthy.

### User not receiving notifications despite channel enabled

- Check per-event toggle for that channel/user.
- Check quiet-hours channel suppression state.
- For SMTP, confirm user has valid email.
- For Telegram, confirm account is linked and verified.
- If SMTP is enabled and succeeding, be aware of current limitation where Telegram may be skipped for the same event dispatch.

### Quiet hours suppressing notifications unexpectedly

- Verify user timezone ID and quiet-hours start/end values.
- Check specific channel suppression toggles.

---

## 12) Operator runbook checklist (end-to-end)

1. Admin configures SMTP transport in `/settings/notifications` and verifies with SMTP test.
2. Admin creates Telegram bot, sets token, enables Telegram transport (polling).
3. Each user updates `/profile` account email (for SMTP).
4. Each user sets channel-level + event-level preferences in `/profile`.
5. Each user links Telegram account via generated code + private bot chat.
6. Each user configures quiet hours as desired.
7. Operator triggers known test events and validates browser/SMTP/Telegram delivery behavior.

---

## 13) Known documentation gaps / follow-up items

- If Telegram webhook mode is implemented later, add explicit endpoint/security/certificate runbook steps in this document.
- If runtime begins honoring configurable Telegram poll interval, update transport section to reflect exact behavior.
- If notification pipeline behavior changes (for example multi-channel send ordering), update troubleshooting and status notes.
