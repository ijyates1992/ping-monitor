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

## Draft Editor Virtual Canvas Slice

This slice upgrades the client-side draft editor from a viewport-sized placement area to a larger virtual diagram workspace.

Included in this slice:

- A 4000 x 2500 diagram-unit virtual canvas rendered inside the existing visible editor area.
- Draft nodes and draft links use diagram/world coordinates rather than raw browser viewport coordinates.
- Empty canvas space can be dragged with pointer events to pan the visible workspace.
- Mouse wheel zooms the canvas around the mouse pointer.
- Toolbar controls provide zoom in, zoom out, reset view, fit content, and a current zoom percentage display.
- The SVG draft link overlay is rendered in the same transformed world coordinate system as the nodes, so draft links stay connected while panning, zooming, dragging nodes, or resizing the browser.
- New monitored endpoint nodes and custom nodes are placed near the current visible viewport centre with a small stagger so repeated additions do not stack exactly.
- Draft nodes are constrained to the virtual canvas bounds, not the visible viewport bounds.
- The layout remains client-side draft-only in this slice; diagrams are not saved yet.

Persistence boundary:

- Do not introduce partial persistence for this draft editor slice.
- Future saved diagram layouts should persist virtual world coordinates, virtual canvas metadata, pan/zoom metadata where appropriate, and documentation-only link metadata. They should not persist browser screen coordinates.

Architecture boundary:

- Diagram links remain documentation-only.
- This slice does not change endpoint monitoring, state evaluation, dependency suppression, alerting, agents, SNMP, topology inference, endpoint creation, or dependency creation.

## Manual Regression Checklist - Virtual Canvas Pan/Zoom

- Open the editor at 1920x1080.
- Open the editor at 1366x768.
- Add several monitored endpoint nodes.
- Add several custom nodes.
- Draw links between nodes.
- Pan around by dragging empty canvas space.
- Zoom in/out with mouse wheel.
- Confirm zoom centres around the mouse pointer.
- Confirm nodes remain connected to links while zooming.
- Confirm nodes remain connected to links while panning.
- Confirm node dragging works correctly at 0.5x, 1x, and 2x zoom.
- Confirm new nodes are added into the currently visible area after panning/zooming.
- Confirm reset view works.
- Confirm feature disabled hides nav and blocks direct access.
- Confirm no endpoint dependencies are created or changed.
