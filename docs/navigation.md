# Navigation

## Purpose

The web UI uses a shared **top navigation bar** across authenticated operator pages so navigation is consistent and no longer depends on per-page ad-hoc link sets.

## Main navigation links

The primary navigation contains:

- Dashboard (`/status`)
- Events / Logs (`/status#recent-events`)
- Security (`/admin/security`) — admin only
- Notifications (`/settings/notifications`) — admin only
- Backups (`/admin/backups`) — admin only
- Settings (`/admin`) — admin only
- Profile (`/profile`)

The nav also includes:

- Current signed-in username
- Profile shortcut
- Logout action

## Pages that intentionally do not use the main navigation

The main authenticated navigation is intentionally excluded from:

- Startup gate (`/startup-gate`)
- Login (`/account/login`)

These pages are setup/authentication flows and should remain focused.

## Layout approach

A shared **top nav** partial (`Views/Shared/_MainNavigation.cshtml`) is rendered at the top of authenticated pages.

Behaviour:

- Active-state highlighting based on current path prefix
- Role-based visibility for admin sections (existing auth roles)
- Responsive behaviour:
  - full horizontal links on wider viewports
  - collapsible menu on narrower viewports

This keeps navigation practical, explicit, and maintainable for day-to-day operations.
