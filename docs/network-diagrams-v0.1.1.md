# Network diagrams V0.1.1 groundwork

Issue: #492

## Scope

This first slice adds only the operational groundwork for future network diagrams:

- an admin enable/disable setting: `NetworkDiagramsEnabled`
- a gated placeholder page at `/network-diagrams`
- a Monitoring navigation link shown only to admins when the setting is enabled
- schema version 15, adding `ApplicationSettings.NetworkDiagramsEnabled`

The feature flag defaults to `false`.

## Explicit non-goals

This slice does not add diagram storage or editing behavior.

- no topology canvas
- no node, link, or diagram tables
- no endpoint drag/drop
- no automatic topology discovery
- no SNMP
- no automatic endpoint creation
- no automatic dependency creation

## Monitoring safety

Network diagrams are visual documentation/status-overlay only. They must not alter:

- monitoring state
- dependency suppression
- alerting
- agent behavior
- endpoint or assignment configuration

Future diagram editor work must keep monitoring relationships separate from visual topology data.

## Follow-up editor shell slice

Issue: #493

The next safe slice replaces the placeholder content at `/network-diagrams` with a basic editor shell while keeping the same feature gate.

Included:

- responsive full-height editor layout
- top editor toolbar, left toolbox, main canvas, and bottom status bar
- toolbox sections for monitored endpoints, custom devices, and notes
- temporary client-side draft nodes for routers, switches, firewalls, servers, generic devices, and notes
- pointer-event dragging for draft nodes within the visible canvas
- `ResizeObserver` sizing for the canvas host

Still excluded:

- no saved diagrams yet
- no diagram, node, or link database tables
- no link drawing yet
- no live monitored endpoint nodes yet
- no endpoint monitoring, state evaluation, dependency suppression, alerting, or agent behaviour changes

Manual regression checklist for the shell:

- Open `/network-diagrams` at 1920x1080.
- Open `/network-diagrams` at 1366x768.
- Resize the browser smaller and larger.
- Confirm the canvas fills available space without desktop page-level scrolling.
- Confirm draft nodes can be added and dragged.
- Confirm draft nodes stay within the visible workspace.
- Confirm disabling `NetworkDiagramsEnabled` hides navigation and blocks direct access.
