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

- A 4000 x 2828 A-series landscape diagram-unit virtual canvas rendered inside the existing visible editor area.
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

## Draft Editor Selection, Edit/Delete, and Group Move Slice

Issue: #499

This slice extends the client-side draft editor interactions without adding persistence. The visible “Layout is not saved yet” messaging remains accurate: node and link edits affect only the current browser draft diagram state.

Included in this slice:

- Draft node selection with visible selected-node highlighting.
- Draft visual link selection with visible selected-link highlighting.
- Shift/Ctrl/Cmd-click multi-selection for draft nodes.
- Select all nodes and clear selection toolbar actions.
- Group movement: dragging any selected node moves all selected draft nodes together in virtual canvas/world coordinates.
- Group movement remains bounded so selected draft nodes stay recoverable inside the virtual canvas.
- Single-node Properties panel editing for diagram display label and notes.
- Monitored endpoint node details in the Properties panel, with clear messaging that editing the diagram label does not rename the monitored endpoint.
- Link Properties panel editing for link label, source port, target port, and notes.
- Safe delete actions for selected draft nodes and selected draft visual links.
- Keyboard support for Ctrl/Cmd+A select all, Escape clear selection, and Delete/Backspace delete selection while focus is inside the editor and not inside a text field.

Draft-only safety boundary:

- Deleting a diagram node removes only the draft diagram node and any attached draft visual links.
- Deleting a monitored endpoint visual node does not delete the endpoint, monitoring data, assignments, dependencies, alerts, or agent state.
- Deleting a visual link does not delete or modify monitoring dependencies.
- Visual links remain documentation-only and do not create, change, or delete endpoint dependencies.
- This slice does not change endpoint monitoring, monitoring state evaluation, dependency suppression, alerting, agent behavior, Startup Gate behavior, SNMP discovery, topology inference, endpoint creation, or dependency creation.

Marquee selection is intentionally left as future work for this slice so it does not interfere with existing pan/zoom behavior.

### Manual Regression Checklist - Selection and Group Move

1. Enable `NetworkDiagramsEnabled`.
2. Open the editor at 1920x1080.
3. Open the editor at 1366x768.
4. Add several monitored endpoint nodes.
5. Add several custom nodes.
6. Draw links between nodes.
7. Select one node.
8. Edit the node display label.
9. Edit the node notes.
10. Select one link.
11. Edit the link label, source port, target port, and notes.
12. Delete a link and confirm connected nodes remain.
13. Delete a custom node and confirm attached visual links are removed.
14. Delete a monitored endpoint node and confirm the actual endpoint remains in the app.
15. Select multiple nodes with Shift/Ctrl-click.
16. Use Select all nodes.
17. Use Clear selection.
18. Drag selected nodes as a group to the right to create space on the left.
19. Confirm links follow moved nodes.
20. Confirm group drag works after zooming to 0.5x and 2x.
21. Confirm panning empty canvas still works.
22. Confirm draw-link mode still works.
23. Disable `NetworkDiagramsEnabled` and confirm nav/direct access are blocked.
24. Confirm no endpoint dependencies are created, changed, or deleted.

## Persistent editor slice (Issue #501)

This slice converts the network diagram editor from a browser-only draft into a basic persistent editor backed by the main application database.

Included persistence:

- Saved diagrams with name, optional description, canvas size, created/updated timestamps, and editor view state.
- Saved diagram nodes for monitored endpoint visual nodes, custom devices, and notes.
- Saved visual links with label, source port label, target port label, notes, link type, and metadata storage.
- Saved node positions in virtual world coordinates rather than browser screen coordinates.
- Saved pan and zoom state so refreshing the editor reloads the previous view.

Safety boundaries remain unchanged:

- Network diagrams are documentation/status-overlay only.
- Diagram links remain visual documentation only and do not create, update, or delete endpoint dependencies.
- Saving a monitored endpoint diagram node does not rename, update, create, or delete the monitored endpoint.
- Deleting a diagram node removes only the saved diagram node and attached visual links.
- Deleting a diagram removes only the saved diagram, diagram nodes, and visual links.
- Endpoint monitoring, dependency suppression, alerting, state evaluation, agents, assignments, check results, states, alerts, event logs, and monitoring history are not changed by diagram editing.

Operator behaviour:

- `NetworkDiagramsEnabled` still gates navigation and direct page/API access.
- Admin users can create, open, save, and delete diagrams in this first persistence slice.
- The editor tracks unsaved changes and warns before browser navigation when local edits have not been saved.
- Save failures keep the local dirty diagram state in the browser and show an error instead of discarding edits.
- Diagram deletion confirmation explicitly states: “This deletes only the saved network diagram. Monitoring endpoints and monitoring data will not be deleted.”

Schema/release notes:

- This is schema version 16.
- New tables: `NetworkDiagrams`, `NetworkDiagramNodes`, and `NetworkDiagramLinks`.
- Startup Gate schema apply creates the diagram tables explicitly and the required schema version metadata must remain aligned with release metadata.

Backup/restore follow-up:

- Network diagrams are configuration, not operational monitoring history.
- Configuration backup/restore inclusion for `NetworkDiagrams`, `NetworkDiagramNodes`, and `NetworkDiagramLinks` remains a required follow-up before publishing V0.1.1 if it is not completed in the release branch.

## A-series canvas sizing and PDF export slice

This slice adds paper-ratio canvas handling and saved-diagram PDF export without changing monitoring behaviour.

- New diagrams use an ISO 216 A-series landscape virtual canvas by default (`4000 × 2828` world units, approximately `1.414:1`). A4 and A3 use the same ratio, so the editor canvas maps predictably to paper export.
- Existing diagrams continue to load with their saved `CanvasWidth` and `CanvasHeight`. Legacy arbitrary-ratio diagrams are not silently normalised; the editor shows a warning and lets an admin choose an A-series canvas size when it is safe.
- The editor provides controlled landscape canvas size presets: Small, Medium, Large, and Extra large. Changing presets preserves node/link world coordinates.
- Shrinking or normalising to a smaller preset is blocked when any existing node would fall outside the target canvas, preventing accidental cropping.
- PDF export is server-side, authenticated, admin-only through the existing Network Diagrams controller, and still respects `NetworkDiagramsEnabled`.
- PDF export renders from the last saved diagram data in the database, not unsaved browser state. The editor warns before exporting when unsaved changes exist.
- PDF export supports A4 landscape and A3 landscape and scales the saved diagram content to fit the page with padding. The export includes the diagram title, UTC export timestamp, nodes, visual links, link labels/ports where present, node labels, practical notes, and the documentation-only footer.
- PDF export wraps, shrinks, and truncates node labels and secondary node text so exported text remains inside device boxes. Clipping is also applied to each exported node as a final safety guard.
- Very dense diagrams may require A3 export or larger canvas spacing for best readability; export does not silently change saved diagram geometry.
- Diagram links remain visual documentation only and do not create monitoring dependencies.
- This slice does not change endpoint monitoring, dependency suppression, alerting, agent runtime behaviour, topology discovery, SNMP, or automatic endpoint/dependency creation.

Manual regression checklist for this slice:

1. Create a new diagram and confirm the default world canvas reads `4000 × 2828` and is shown as A-series landscape.
2. Add monitored endpoint nodes and custom nodes, then draw visual links.
3. Save, expand the canvas to a larger preset, and confirm existing nodes keep their world positions.
4. Confirm pan, zoom, node dragging, link drawing, and Fit content still work after canvas size changes.
5. Attempt to shrink below existing node bounds and confirm the editor blocks the change with a clear message.
6. Export A4 and A3 PDFs and confirm the diagram title, timestamp, nodes, links behind nodes, readable labels, and documentation-only footer appear without editor UI/sidebar/toolbars.
7. Confirm long labels such as “Summerhouse Access Point”, “Living Room Access Point”, and dense Trading VM labels stay inside node boxes and do not overlap adjacent nodes.
8. Try exporting with unsaved changes and confirm the editor warns that export uses the last saved diagram.
9. Disable `NetworkDiagramsEnabled` and confirm the export route is blocked.
10. Confirm no endpoint dependency, alerting, state-evaluation, or agent data is created or changed.
