# Telegram Integration Specification

## Overview

This document defines the design and implementation of Telegram
integration for the Ping Monitor system.

Telegram is used as a notification channel and supports secure account
linking via a bot-based verification flow.

The system supports two inbound communication modes:

-   Polling (default)
-   Webhook (optional, for public deployments)

------------------------------------------------------------------------

## Goals

-   Provide Telegram notifications for system events
-   Ensure secure and explicit user verification
-   Support both private (LAN/dev) and public deployments
-   Avoid requiring additional agents or services
-   Keep initial implementation simple and extensible

------------------------------------------------------------------------

## Architecture

Telegram integration consists of:

-   Bot API communication (outbound messages)
-   Inbound message handling (polling or webhook)
-   Account linking via verification codes
-   Notification delivery via Telegram

### Key Principle

Inbound transport (polling/webhook) must be decoupled from message
processing.

------------------------------------------------------------------------

## Inbound Modes

### Polling (Default)

Polling is the default mode and should be used in:

-   Development environments
-   LAN/internal deployments
-   Any environment without public internet access

#### Behaviour

-   Background service polls Telegram for updates
-   Processes new messages using stored offset
-   Safe and reliable for all environments

#### Requirements

-   Persist last processed update ID
-   Avoid duplicate message processing

------------------------------------------------------------------------

### Webhook (Optional)

Webhook mode is more efficient but requires the application to be
reachable from the public internet.

#### Behaviour

-   Telegram sends updates via HTTP POST to the app
-   App processes incoming messages in real time

#### Requirements

-   Public HTTPS endpoint
-   Webhook registration via Telegram API
-   Secret token validation

#### Warning (UI Text)

Webhook mode requires this application to be reachable from Telegram
over the public internet.\
It will not work on internal-only or non-public deployments.

------------------------------------------------------------------------

## Configuration

### Telegram Settings

-   Enabled (bool)
-   Bot Token (secure)
-   Inbound Mode (Polling / Webhook)

### Polling Settings (Polling Mode Only)

-   Poll Interval (seconds)

### Webhook Settings (Webhook Mode Only)

-   Webhook URL
-   Register Webhook button
-   Webhook status display

------------------------------------------------------------------------

## Account Linking (Verification Flow)

Telegram accounts are linked using a secure, user-initiated verification
process.

### Flow

1.  System generates a random 8-digit code
2.  User is instructed to send the code to the Telegram bot
3.  Bot receives message via polling or webhook
4.  System matches code to pending link request
5.  Chat ID is stored and marked as verified
6.  Bot confirms successful linking

### Example

User sends: 48291357

Bot replies: ✅ Your Telegram account is now linked to Ping Monitor.

------------------------------------------------------------------------

## Verification Rules

-   Codes must be:
    -   Random (8 digits)
    -   One-time use
    -   Expire after a short period (e.g. 10--15 minutes)
-   Only verified Telegram accounts may receive alerts

------------------------------------------------------------------------

## Data Model (Initial)

### PendingTelegramLink

-   Code
-   CreatedAtUtc
-   ExpiresAtUtc

### TelegramAccount

-   ChatId
-   Verified (bool)
-   LinkedAtUtc

------------------------------------------------------------------------

## Notification Delivery

Telegram is treated as a notification channel.

### Behaviour

-   Only send notifications to verified Telegram accounts
-   Respect global notification settings
-   Respect per-event-type settings

### Message Format (Initial)

Plain text only.

Examples:

-   Ping Monitor: Endpoint Down - Core Switch
-   Ping Monitor: Endpoint Recovered - Core Switch after 00:05:12

------------------------------------------------------------------------

## Security Considerations

-   Do not expose bot token in logs
-   Validate webhook secret token
-   Never send alerts to unverified accounts
-   Expire verification codes

------------------------------------------------------------------------

## Future Enhancements

The following are out of scope for initial implementation but should be
supported by design:

-   Multiple Telegram recipients
-   Per-user Telegram notification preferences
-   Per-agent notification routing
-   Rich message formatting (Markdown, buttons)
-   Automatic chat ID discovery
-   Notification templates

------------------------------------------------------------------------

## Implementation Notes

-   Use Telegram Bot API (no external agents)
-   Use .NET library (e.g. Telegram.Bot) or direct HTTP
-   Keep inbound handling modular:
    -   Polling source
    -   Webhook source
    -   Shared message processor

------------------------------------------------------------------------

## Summary

-   Polling is the default and safe option
-   Webhook is optional for public deployments
-   Account linking is user-driven and secure
-   Telegram integration is built directly into the web app
-   Design supports future per-user notification expansion
