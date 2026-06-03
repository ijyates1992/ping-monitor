# Network Diagrams preview release preparation

Issue: #527

This document is the release-preparation checklist and release-notes draft for the first Network Diagrams preview. It is intentionally preparation-only: do not upload GitHub release assets and do not publish a GitHub release unless Ian explicitly instructs it.

## Intended version naming

Repository release tooling currently accepts only strict production-style versions in the form `Vx.x.x` for `scripts/build.ps1`. The documented release script rejects prerelease suffixes such as `V0.1.1-preview.1`.

For this preview release, use the project’s existing strict script convention unless the release tooling is intentionally changed first. The likely package version under current tooling is therefore `V0.1.1`, not `V0.1.1-preview.1`. If Ian wants a semver prerelease tag/package name, update and validate `scripts/build.ps1`, `docs/build-and-release.md`, updater version comparison rules, and manifest validation before building or publishing.

## Schema and release metadata confirmation

- Required schema version for the Network Diagrams preview is **18**.
- Schema 15 added `ApplicationSettings.NetworkDiagramsEnabled` with a safe default of `false`.
- Schema 16 added `NetworkDiagrams`, `NetworkDiagramNodes`, and `NetworkDiagramLinks`.
- Schema 17 added structured link metadata fields on `NetworkDiagramLinks` for media type, media subtype, link type, link speed, and LACP metadata.
- Schema 18 added `NetworkDiagramLinkVlans` for documentation-only VLAN metadata.
- `StartupGate.RequiredSchemaVersion` must remain aligned in production and development appsettings before release packaging.
- `scripts/build.ps1` and `scripts/build-dev.ps1` read the published `StartupGate.RequiredSchemaVersion` and write it to package `manifest.json` as `requiredSchemaVersion`.
- Startup Gate must continue to create/upgrade all Network Diagram tables/columns before normal web app services access the database.

## Feature scope included

- Optional `NetworkDiagramsEnabled` admin setting.
- Feature-gated Network Diagrams navigation, pages, viewer/editor routes, export routes, and JSON endpoints.
- Persistent diagrams, nodes, visual links, and VLAN metadata.
- Admin editor with monitored endpoint nodes, custom diagram nodes, notes, link drawing, link selection/edit/delete, multi-select/group move, pan/zoom, A-series canvas sizing, and save/reload persistence.
- Link metadata for media type, link type, fibre/media subtype where supported, speed, LACP member count/ports, labels, notes, and VLANs.
- Read-only viewer with saved-layout rendering, live endpoint status/uptime/RTT overlay, summary panel, node details, and link details.
- PDF export from saved diagram state.
- Native-scale PNG/SVG export from saved diagram state, with PNG preserving saved canvas dimensions at 1x.

## Release notes draft

### Network Diagrams preview

This preview introduces the first optional Network Diagrams implementation for Ping Monitor. Administrators can enable **NetworkDiagramsEnabled** and create saved topology/documentation diagrams containing monitored endpoint nodes, custom diagram-only devices, notes, and visual links.

Highlights:

- New optional Network Diagrams feature, disabled by default.
- Persistent diagram editor with monitored endpoint nodes, custom nodes, notes, visual links, pan/zoom, multi-select/group move, and A-series canvas sizing.
- Documentation metadata on links, including media type, link type, fibre/media subtype where supported, speed, LACP member details where supported, labels, ports, notes, and VLAN metadata.
- Read-only topology viewer with live overlays from existing monitoring state, including status, 24-hour uptime, and last RTT.
- Diagram summary panel with monitored/custom node counts, visual link counts, live state counts, and affected endpoint shortcuts.
- PDF export for saved diagrams.
- Native-scale PNG/SVG export for saved diagrams; PNG at 1x preserves saved canvas dimensions instead of compressing the diagram into a paper-size layout.
- Diagram links and link/VLAN/media metadata are documentation-only. They do not create endpoint dependencies, configure network devices, alter monitoring state, affect suppression, or change alerting.
- No agent protocol or agent package version change is expected for this release unless agent files change before final packaging.

Operator notes:

- This release requires database schema version **18**.
- Startup Gate performs schema creation/upgrade before the normal application starts.
- `NetworkDiagramsEnabled` defaults to `false`; operators must enable it in admin settings before diagram routes appear.
- Exports use the last saved diagram state. Unsaved editor changes must be saved before export.

Known limitations for preview:

- Network Diagrams are manual documentation/status-overlay views only. There is no SNMP discovery, topology inference, endpoint auto-creation, dependency auto-creation, physical link-state detection, or remediation.
- PDF export is available but still uses the paper-fit renderer; dense diagrams may need A3 export or larger spacing for readability.
- Configuration backup/restore does **not yet** include Network Diagrams. This is tracked as release-blocking follow-up #528 unless Ian explicitly accepts the limitation for this preview.
- Editing is admin-only and optimized for desktop/tablet-sized workflows; viewing is the supported read-only operational mode for non-admin users.

## Validation checklist

### Feature gate

When disabled:

- Monitoring navigation must hide Network Diagrams.
- Diagram list, viewer, editor, save/data/live-data APIs, PDF export, and image export must return blocked/not-found responses.
- Existing diagram rows must remain in the database and must not be deleted.

When enabled:

- Authenticated users can open the diagram list and read-only viewer.
- Admin users can create, edit, save, delete, and export diagrams.
- Non-admin users cannot open the editor or call write/export actions.

### Editor smoke checklist

1. Enable `NetworkDiagramsEnabled`.
2. Create a diagram.
3. Rename/edit diagram metadata if supported by the UI.
4. Add a monitored endpoint node.
5. Add a custom node.
6. Draw a visual link.
7. Edit link metadata, including media/link type and speed where supported.
8. Add VLAN metadata and save.
9. Delete a link and confirm endpoints/nodes remain.
10. Delete a node and confirm monitored endpoint records remain.
11. Multi-select nodes and group move them.
12. Pan and zoom the canvas.
13. Save, refresh, and confirm layout/link/VLAN metadata persists.
14. Confirm no endpoint dependencies, assignments, alerts, agents, check results, or monitoring state records changed because of diagram editing.

### Viewer smoke checklist

1. Open the read-only viewer.
2. Confirm editor controls are not visible.
3. Confirm live endpoint overlay appears with state, uptime, and RTT.
4. Confirm the summary panel appears when nothing is selected.
5. Select a node and confirm read-only node details appear.
6. Select a link and confirm read-only link details appear.
7. Wait for polling refresh and confirm it updates without a full page reload.
8. Confirm non-admin users remain read-only and hidden endpoint details are filtered.

### Export smoke checklist

1. Export PDF and confirm the response is a valid `application/pdf` document.
2. Export PNG and confirm the response is a valid PNG image.
3. Confirm PNG 1x dimensions match saved canvas dimensions.
4. Confirm export routes require authentication, admin role, and enabled feature gate.
5. Confirm exports read saved diagram data only and do not modify database rows.

### Backup/restore checklist

- Current status: implemented and tested


### Security and performance checklist

- Editor/save/delete/export actions are admin-only and follow existing antiforgery/write-action patterns.
- Viewer/data/live overlay responses filter endpoint details through existing visibility rules for non-admin users.
- Live overlay uses batched assignment/current-state and existing 24-hour metrics snapshots rather than scanning raw `CheckResults` on every viewer refresh.
- Viewer polling should not cause obvious CPU/disk pressure while left open.

## Build and packaging checklist

Run before final release approval:

```bash
dotnet build src/WebApp/PingMonitor.WebApp.slnx
dotnet test src/WebApp/PingMonitor.WebApp.slnx
pwsh ./scripts/build-dev.ps1
```

For an actual release package under the current strict version convention, use for example:

```powershell
pwsh ./scripts/build.ps1 -Version V0.1.1
```

Do not publish release assets or create the GitHub release from automation unless Ian explicitly instructs it.
