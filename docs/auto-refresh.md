# Auto-refresh (phase 1)

## Scope

Auto-refresh is **section-level only**. The application intentionally avoids full-page refresh/reload.

Phase 1 targets:

- Home (`/status`):
  - recent events
  - summary
  - assignments
- Manage endpoints (`/endpoints`):
  - assignments only

## Transport approach

- Use lightweight HTTP `GET` endpoints that return partial HTML for each refreshable section.
- Poll each section on a fixed interval.
- Replace only the matching DOM section (`<section ...>`) on success.
- Do not use SignalR/websockets in phase 1.

## Interval policy

- Status recent events: every **10 seconds**
- Status summary: every **15 seconds**
- Status assignments: every **15 seconds**
- Manage endpoints assignments: every **15 seconds**

Intervals are explicit constants in page scripts so they are easy to tune later.

## UX and stability rules

- Never refresh the whole page.
- Avoid overlapping refresh runs per section.
- Fail quietly on transient fetch errors and retry on the next interval.
- Skip refresh while the tab is hidden.
- Preserve assignment expand/collapse state on the status page where practical by restoring open assignment IDs after section replacement.
- Avoid scroll/focus disruption by replacing only target sections and not triggering navigation.
