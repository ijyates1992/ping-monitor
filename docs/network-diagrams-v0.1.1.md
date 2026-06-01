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
