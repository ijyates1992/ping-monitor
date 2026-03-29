# Theme settings

The web UI supports three theme preference modes:

- `on`: always use dark theme.
- `off`: always use light theme.
- `system`: follow browser/OS `prefers-color-scheme`.

## Persistence

Theme preference is stored as a UI-only preference in browser storage:

- primary: `localStorage` key `pingmonitor.themeMode`
- fallback: cookie `pm_theme_mode`

This allows preference reuse on authenticated and unauthenticated pages (including login/startup gate) without introducing a new server-side settings subsystem.

## Scope

Theme preference is visual/UI only.

It does **not** change authentication, authorization, monitoring, suppression, alerting, or any server-side behavior.
