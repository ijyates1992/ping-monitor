# Endpoint performance graphs

The endpoint performance page now prioritizes readability by rendering **one graph at a time** in a large chart area.

## Default graph

- The initial graph is **RTT**.
- Graph switching is client-side and does not require a full page reload.

## Available graph types

- **RTT** (successful checks only)
- **Check outcomes** (failed and successful counts per bucket)
- **Jitter** (absolute delta between consecutive successful RTT samples)

## Readability goals

The page is designed as a focused analysis view:

- single active graph instead of stacked mini-graphs
- larger graph height and width to improve trace readability
- thinner line strokes and smaller points for cleaner rendering
- simplified axis tick density to reduce visual clutter
- dark-mode-aware axis, legend, and grid colors for contrast

## Dense data behavior

When the selected range has many points, the chart container supports horizontal scrolling.

- chart minimum width scales with point density
- operators can scroll horizontally to inspect dense time windows
- time range selection continues to apply to the currently viewed graph
