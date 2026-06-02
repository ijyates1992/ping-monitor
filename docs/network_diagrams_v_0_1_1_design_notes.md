# Network Diagrams V0.1.1 – Design Notes

## Purpose

V0.1.1 introduces the first implementation of optional network diagram functionality in Ping Monitor.

The feature is intended to let administrators manually document network topology inside the application using a simple drag-and-drop interface. Diagrams can include monitored endpoints, custom non-monitored devices, links, ports, VLAN notes, LAG/LACP notes, and other operational annotations.

Network diagrams are a visual documentation and status overlay feature. They must not change endpoint monitoring, dependency suppression, alerting, agent behaviour, or state evaluation.

## Design Principles

- Diagrams are optional and can be enabled or disabled in admin settings.
- Diagrams are stored in the main application database.
- Diagrams do not require a separate plugin database or external service.
- Monitored endpoints can be placed on diagrams.
- Non-monitored devices can also be created directly on diagrams.
- Links are manually drawn by the user.
- Link labels and notes can be used for ports, VLANs, LAGs, fibre links, wireless links, trunk details, and similar documentation.
- The feature must remain simple and predictable.
- The feature must not introduce automatic topology inference.
- The feature must not introduce SNMP discovery.
- The feature must not automatically create endpoints.
- The feature must not automatically create monitoring dependencies.

## Architectural Boundary

Network diagrams sit above the existing monitoring model.

The existing monitoring system remains authoritative for:

- endpoint state
- assignment state
- dependency suppression
- degraded evaluation
- alerts
- uptime calculations

The diagram system may display existing monitoring state, but it must not calculate or modify monitoring state.

A monitored diagram node references an existing endpoint or assignment, but the diagram node itself is not a monitored object.

A custom diagram-only device exists only for visual documentation and has no monitoring state unless it is later linked to a monitored endpoint.

## Feature Enablement

A global admin setting controls whether network diagrams are available.

Suggested setting:

`NetworkDiagramsEnabled`

When disabled:

- Diagram navigation should be hidden.
- Diagram pages should not be available to normal users.
- Existing diagram data should remain in the database.
- No diagram data should be deleted.
- Monitoring, agents, alerting, and state evaluation must continue unchanged.

This keeps the schema consistent across installations while allowing the feature to be hidden where it is not required.

## V0.1.1 Functional Scope

### Included

- Admin setting to enable or disable network diagrams.
- Diagram index/list page.
- Create, rename, edit, and delete diagrams.
- Drag monitored endpoints onto a diagram.
- Add custom non-monitored devices to a diagram.
- Move devices around the canvas.
- Draw links between devices.
- Edit link labels and notes.
- Edit device labels and notes.
- Show monitored endpoint status on diagram nodes.
- Save and reload diagram layout.

### Excluded from V0.1.1

- SNMP discovery.
- Automatic topology discovery.
- Automatic endpoint creation.
- Automatic dependency creation.
- Automatic link-state detection.
- Network remediation.
- Rack elevation views.
- Multi-user collaborative editing.
- Custom icon uploads.
- Complex structured VLAN database modelling.
- Diagram version history.
- Background floorplans or image overlays unless trivial.

## Diagram Concepts

### Diagram

A diagram is a saved canvas representing a network, site, rack, logical topology, or other infrastructure view.

Potential examples:

- Site overview
- Server rack
- Farm network
- Wireless bridge layout
- VLAN overview
- CCTV network
- Customer site
- WAN view

Suggested properties:

- Diagram ID
- Name
- Description
- Created timestamp
- Updated timestamp
- Created by user
- Last edited by user
- Canvas metadata

Canvas metadata may include:

- zoom level
- pan/viewport position
- grid setting
- optional background settings

### Diagram Node

A node is an object placed on a diagram.

Node types:

- Monitored endpoint node
- Custom device node
- Note/annotation node

A monitored endpoint node references an existing endpoint.

A custom device node exists only inside the diagram and does not affect monitoring.

Suggested properties:

- Node ID
- Diagram ID
- Node type
- Endpoint ID, nullable
- Display label
- Icon key
- X position
- Y position
- Width
- Height
- Notes
- Metadata JSON

For monitored endpoint nodes:

- The label should default to the endpoint name.
- The user may optionally override the display label on the diagram.
- The node should display current monitoring status where available.

For custom device nodes:

- The user provides the name/label.
- The user selects an icon/type.
- No live status is shown unless linked to a monitored endpoint later.

### Diagram Link

A link represents a manually drawn connection between two nodes.

Suggested properties:

- Link ID
- Diagram ID
- Source node ID
- Target node ID
- Source port label
- Target port label
- Link label
- Notes
- Link type/style
- Metadata JSON

Example link labels/notes:

- `Port 17 ↔ Port 2`
- `Gi1/0/24 ↔ eth0`
- `LACP 17-18`
- `VLANs 10, 20, 30 tagged`
- `VLAN 5 untagged`
- `10Gb fibre`
- `PTP wireless`
- `UPS protected`

## Status Display

Monitored endpoint nodes should display live status from the existing monitoring system.

Supported states:

- Up
- Degraded
- Down
- Suppressed
- Unknown

The diagram must not calculate these states itself.

Because endpoint state is assignment-scoped, a diagram node may need to display a summary when an endpoint has multiple assignments.

Suggested V0.1.1 behaviour:

- Node shows a simple summary state.
- Clicking or opening details shows per-agent/per-assignment state.
- Summary state is for display only and must not feed back into monitoring logic.

Suggested visual priority for diagram display:

1. Down
2. Unknown
3. Suppressed
4. Degraded
5. Up

This priority is only for visual urgency on diagrams. It does not redefine the monitoring state machine.

## Link Status

V0.1.1 should avoid claiming real link status.

Ping Monitor does not use SNMP and does not know actual switch/router interface state. Therefore, drawn links are documentation objects, not authoritative link-state monitors.

Possible V0.1.1 behaviour:

- Links are static/manual by default.
- Link labels and notes are user-defined.
- Automatic link-state inference is excluded.

Future versions may optionally add inferred visual hints, but these must be clearly labelled as inferred and must not affect monitoring state.

## Relationship to Dependencies

Diagram links must not automatically create or modify endpoint dependencies.

The dependency system remains separate and authoritative for suppression behaviour.

For V0.1.1:

- Drawing a link does not create a dependency.
- Deleting a link does not remove a dependency.
- Moving nodes does not affect dependencies.
- Diagram topology is visual documentation only.

Future possible enhancements:

- Overlay existing dependencies on a diagram.
- Show dependency arrows separately from physical/logical links.
- Warn when diagram links and dependency definitions appear inconsistent.
- Offer an explicit admin action to create dependencies from selected links.

Any future dependency integration must be explicit and confirmation-based.

## User Interface

### Diagram List Page

The diagram list page should show:

- Diagram name
- Description
- Last updated time
- Created by / updated by if available
- Open button
- Edit button for admins
- Delete button for admins

When diagrams are disabled, navigation should be hidden and direct access should show an appropriate unavailable message.

### Diagram Editor

The editor should provide:

- Main canvas
- Endpoint/device toolbox
- Properties panel or modal editor
- Save indication
- Delete controls with confirmation

### Toolbox

Suggested toolbox sections:

- Monitored endpoints
- Custom devices
- Notes/annotations

Monitored endpoints should be searchable/filterable.

Endpoint entries should show:

- Name
- Target/IP/hostname
- Icon
- Current status

Custom device options should initially reuse existing built-in icon concepts:

- Generic
- Switch
- Firewall
- Server
- Router
- Printer
- PC
- Laptop
- NAS
- Access point
- Camera
- Phone
- Cloud/Internet
- Note

### Canvas Behaviour

V0.1.1 canvas behaviour:

- Drag nodes onto canvas.
- Move nodes around.
- Select nodes.
- Edit selected node properties.
- Draw links between nodes.
- Select links.
- Edit selected link properties.
- Delete selected nodes/links with confirmation.
- Save layout.

Nice-to-have but not mandatory for V0.1.1:

- Snap to grid.
- Zoom and pan.
- Auto-arrange.
- Duplicate node.
- Undo/redo.

### Mobile Behaviour

Viewing diagrams should work on mobile.

Editing may be optimised for desktop/tablet first, but controls must not rely on hover-only behaviour.

Critical actions should use clear tap targets.

## Permissions

Suggested V0.1.1 permission model:

- Admins can create, edit, and delete diagrams.
- Users can view diagrams.
- Users cannot edit diagrams.

Endpoint visibility must remain server-side.

If a user cannot access an endpoint, the diagram should either:

- hide that monitored endpoint node, or
- show it as restricted without exposing endpoint details.

The safest V0.1.1 approach is to hide inaccessible endpoint details from non-admin users.

## Data Safety

Deleting a diagram should delete only diagram data.

Deleting a diagram must not delete:

- monitored endpoints
- agents
- monitor assignments
- check results
- endpoint states
- dependencies
- alerts
- event logs

Deleting a node that references a monitored endpoint should remove only the diagram node, not the endpoint.

Deleting a link should remove only the diagram link, not dependencies or monitoring data.

## Schema Impact

This feature requires database schema changes.

The release containing this feature must increment the required schema version.

Release metadata must declare the required schema version in the release manifest.

## Suggested V0.1.1 Definition

V0.1.1 introduces optional manually managed network diagrams. Admins can enable the feature, create diagrams, drag monitored endpoints or custom non-monitored devices onto a canvas, draw labelled links, and add notes such as ports, VLANs, and LAG details. Monitored endpoint nodes display current monitoring status, but diagrams are visual documentation only and do not alter monitoring, alerting, dependency suppression, or agent behaviour.

## Open Questions

- Should diagram viewing be available to read-only users in V0.1.1, or should diagrams be admin-only initially?
- Should a monitored endpoint be allowed to appear multiple times on the same diagram?
- Should custom devices be reusable across diagrams, or should they exist only inside one diagram?
- Should diagrams support folders/groups in V0.1.1, or just a flat list?
- Should the first implementation include zoom/pan, or keep the canvas fixed/simple?
- Should endpoint state summary be endpoint-wide or assignment-selected per node?
- Should link styling be simple text-only initially, or include selectable link types such as fibre, copper, wireless, trunk, and LAG?


## Current editor slice: monitored endpoint placement and draft links

This V0.1.1 editor slice keeps the current diagram editor client-side draft-only. It does not add diagram, node, or link persistence and it intentionally keeps the visible “Layout is not saved yet” messaging.

Implemented in this slice:

- The editor toolbox lists monitored endpoints visible through the existing endpoint query path.
- Endpoint toolbox entries can be added to the draft canvas as monitored endpoint visual nodes.
- Adding the same monitored endpoint more than once is allowed; each placement is a separate visual draft instance and does not create or update an endpoint.
- Custom draft devices remain available and draggable.
- Draw link mode lets users create basic client-side links between non-note nodes.
- Draft links can be selected, deleted, and edited with basic label, source port, target port, and notes metadata.
- Link labels and port text are rendered on the SVG link overlay when provided.
- Links are documentation/status-overlay objects only.

Still not implemented in this slice:

- Saved diagrams or persisted layouts.
- Automatic topology discovery.
- SNMP discovery.
- Automatic endpoint creation.
- Automatic monitoring dependency creation.
- Physical link-state detection.
- Monitoring, dependency suppression, alerting, or agent behaviour changes.

Diagram links must not be interpreted as monitoring dependencies or real physical link state. Deleting a diagram link removes only the draft visual link and must not alter endpoint dependencies, monitoring history, alerting, or agent configuration.

### Manual regression checklist for endpoint placement and draft links

1. Enable `NetworkDiagramsEnabled`.
2. Open the editor at 1920x1080.
3. Open the editor at 1366x768.
4. Confirm monitored endpoints appear in the toolbox.
5. Add a monitored endpoint to the canvas.
6. Add a custom device to the canvas.
7. Drag both nodes and confirm they remain within the visible canvas.
8. Switch to Draw link mode.
9. Draw a link between the monitored endpoint node and the custom device node.
10. Drag nodes after creating the link and confirm the line follows the node positions.
11. Select the link and edit label, source port, target port, and notes text.
12. Confirm the link label and port text display on or near the line.
13. Delete the selected link and confirm both nodes remain on the canvas.
14. Confirm deleting the visual link does not alter endpoint dependencies.
15. Disable `NetworkDiagramsEnabled` and confirm navigation, linked access, and direct page access are blocked.

## Current editor slice: selection, edit/delete, and group move

Issue: #499

The draft editor now supports selecting draft nodes and documentation-only visual links. Multiple nodes can be selected with Shift/Ctrl/Cmd-click, Select all nodes selects every current draft node, and Clear selection removes both node and link selection. Dragging any selected node moves the selected group together in virtual canvas/world coordinates so links remain connected while the group moves.

The Properties panel supports draft-only edits for exactly one selected node: diagram display label and notes. For monitored endpoint visual nodes, the panel shows endpoint details and states that changing the diagram label does not rename the monitored endpoint. When multiple nodes are selected, the panel shows the selected count and group delete action rather than single-node-only fields.

The link Properties panel supports draft-only edits for label, source port, target port, and notes, and repeats that visual links do not create or modify monitoring dependencies. Deleting a selected link removes only the draft visual link. Deleting selected node(s) removes only draft diagram nodes and attached draft visual links; it does not delete endpoints, assignments, monitoring data, dependencies, alerts, or agent state.

This remains a client-side draft-only slice. No saved diagram layout, database persistence, monitoring state evaluation, dependency suppression, alerting, Startup Gate, or agent behavior is changed. Marquee selection remains future work to avoid risking pan/zoom regressions.

## Link Styling/Label Enhancement Notes

Network diagram links now support documentation-only media/type metadata for Copper, Fibre, Wireless, LACP, VPN, Logical, and Other. The selected type controls editor and PDF styling only; wireless uses a dashed line, fibre is visually distinct from copper, and LACP/VPN/logical links use pattern/weight differences so the styling is not colour-only.

Link labels and notes are drawn on-canvas near the link midpoint, and multiple links between the same unordered node pair are grouped and offset deterministically so A → B and B → A links do not overlap after save/reload. These visual links remain separate from monitoring dependencies: creating, editing, deleting, saving, or exporting links must not alter endpoint monitoring, dependency suppression, state evaluation, alerting, agents, or Startup Gate behaviour.

## Link VLAN metadata addendum

Visual links may carry structured VLAN documentation metadata. VLAN entries support an ID, optional label, mode (Tagged, Untagged, Native, Management, Other), optional notes, and deterministic ordering. This metadata is intentionally scoped to the diagram link as documentation only: it must not configure network devices, infer topology, create endpoints, create dependencies, or influence monitoring state, suppression, or alerting.

## Read-only viewer/live status implementation notes

The read-only viewer is a separate saved-diagram route from the editor. It is intended for operations users who need to look at a saved diagram with live monitoring context, not for modifying the diagram.

Implementation boundaries:

- The viewer renders saved diagram geometry and documentation-only links.
- Editing tools, mutable inputs, save actions, delete actions, draw-link mode, and add-node controls are not rendered in viewer mode.
- Live data is provided by a lightweight JSON endpoint that reuses current assignment state and 24-hour assignment metrics.
- The live endpoint overlay shows server-calculated state, uptime, and RTT data only; it does not evaluate monitoring state itself.
- Multi-assignment endpoint nodes use the UI-only diagram urgency order Down, Unknown, Suppressed, Degraded, Up for the node badge while preserving per-assignment state details in the read-only details panel.
- Non-admin access is filtered using existing endpoint visibility rules; hidden monitored endpoint nodes and connected links are omitted from non-admin static diagram JSON and live overlay JSON.
- Diagram links remain visual documentation and still do not create monitoring dependencies or alter suppression.

Manual regression checklist for this slice:

1. Enable `NetworkDiagramsEnabled`.
2. Open the diagram list and confirm saved diagrams offer View as the primary action.
3. Open a saved diagram in View mode and confirm no toolbox, editable Properties form, Save, Delete selected, Draw link, or add-node controls are visible.
4. Confirm pan, mouse-wheel zoom, toolbar zoom, reset view, and fit content work.
5. Confirm monitored endpoint nodes show state, 24-hour uptime, and last RTT.
6. Confirm custom diagram nodes show diagram-only presentation and do not show fake live endpoint data.
7. Wait for the polling interval and confirm live data refresh timestamp changes without reloading the page.
8. Click a monitored node and confirm the details panel is read-only and includes endpoint/assignment status data.
9. Click a visual link and confirm the details panel is read-only and states that the link is visual documentation only.
10. Confirm admin users can navigate to Edit from the viewer.
11. Confirm non-admin users cannot access the edit/save/delete/export routes and do not receive endpoint details they cannot access.
12. Disable `NetworkDiagramsEnabled` and confirm viewer and live-data API access are blocked.
13. Confirm no endpoint, dependency, state-evaluation, alert, agent, or startup-gate behaviour changed.
