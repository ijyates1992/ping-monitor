(() => {
    const viewer = document.querySelector('[data-network-diagram-viewer]');
    if (!viewer) {
        return;
    }

    const nav = document.querySelector('.site-nav');
    const loadUrl = viewer.dataset.loadUrl || '';
    const liveDataUrl = viewer.dataset.liveDataUrl || '';
    const canvasHost = viewer.querySelector('[data-diagram-canvas-host]');
    const canvas = viewer.querySelector('[data-diagram-canvas]');
    const world = viewer.querySelector('[data-diagram-world]');
    const nodeLayer = viewer.querySelector('[data-node-layer]');
    const linkLayer = viewer.querySelector('[data-link-layer]');
    const emptyState = viewer.querySelector('[data-empty-state]');
    const zoomLabel = viewer.querySelector('[data-zoom-label]');
    const sizeLabel = viewer.querySelector('[data-canvas-size]');
    const refreshStatus = viewer.querySelector('[data-refresh-status]');
    const nodeDetail = viewer.querySelector('[data-node-detail]');
    const linkDetail = viewer.querySelector('[data-link-detail]');
    const noSelectionPanel = viewer.querySelector('[data-no-selection-panel]');
    const exportPdfButton = viewer.querySelector('[data-export-pdf]');
    const exportPngButton = viewer.querySelector('[data-export-png]');
    const exportSvgButton = viewer.querySelector('[data-export-svg]');
    const exportPaperSelect = viewer.querySelector('[data-export-paper]');
    const exportScaleSelect = viewer.querySelector('[data-export-scale]');

    const zoomStep = 1.15;
    const minZoom = 0.15;
    const maxZoom = 3;
    const parallelLinkOffsetStep = 34;
    const refreshIntervalMs = 20000;
    const statePriority = { Down: 5, Unknown: 4, Suppressed: 3, Degraded: 2, Up: 1 };
    const state = {
        nodes: [],
        links: [],
        overlayByNodeId: new Map(),
        lastOverlayRefreshUtc: null,
        summaryMessage: '',
        selectedNodeId: null,
        selectedLinkId: null,
        zoom: 1,
        panX: 0,
        panY: 0,
        virtualCanvasWidth: 4000,
        virtualCanvasHeight: 2828
    };
    let panState = null;
    let refreshTimer = null;

    function updateNavHeight() {
        const height = nav ? nav.getBoundingClientRect().height : 0;
        document.documentElement.style.setProperty('--network-diagrams-nav-height', `${height}px`);
    }

    function applyViewTransform() {
        world.style.width = `${state.virtualCanvasWidth}px`;
        world.style.height = `${state.virtualCanvasHeight}px`;
        world.style.transform = `translate(${state.panX}px, ${state.panY}px) scale(${state.zoom})`;
        linkLayer.setAttribute('viewBox', `0 0 ${state.virtualCanvasWidth} ${state.virtualCanvasHeight}`);
        linkLayer.setAttribute('width', String(state.virtualCanvasWidth));
        linkLayer.setAttribute('height', String(state.virtualCanvasHeight));
        canvasHost.style.setProperty('--diagram-world-width', `${state.virtualCanvasWidth}px`);
        canvasHost.style.setProperty('--diagram-world-height', `${state.virtualCanvasHeight}px`);
        if (zoomLabel) {
            zoomLabel.textContent = `${Math.round(state.zoom * 100)}%`;
        }
    }

    function updateCanvasSize() {
        updateNavHeight();
        const rect = canvas.getBoundingClientRect();
        if (sizeLabel) {
            sizeLabel.textContent = `${Math.round(rect.width)} x ${Math.round(rect.height)} visible • ${state.virtualCanvasWidth} x ${state.virtualCanvasHeight} world • ${Math.round(state.zoom * 100)}% zoom`;
        }
        applyViewTransform();
        renderLinks();
    }

    function getNodeWidth(node) { return node.element.offsetWidth || node.width || 178; }
    function getNodeHeight(node) { return node.element.offsetHeight || node.height || 78; }
    function getNodeCenter(node) { return { x: node.x + getNodeWidth(node) / 2, y: node.y + getNodeHeight(node) / 2 }; }
    function findNodeById(id) { return state.nodes.find(node => node.id === id); }
    function findLinkById(id) { return state.links.find(link => link.id === id); }
    function createSvgElement(name) { return document.createElementNS('http://www.w3.org/2000/svg', name); }
    function escapeHtml(value) {
        return String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
    }

    function normalizeNodeType(serverType) {
        if (serverType === 'MonitoredEndpoint') { return { nodeType: 'monitored-endpoint', nodeKind: 'monitored-endpoint' }; }
        if (serverType === 'Note') { return { nodeType: 'note', nodeKind: 'custom-device' }; }
        return { nodeType: 'generic-device', nodeKind: 'custom-device' };
    }

    function formatNodeType(type) {
        if (type === 'monitored-endpoint') { return 'monitored endpoint'; }
        if (type === 'note') { return 'note'; }
        return 'custom device';
    }

    function createNodeElement(node) {
        const element = document.createElement('div');
        element.className = 'diagram-node diagram-viewer-node';
        element.tabIndex = 0;
        element.setAttribute('role', 'button');
        element.setAttribute('aria-label', `${node.label} ${formatNodeType(node.nodeType)} node`);
        element.dataset.nodeId = node.id;
        element.dataset.nodeKind = node.nodeKind;
        element.dataset.selected = 'false';

        const symbol = document.createElement('span');
        symbol.className = 'diagram-node-symbol';
        symbol.textContent = node.iconKey || 'DEV';
        symbol.setAttribute('aria-hidden', 'true');

        const main = document.createElement('span');
        main.className = 'diagram-node-main';
        main.innerHTML = `<span class="diagram-node-name">${escapeHtml(node.label)}</span><span class="diagram-node-type">${node.nodeKind === 'monitored-endpoint' ? 'monitored endpoint' : formatNodeType(node.nodeType)}</span>`;

        if (node.nodeKind === 'monitored-endpoint') {
            main.innerHTML += '<span class="diagram-live-state" data-live-state>State: pending</span><span class="diagram-live-metrics" data-live-metrics>24h: — • RTT: —</span>';
        } else {
            main.innerHTML += '<span class="diagram-node-draft diagram-node-readonly">Diagram only</span>';
        }

        element.append(symbol, main);
        element.addEventListener('click', event => { selectNode(node.id); event.stopPropagation(); });
        element.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') { selectNode(node.id); event.preventDefault(); }
        });
        return element;
    }

    function addLoadedNode(savedNode) {
        const normalized = normalizeNodeType(savedNode.nodeType);
        const node = {
            id: savedNode.nodeId,
            nodeType: normalized.nodeType,
            nodeKind: normalized.nodeKind,
            endpointId: savedNode.endpointId || '',
            label: savedNode.displayLabel || 'Diagram node',
            iconKey: savedNode.iconKey || 'DEV',
            notes: savedNode.notes || '',
            x: Number(savedNode.x) || 0,
            y: Number(savedNode.y) || 0,
            width: Number(savedNode.width) || 178,
            height: Number(savedNode.height) || 78
        };
        node.element = createNodeElement(node);
        node.element.style.width = `${node.width}px`;
        node.element.style.minHeight = `${node.height}px`;
        node.element.style.transform = `translate(${Math.round(node.x)}px, ${Math.round(node.y)}px)`;
        nodeLayer.appendChild(node.element);
        state.nodes.push(node);
    }

    function normalizeMediaType(value, linkType) {
        const media = String(value || '').trim().toLowerCase();
        if (media === 'fibre' || media === 'fiber') { return 'Fibre'; }
        if (['wireless', 'dac', 'vpn', 'virtual', 'other'].includes(media)) { return media === 'dac' ? 'DAC' : media.charAt(0).toUpperCase() + media.slice(1); }
        if (String(linkType || '').toLowerCase() === 'vpn') { return 'VPN'; }
        return 'Copper';
    }

    function normalizeLinkType(value) {
        const allowed = ['Standard', 'Trunk', 'Access', 'LACP', 'PointToPoint', 'Backhaul', 'WAN', 'Management', 'Logical', 'Other'];
        const requested = String(value || '').trim();
        return allowed.find(item => item.toLowerCase() === requested.toLowerCase()) || 'Standard';
    }

    function normalizeVlans(vlans) {
        return Array.isArray(vlans) ? vlans.map((vlan, index) => ({ vlanId: vlan?.vlanId == null ? '' : String(vlan.vlanId), name: String(vlan?.name || ''), mode: String(vlan?.mode || 'Tagged'), notes: String(vlan?.notes || ''), sortOrder: Number(vlan?.sortOrder) || index })).sort((a, b) => a.sortOrder - b.sortOrder) : [];
    }

    function formatSpeed(value, unit) { return value == null || value === '' ? '' : `${value} ${unit || 'Mbps'}`; }
    function buildVlanSummary(link) {
        const vlans = normalizeVlans(link.vlans).filter(vlan => vlan.vlanId);
        if (vlans.length === 0) { return ''; }
        return vlans.map(vlan => `${vlan.mode}:${vlan.vlanId}${vlan.name ? ` ${vlan.name}` : ''}`).join(' · ');
    }
    function truncate(value, max) { const text = String(value || '').replace(/\s+/g, ' ').trim(); return text.length <= max ? text : `${text.slice(0, max - 1)}…`; }
    function buildLinkSummary(link) {
        const type = normalizeLinkType(link.linkType);
        const media = normalizeMediaType(link.mediaType, link.linkType);
        const speed = formatSpeed(link.linkSpeedValue, link.linkSpeedUnit);
        return [type === 'Standard' ? '' : type, speed, media.toLowerCase()].filter(Boolean).join(' ');
    }
    function buildVisibleLinkLabel(link) {
        const ports = normalizeLinkType(link.linkType) !== 'LACP' && (link.sourcePort || link.targetPort) ? `${link.sourcePort || '?'} ↔ ${link.targetPort || '?'}` : '';
        return truncate([buildLinkSummary(link), link.label, ports, buildVlanSummary(link), link.notes].filter(Boolean).join(' • '), 116);
    }

    function getUnorderedLinkPairKey(link) { return [link.sourceNodeId, link.targetNodeId].sort().join('::'); }
    function getParallelOffsetIndexes() {
        const groups = new Map();
        state.links.forEach(link => { const key = getUnorderedLinkPairKey(link); const group = groups.get(key) || []; group.push(link); groups.set(key, group); });
        const offsets = new Map();
        groups.forEach(group => { const ordered = [...group].sort((a, b) => String(a.id).localeCompare(String(b.id))); const center = (ordered.length - 1) / 2; ordered.forEach((link, index) => offsets.set(link.id, index - center)); });
        return offsets;
    }

    function buildLinkGeometry(source, target, offsetIndex) {
        const start = getNodeCenter(source);
        const end = getNodeCenter(target);
        const dx = end.x - start.x;
        const dy = end.y - start.y;
        const length = Math.hypot(dx, dy) || 1;
        const perpendicularX = -dy / length;
        const perpendicularY = dx / length;
        const offset = offsetIndex * parallelLinkOffsetStep;
        const control = { x: (start.x + end.x) / 2 + perpendicularX * offset, y: (start.y + end.y) / 2 + perpendicularY * offset };
        const midpoint = { x: start.x * 0.25 + control.x * 0.5 + end.x * 0.25, y: start.y * 0.25 + control.y * 0.5 + end.y * 0.25 };
        return { path: `M ${start.x} ${start.y} Q ${control.x} ${control.y} ${end.x} ${end.y}`, label: { x: midpoint.x + perpendicularX * 14, y: midpoint.y + perpendicularY * 14 - 4 } };
    }

    function renderLinks() {
        linkLayer.replaceChildren();
        const offsetIndexes = getParallelOffsetIndexes();
        state.links.forEach(link => {
            const source = findNodeById(link.sourceNodeId);
            const target = findNodeById(link.targetNodeId);
            if (!source || !target) { return; }
            const geometry = buildLinkGeometry(source, target, offsetIndexes.get(link.id) || 0);
            const group = createSvgElement('g');
            group.classList.add('diagram-link-group');
            group.dataset.linkId = link.id;
            group.dataset.selected = state.selectedLinkId === link.id ? 'true' : 'false';
            group.dataset.mediaType = normalizeMediaType(link.mediaType, link.linkType).toLowerCase();
            group.dataset.linkType = normalizeLinkType(link.linkType).toLowerCase();
            const line = createSvgElement('path');
            line.classList.add('diagram-link-line');
            line.setAttribute('d', geometry.path);
            const hit = createSvgElement('path');
            hit.classList.add('diagram-link-hit');
            hit.setAttribute('d', geometry.path);
            const select = event => { selectLink(link.id); event.preventDefault(); event.stopPropagation(); };
            line.addEventListener('pointerdown', select);
            hit.addEventListener('pointerdown', select);
            group.append(line, hit);
            const labelText = buildVisibleLinkLabel(link);
            if (labelText) {
                const label = createSvgElement('text');
                label.classList.add('diagram-link-label');
                label.setAttribute('x', String(geometry.label.x));
                label.setAttribute('y', String(geometry.label.y));
                label.setAttribute('text-anchor', 'middle');
                label.textContent = labelText;
                label.addEventListener('pointerdown', select);
                group.appendChild(label);
            }
            linkLayer.appendChild(group);
        });
    }

    function setEmptyState() { emptyState.hidden = state.nodes.length > 0; }
    function formatRtt(value) { return value == null ? '—' : `${Number(value).toFixed(1)} ms`; }
    function formatDate(value) { return value ? new Date(value).toLocaleString() : '—'; }
    function stateLabel(value) { return value || 'Unknown'; }
    function normalizeSummaryState(value) {
        const text = String(value || 'Unknown').trim().toLowerCase();
        if (text === 'up') { return 'Up'; }
        if (text === 'degraded') { return 'Degraded'; }
        if (text === 'down') { return 'Down'; }
        if (text === 'suppressed') { return 'Suppressed'; }
        return 'Unknown';
    }

    function getNodeSummaryState(node) {
        if (node.nodeKind !== 'monitored-endpoint') { return null; }
        return normalizeSummaryState(state.overlayByNodeId.get(node.id)?.summaryStateLabel);
    }

    function formatAssignmentSummary(overlay) {
        const count = Array.isArray(overlay?.assignments) ? overlay.assignments.length : 0;
        if (count === 0) { return ''; }
        return `${count} assignment${count === 1 ? '' : 's'}`;
    }

    function buildEndpointSummaryItem(node, overlay, summaryState) {
        const label = overlay?.endpointName || node.label;
        const uptime = overlay?.uptimeDisplay || '—';
        const assignmentSummary = formatAssignmentSummary(overlay);
        return `<li><button type="button" class="diagram-summary-endpoint" data-summary-node-id="${escapeHtml(node.id)}"><span class="diagram-summary-endpoint-main"><strong>${escapeHtml(label)}</strong><span>${escapeHtml(summaryState)} • RTT ${escapeHtml(formatRtt(overlay?.lastRttMs))} • 24h ${escapeHtml(uptime)}</span></span>${assignmentSummary ? `<span class="diagram-summary-endpoint-meta">${escapeHtml(assignmentSummary)}</span>` : ''}</button></li>`;
    }

    function renderAffectedSection(title, entries) {
        const body = entries.length > 0
            ? `<ul class="diagram-summary-endpoint-list">${entries.join('')}</ul>`
            : '<p class="diagram-summary-none">None</p>';
        return `<section class="diagram-summary-section"><h3>${escapeHtml(title)}</h3>${body}</section>`;
    }

    function renderSummaryPanel() {
        if (!noSelectionPanel) { return; }
        const monitoredNodes = state.nodes.filter(node => node.nodeKind === 'monitored-endpoint');
        const customNodes = state.nodes.filter(node => node.nodeKind !== 'monitored-endpoint');
        const stateCounts = { Up: 0, Degraded: 0, Down: 0, Suppressed: 0, Unknown: 0 };
        const affected = { Down: [], Degraded: [], Suppressed: [], Unknown: [] };
        const highestRtt = [];

        monitoredNodes.forEach(node => {
            const overlay = state.overlayByNodeId.get(node.id);
            const summaryState = getNodeSummaryState(node);
            stateCounts[summaryState] = (stateCounts[summaryState] || 0) + 1;
            if (affected[summaryState]) {
                affected[summaryState].push(buildEndpointSummaryItem(node, overlay, summaryState));
            }
            if (overlay?.lastRttMs != null && Number.isFinite(Number(overlay.lastRttMs))) {
                highestRtt.push({ node, overlay, lastRttMs: Number(overlay.lastRttMs) });
            }
        });

        highestRtt.sort((a, b) => b.lastRttMs - a.lastRttMs);
        const highestRttItems = highestRtt.slice(0, 5).map(item => buildEndpointSummaryItem(item.node, item.overlay, getNodeSummaryState(item.node)));
        const refreshedAt = state.lastOverlayRefreshUtc ? formatDate(state.lastOverlayRefreshUtc) : 'Pending';
        const countCards = [
            ['Total nodes', state.nodes.length],
            ['Monitored', monitoredNodes.length],
            ['Diagram-only', customNodes.length],
            ['Visual links', state.links.length],
            ['Up', stateCounts.Up],
            ['Degraded', stateCounts.Degraded],
            ['Down', stateCounts.Down],
            ['Suppressed', stateCounts.Suppressed],
            ['Unknown', stateCounts.Unknown]
        ].map(([label, value]) => `<div class="diagram-summary-count"><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`).join('');

        noSelectionPanel.innerHTML = `<div class="diagram-summary-header"><h3>Diagram live summary</h3><span>Live overlay refresh: ${escapeHtml(refreshedAt)}</span></div><div class="diagram-summary-count-grid">${countCards}</div>${state.summaryMessage ? `<p class="diagram-summary-message" role="status">${escapeHtml(state.summaryMessage)}</p>` : '<p class="diagram-summary-message" data-summary-action-status></p>'}<div class="diagram-summary-sections">${renderAffectedSection('Down endpoints', affected.Down)}${renderAffectedSection('Degraded endpoints', affected.Degraded)}${renderAffectedSection('Suppressed endpoints', affected.Suppressed)}${renderAffectedSection('Unknown endpoints', affected.Unknown)}${highestRttItems.length > 0 ? renderAffectedSection('Highest RTT', highestRttItems) : ''}</div><p class="toolbox-help">Viewer overlays existing monitoring status only. Visual links remain documentation-only.</p>`;
    }

    function applyOverlay() {
        state.nodes.forEach(node => {
            if (node.nodeKind !== 'monitored-endpoint') { return; }
            const overlay = state.overlayByNodeId.get(node.id);
            const stateValue = overlay?.summaryStateLabel || 'Unknown';
            node.element.dataset.liveState = stateValue.toLowerCase();
            const stateElement = node.element.querySelector('[data-live-state]');
            const metricsElement = node.element.querySelector('[data-live-metrics]');
            if (stateElement) { stateElement.textContent = `State: ${stateLabel(stateValue)}`; }
            if (metricsElement) { metricsElement.textContent = `24h: ${overlay?.uptimeDisplay || '—'} • RTT: ${formatRtt(overlay?.lastRttMs)}`; }
        });
        updateDetails();
    }

    function clampPanForView() {
        const rect = canvas.getBoundingClientRect();
        const margin = 120;
        const scaledWidth = state.virtualCanvasWidth * state.zoom;
        const scaledHeight = state.virtualCanvasHeight * state.zoom;
        if (scaledWidth <= rect.width) {
            state.panX = (rect.width - scaledWidth) / 2;
        } else {
            state.panX = Math.min(margin, Math.max(rect.width - scaledWidth - margin, state.panX));
        }
        if (scaledHeight <= rect.height) {
            state.panY = (rect.height - scaledHeight) / 2;
        } else {
            state.panY = Math.min(margin, Math.max(rect.height - scaledHeight - margin, state.panY));
        }
    }

    function centreOnNode(nodeId, preferredZoom = 1) {
        const node = findNodeById(nodeId);
        if (!node) {
            state.summaryMessage = 'That endpoint is no longer available on this diagram.';
            renderSummaryPanel();
            return false;
        }
        const rect = canvas.getBoundingClientRect();
        const center = getNodeCenter(node);
        state.zoom = Math.min(Math.max(Math.max(preferredZoom, 0.75), minZoom), maxZoom);
        state.panX = rect.width / 2 - center.x * state.zoom;
        state.panY = rect.height / 2 - center.y * state.zoom;
        clampPanForView();
        updateCanvasSize();
        selectNode(node.id);
        node.element.focus({ preventScroll: true });
        state.summaryMessage = '';
        return true;
    }

    async function refreshLiveData() {
        if (!liveDataUrl) { return; }
        try {
            const response = await fetch(liveDataUrl, { headers: { Accept: 'application/json' } });
            if (!response.ok) { throw new Error(`HTTP ${response.status}`); }
            const data = await response.json();
            state.overlayByNodeId = new Map((data.nodes || []).map(node => [node.nodeId, node]));
            state.lastOverlayRefreshUtc = data.refreshedAtUtc || new Date().toISOString();
            applyOverlay();
            if (refreshStatus) {
                refreshStatus.dataset.error = 'false';
                refreshStatus.textContent = `Live overlay refreshed ${new Date(data.refreshedAtUtc || Date.now()).toLocaleTimeString()} • polling every 20s`;
            }
        } catch (error) {
            if (refreshStatus) {
                refreshStatus.dataset.error = 'true';
                refreshStatus.textContent = 'Live overlay stale; refresh failed.';
            }
        }
    }

    function updateSelectionDom() {
        state.nodes.forEach(node => { node.element.dataset.selected = node.id === state.selectedNodeId ? 'true' : 'false'; });
        renderLinks();
    }

    function selectNode(id) { state.selectedNodeId = id; state.selectedLinkId = null; updateSelectionDom(); updateDetails(); }
    function selectLink(id) { state.selectedNodeId = null; state.selectedLinkId = id; updateSelectionDom(); updateDetails(); }
    function clearSelection() { state.selectedNodeId = null; state.selectedLinkId = null; updateSelectionDom(); updateDetails(); }

    function updateDetails() {
        const selectedNode = state.selectedNodeId ? findNodeById(state.selectedNodeId) : null;
        const selectedLink = state.selectedLinkId ? findLinkById(state.selectedLinkId) : null;
        noSelectionPanel.hidden = Boolean(selectedNode || selectedLink);
        if (!selectedNode && !selectedLink) { renderSummaryPanel(); }
        nodeDetail.hidden = !selectedNode;
        linkDetail.hidden = !selectedLink;
        if (selectedNode) {
            const overlay = state.overlayByNodeId.get(selectedNode.id);
            const assignmentRows = (overlay?.assignments || []).map(a => `<li><strong>${escapeHtml(a.agentName)}</strong>: ${escapeHtml(a.stateLabel)} • 24h ${escapeHtml(a.uptimeDisplay)} • RTT ${escapeHtml(formatRtt(a.lastRttMs))}${a.suppressedByEndpointName ? ` • Suppressed by ${escapeHtml(a.suppressedByEndpointName)}` : ''}</li>`).join('');
            nodeDetail.innerHTML = `<h3>${escapeHtml(selectedNode.label)}</h3><dl class="diagram-property-summary"><div><dt>Type</dt><dd>${escapeHtml(formatNodeType(selectedNode.nodeType))}</dd></div>${overlay ? `<div><dt>Endpoint</dt><dd>${escapeHtml(overlay.endpointName)}<br>${escapeHtml(overlay.target)}</dd></div><div><dt>Summary state</dt><dd>${escapeHtml(overlay.summaryStateLabel)}</dd></div><div><dt>24h uptime</dt><dd>${escapeHtml(overlay.uptimeDisplay)}</dd></div><div><dt>Last RTT</dt><dd>${escapeHtml(formatRtt(overlay.lastRttMs))}</dd></div><div><dt>Last check</dt><dd>${escapeHtml(formatDate(overlay.lastCheckUtc))}</dd></div>` : '<div><dt>Live data</dt><dd>No visible live endpoint data.</dd></div>'}</dl>${assignmentRows ? `<h3>Assignments</h3><ul class="diagram-viewer-assignment-list">${assignmentRows}</ul>` : ''}${selectedNode.notes ? `<h3>Notes</h3><p>${escapeHtml(selectedNode.notes)}</p>` : ''}`;
        }
        if (selectedLink) {
            linkDetail.innerHTML = `<h3>Visual link</h3><p class="toolbox-help">Visual link only; does not create monitoring dependency.</p><dl class="diagram-property-summary"><div><dt>Media</dt><dd>${escapeHtml(normalizeMediaType(selectedLink.mediaType, selectedLink.linkType))}</dd></div><div><dt>Link type</dt><dd>${escapeHtml(normalizeLinkType(selectedLink.linkType))}</dd></div><div><dt>Speed</dt><dd>${escapeHtml(formatSpeed(selectedLink.linkSpeedValue, selectedLink.linkSpeedUnit) || '—')}</dd></div><div><dt>Ports</dt><dd>${escapeHtml([selectedLink.sourcePort || '?', selectedLink.targetPort || '?'].join(' ↔ '))}</dd></div><div><dt>VLANs</dt><dd>${escapeHtml(buildVlanSummary(selectedLink) || '—')}</dd></div><div><dt>Notes</dt><dd>${escapeHtml(selectedLink.notes || '—')}</dd></div></dl>`;
        }
    }

    function zoomAt(clientX, clientY, requestedZoom) {
        const nextZoom = Math.min(Math.max(requestedZoom, minZoom), maxZoom);
        const rect = canvas.getBoundingClientRect();
        const worldX = (clientX - rect.left - state.panX) / state.zoom;
        const worldY = (clientY - rect.top - state.panY) / state.zoom;
        state.zoom = nextZoom;
        state.panX = clientX - rect.left - worldX * nextZoom;
        state.panY = clientY - rect.top - worldY * nextZoom;
        updateCanvasSize();
    }

    function resetView() { state.zoom = 1; state.panX = 0; state.panY = 0; updateCanvasSize(); }
    function fitContent() {
        if (state.nodes.length === 0) { resetView(); return; }
        const bounds = state.nodes.reduce((acc, node) => ({ minX: Math.min(acc.minX, node.x), minY: Math.min(acc.minY, node.y), maxX: Math.max(acc.maxX, node.x + getNodeWidth(node)), maxY: Math.max(acc.maxY, node.y + getNodeHeight(node)) }), { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity });
        const rect = canvas.getBoundingClientRect();
        const padding = 80;
        state.zoom = Math.min(Math.max(Math.min((rect.width - padding) / Math.max(bounds.maxX - bounds.minX, 1), (rect.height - padding) / Math.max(bounds.maxY - bounds.minY, 1)), minZoom), maxZoom);
        state.panX = (rect.width - (bounds.maxX - bounds.minX) * state.zoom) / 2 - bounds.minX * state.zoom;
        state.panY = (rect.height - (bounds.maxY - bounds.minY) * state.zoom) / 2 - bounds.minY * state.zoom;
        updateCanvasSize();
    }

    function beginPan(event) {
        if (event.target !== canvas && event.target !== world && event.target !== linkLayer) { return; }
        clearSelection();
        panState = { pointerId: event.pointerId, startX: event.clientX, startY: event.clientY, panX: state.panX, panY: state.panY };
        canvas.dataset.panning = 'true';
        canvas.setPointerCapture(event.pointerId);
        event.preventDefault();
    }
    function movePan(event) {
        if (!panState || panState.pointerId !== event.pointerId) { return; }
        state.panX = panState.panX + event.clientX - panState.startX;
        state.panY = panState.panY + event.clientY - panState.startY;
        applyViewTransform();
        event.preventDefault();
    }
    function endPan(event) {
        if (!panState || panState.pointerId !== event.pointerId) { return; }
        panState = null;
        canvas.dataset.panning = 'false';
        canvas.releasePointerCapture(event.pointerId);
    }

    async function loadDiagram() {
        const response = await fetch(loadUrl, { headers: { Accept: 'application/json' } });
        if (!response.ok) {
            emptyState.textContent = 'Failed to load diagram.';
            return;
        }
        const diagram = await response.json();
        state.virtualCanvasWidth = diagram.canvasWidth || state.virtualCanvasWidth;
        state.virtualCanvasHeight = diagram.canvasHeight || state.virtualCanvasHeight;
        state.panX = diagram.viewportPanX || 0;
        state.panY = diagram.viewportPanY || 0;
        state.zoom = diagram.viewportZoom || 1;
        nodeLayer.replaceChildren();
        state.nodes = [];
        (diagram.nodes || []).forEach(addLoadedNode);
        state.links = (diagram.links || []).map(link => ({ id: link.linkId, sourceNodeId: link.sourceNodeId, targetNodeId: link.targetNodeId, label: link.label || '', sourcePort: link.sourcePortLabel || '', targetPort: link.targetPortLabel || '', notes: link.notes || '', mediaType: normalizeMediaType(link.mediaType, link.linkType), linkType: normalizeLinkType(link.linkType), linkSpeedValue: link.linkSpeedValue, linkSpeedUnit: link.linkSpeedUnit, vlans: normalizeVlans(link.vlans) }));
        setEmptyState();
        updateCanvasSize();
        updateDetails();
        await refreshLiveData();
        refreshTimer = window.setInterval(refreshLiveData, refreshIntervalMs);
    }

    viewer.querySelector('[data-zoom-in]')?.addEventListener('click', () => zoomAt(canvas.getBoundingClientRect().left + canvas.clientWidth / 2, canvas.getBoundingClientRect().top + canvas.clientHeight / 2, state.zoom * zoomStep));
    viewer.querySelector('[data-zoom-out]')?.addEventListener('click', () => zoomAt(canvas.getBoundingClientRect().left + canvas.clientWidth / 2, canvas.getBoundingClientRect().top + canvas.clientHeight / 2, state.zoom / zoomStep));
    viewer.querySelector('[data-reset-view]')?.addEventListener('click', resetView);
    viewer.querySelector('[data-fit-content]')?.addEventListener('click', fitContent);
    exportPdfButton?.addEventListener('click', () => { const base = exportPdfButton.dataset.exportPdfUrl; if (base) { window.location.href = `${base}?paper=${encodeURIComponent(exportPaperSelect?.value || 'A4')}`; } });
    const exportImage = button => { const base = button?.dataset.exportImageUrl; if (base) { const separator = base.includes('?') ? '&' : '?'; window.location.href = `${base}${separator}scale=${encodeURIComponent(exportScaleSelect?.value || '1')}&background=light`; } };
    exportPngButton?.addEventListener('click', () => exportImage(exportPngButton));
    exportSvgButton?.addEventListener('click', () => exportImage(exportSvgButton));
    noSelectionPanel?.addEventListener('click', event => {
        const target = event.target instanceof Element ? event.target : event.target?.parentElement;
        const button = target?.closest('[data-summary-node-id]');
        if (!button) { return; }
        centreOnNode(button.dataset.summaryNodeId, 1);
    });
    canvas.addEventListener('pointerdown', beginPan);
    canvas.addEventListener('pointermove', movePan);
    canvas.addEventListener('pointerup', endPan);
    canvas.addEventListener('pointercancel', endPan);
    canvas.addEventListener('wheel', event => { zoomAt(event.clientX, event.clientY, state.zoom * (event.deltaY < 0 ? zoomStep : 1 / zoomStep)); event.preventDefault(); }, { passive: false });
    window.addEventListener('resize', updateCanvasSize);
    if ('ResizeObserver' in window) { new ResizeObserver(updateCanvasSize).observe(canvasHost); }
    window.addEventListener('beforeunload', () => { if (refreshTimer) { window.clearInterval(refreshTimer); } });
    applyViewTransform();
    updateCanvasSize();
    loadDiagram();
})();
