# Telegram Integration Specification (Revised)

## Overview

Telegram is a per-user notification channel for Ping Monitor.

Integration supports: - Secure account linking via verification code -
Browser-independent delivery - Polling (default) and Webhook (optional)

------------------------------------------------------------------------

## Key Design Principles

-   Fully **per-user ownership**
-   Inbound transport decoupled from processing
-   Simple first-phase implementation
-   Secure, explicit linking
-   No global notification behaviour

------------------------------------------------------------------------

## Architecture

Components: - Telegram Bot API (outbound) - Inbound updates
(Polling/Webhook) - Verification flow - Notification delivery

------------------------------------------------------------------------

## Inbound Modes

### Polling (Default)

-   Uses long polling
-   Persists last update ID
-   Handles restarts safely
-   Implements backoff on API errors

### Webhook (Optional)

-   Requires public HTTPS
-   Dedicated endpoint
-   Validates Telegram secret token

------------------------------------------------------------------------

## Configuration

### Telegram Settings

-   Enabled
-   Bot Token (secure)
-   Inbound Mode (Polling/Webhook)

------------------------------------------------------------------------

## Account Linking (Per-User)

### Flow

1.  User requests link from profile page
2.  System generates 8-digit code
3.  User sends code to bot **via private chat**
4.  System matches code:
    -   not expired
    -   not used
    -   tied to user
5.  Code consumed atomically
6.  Chat ID linked to user
7.  Bot confirms success

------------------------------------------------------------------------

## Verification Rules

-   8-digit random codes
-   One-time use
-   Expire (10--15 minutes)
-   Private chat only (phase 1)

------------------------------------------------------------------------

## Data Model

### PendingTelegramLink

-   UserId
-   Code
-   CreatedAtUtc
-   ExpiresAtUtc
-   UsedAtUtc
-   ConsumedByChatId
-   Status

### TelegramAccount

-   UserId
-   ChatId
-   Verified
-   LinkedAtUtc
-   Username (optional)
-   DisplayName (optional)
-   IsActive

------------------------------------------------------------------------

## Notification Delivery

Telegram is per-user:

-   Only to verified accounts
-   Respects per-user settings
-   Respects per-event-type settings
-   Respects quiet hours

### First Phase Constraints

-   One Telegram account per user
-   Private chats only
-   No groups/channels

------------------------------------------------------------------------

## Message Format

Plain text:

-   Ping Monitor: Endpoint Down - Core Switch
-   Ping Monitor: Endpoint Recovered - Core Switch after 00:05:12

------------------------------------------------------------------------

## Security

-   Do not log bot token
-   Validate webhook secret
-   Expire codes
-   Never notify unverified users

------------------------------------------------------------------------

## Error Handling

-   Log send failures
-   Do not break pipeline on failure
-   Optional retry later

------------------------------------------------------------------------

## Future Enhancements

-   Multiple Telegram accounts per user
-   Group/channel support
-   Rich formatting
-   Routing rules
-   Templates

------------------------------------------------------------------------

## Summary

-   Fully per-user design
-   Secure linking
-   Polling default, webhook optional
-   Clean extensibility
