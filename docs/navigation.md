# Site navigation (authenticated web app)

This document defines the site-wide authenticated navigation information architecture.

## Top-level categories and submenu items

The authenticated top navigation uses dropdown menus with this structure:

- **Monitoring**
  - Operational status → `/status`
  - Manage endpoints → `/endpoints`
  - Manage groups → `/groups` (Admin only)
- **Agents** (Admin only)
  - Manage agents → `/agents`
  - Deploy new agent → `/agents/deploy`
- **Events / Logs** (Admin only)
  - Recent events → `/status#recent-events`
  - Security logs → `/admin/security#security-logs`
- **Notifications**
  - My notification settings → `/profile#notification-preferences`
  - Notification infrastructure settings → `/settings/notifications` (Admin only)
- **Admin** (Admin only)
  - Security settings → `/admin/security#security-settings`
  - Configuration backups → `/admin/backups`
  - DB maintenance → `/admin`
  - User management → `/users`
- **Profile**
  - My profile → `/profile`
  - Change password → `/profile#change-password`
  - Logout → `POST /account/logout`

## Duplicate destination rule

Duplicate destinations in the site-wide navigation are not allowed.

- No separate top-level Dashboard item is included.
- Operational status (`/status`) is the single status landing destination.
- Each menu item must map to a distinct destination path/anchor combination.

## Intentional exclusions from main nav

The full authenticated site nav is intentionally not shown on:

- Startup gate flow (`/startup-gate`)
- Login/auth entry pages (`/account/login`)

These flows are standalone and are not part of normal authenticated operations navigation.
