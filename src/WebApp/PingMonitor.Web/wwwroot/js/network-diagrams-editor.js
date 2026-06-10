(() => {
    const editor = document.querySelector('[data-network-diagram-editor]');
    if (!editor) {
        return;
    }

    const nav = document.querySelector('.site-nav');
    const canvasHost = editor.querySelector('[data-diagram-canvas-host]');
    const canvas = editor.querySelector('[data-diagram-canvas]');
    const world = editor.querySelector('[data-diagram-world]');
    const nodeLayer = editor.querySelector('[data-node-layer]');
    const areaLayer = editor.querySelector('[data-area-layer]');
    const linkLayer = editor.querySelector('[data-link-layer]');
    const emptyState = editor.querySelector('[data-empty-state]');
    const sizeLabel = editor.querySelector('[data-canvas-size]');
    const zoomLabel = editor.querySelector('[data-zoom-label]');
    const zoomInButton = editor.querySelector('[data-zoom-in]');
    const zoomOutButton = editor.querySelector('[data-zoom-out]');
    const resetViewButton = editor.querySelector('[data-reset-view]');
    const fitContentButton = editor.querySelector('[data-fit-content]');
    const selectAllButton = editor.querySelector('[data-select-all]');
    const clearSelectionButton = editor.querySelector('[data-clear-selection]');
    const deleteSelectionButtons = Array.from(editor.querySelectorAll('[data-delete-selection]'));
    const addButtons = Array.from(editor.querySelectorAll('[data-add-node]'));
    const addEndpointButtons = Array.from(editor.querySelectorAll('[data-add-endpoint-node]'));
    const addAreaButton = editor.querySelector('[data-add-area]');
    const endpointSearchInput = editor.querySelector('[data-endpoint-search]');
    const clearEndpointSearchButton = editor.querySelector('[data-clear-endpoint-search]');
    const endpointGroupFilter = editor.querySelector('[data-endpoint-group-filter]');
    const hideExistingEndpointsCheckbox = editor.querySelector('[data-hide-existing-endpoints]');
    const endpointResultCount = editor.querySelector('[data-endpoint-result-count]');
    const endpointFilterEmpty = editor.querySelector('[data-endpoint-filter-empty]');
    const clearEndpointFiltersButton = editor.querySelector('[data-clear-endpoint-filters]');
    const endpointToolboxItems = Array.from(editor.querySelectorAll('[data-endpoint-toolbox-item]'));
    const toolButtons = Array.from(editor.querySelectorAll('[data-tool-button]'));
    const toolHint = editor.querySelector('[data-tool-hint]');
    const nodeProperties = editor.querySelector('[data-node-properties]');
    const multiNodeProperties = editor.querySelector('[data-multi-node-properties]');
    const linkProperties = editor.querySelector('[data-link-properties]');
    const areaProperties = editor.querySelector('[data-area-properties]');
    const noSelectionPanel = editor.querySelector('[data-no-selection-panel]');
    const nodeFields = Array.from(editor.querySelectorAll('[data-node-field]'));
    const linkFields = Array.from(editor.querySelectorAll('[data-link-field]'));
    const areaFields = Array.from(editor.querySelectorAll('[data-area-field]'));
    const selectedNodeKindLabel = editor.querySelector('[data-selected-node-kind]');
    const selectedNodeEndpointDetails = editor.querySelector('[data-selected-node-endpoint-details]');
    const selectedNodeEndpointName = editor.querySelector('[data-selected-node-endpoint-name]');
    const selectedNodeEndpointTarget = editor.querySelector('[data-selected-node-endpoint-target]');
    const selectedNodeTypeLabel = editor.querySelector('[data-selected-node-type]');
    const selectedNodeHelp = editor.querySelector('[data-selected-node-help]');
    const selectedNodeCount = editor.querySelector('[data-selected-node-count]');
    const saveButton = editor.querySelector('[data-save-diagram]');
    const exportPdfButton = editor.querySelector('[data-export-pdf]');
    const exportPngButton = editor.querySelector('[data-export-png]');
    const exportSvgButton = editor.querySelector('[data-export-svg]');
    const exportPaperSelect = editor.querySelector('[data-export-paper]');
    const exportScaleSelect = editor.querySelector('[data-export-scale]');
    const canvasSizePresetSelect = editor.querySelector('[data-canvas-size-preset]');
    const canvasRatioWarning = editor.querySelector('[data-canvas-ratio-warning]');
    const drawMediaTypeSelect = editor.querySelector('[data-draw-media-type]');
    const drawLinkTypeSelect = editor.querySelector('[data-draw-link-type]');
    const mediaSubtypeField = editor.querySelector('[data-media-subtype-field]');
    const mediaSubtypeSelect = editor.querySelector('[data-link-field="mediaSubtype"]');
    const linkSpeedPresetSelect = editor.querySelector('[data-link-field="linkSpeedPreset"]');
    const customSpeedFields = editor.querySelector('[data-custom-speed-fields]');
    const lacpFields = editor.querySelector('[data-lacp-fields]');
    const lacpMemberPorts = editor.querySelector('[data-lacp-member-ports]');
    const linkVlanList = editor.querySelector('[data-link-vlan-list]');
    const addLinkVlanButton = editor.querySelector('[data-add-link-vlan]');
    const saveStatus = editor.querySelector('[data-save-status]');
    const antiforgeryToken = editor.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const loadUrl = editor.dataset.loadUrl || '';
    const saveUrl = editor.dataset.saveUrl || '';
    const exportPdfUrl = editor.dataset.exportPdfUrl || '';
    const exportPngUrl = editor.dataset.exportPngUrl || '';
    const exportSvgUrl = editor.dataset.exportSvgUrl || '';

    if (!canvasHost || !canvas || !world || !areaLayer || !nodeLayer || !linkLayer) {
        return;
    }

    const minimumZoom = 0.25;
    const maximumZoom = 3;
    const zoomStep = 1.1;
    const nodeMargin = 8;
    const aSeriesLandscapeRatio = 1.41421356237;
    const mediaTypes = ['Copper', 'Fibre', 'Wireless', 'DAC', 'VPN', 'Virtual', 'Other'];
    const linkTypes = ['Standard', 'Trunk', 'Access', 'LACP', 'PointToPoint', 'Backhaul', 'WAN', 'Management', 'Logical', 'Other'];
    const mediaSubtypeOptions = {
        Copper: ['Cat5e', 'Cat6', 'Cat6a', 'Cat7', 'Cat8', 'Coax', 'Other'],
        Fibre: ['OM1', 'OM2', 'OM3', 'OM4', 'OM5', 'OS1', 'OS2', 'Other'],
        Wireless: ['802.11a', '802.11b', '802.11g', '802.11n / Wi-Fi 4', '802.11ac / Wi-Fi 5', '802.11ax / Wi-Fi 6', '802.11be / Wi-Fi 7', '60GHz', 'Other'],
        DAC: ['Passive DAC', 'Active DAC', 'AOC', 'Other'],
        VPN: ['IPsec', 'WireGuard', 'OpenVPN', 'GRE', 'Other'],
        Virtual: ['Hyper-V vSwitch', 'VMware vSwitch', 'VLAN interface', 'Loopback', 'Other'],
        Other: ['None', 'Other']
    };
    const speedPresets = [
        { value: '', label: 'None', speedValue: '', unit: '' },
        { value: '10 Mbps', label: '10 Mbps', speedValue: '10', unit: 'Mbps' },
        { value: '100 Mbps', label: '100 Mbps', speedValue: '100', unit: 'Mbps' },
        { value: '1 Gbps', label: '1 Gbps', speedValue: '1', unit: 'Gbps' },
        { value: '2.5 Gbps', label: '2.5 Gbps', speedValue: '2.5', unit: 'Gbps' },
        { value: '5 Gbps', label: '5 Gbps', speedValue: '5', unit: 'Gbps' },
        { value: '10 Gbps', label: '10 Gbps', speedValue: '10', unit: 'Gbps' },
        { value: '25 Gbps', label: '25 Gbps', speedValue: '25', unit: 'Gbps' },
        { value: '40 Gbps', label: '40 Gbps', speedValue: '40', unit: 'Gbps' },
        { value: '100 Gbps', label: '100 Gbps', speedValue: '100', unit: 'Gbps' },
        { value: 'Other', label: 'Other', speedValue: '', unit: 'Gbps' }
    ];
    const linkSpeedUnits = ['Mbps', 'Gbps', 'Tbps'];
    const vlanModes = ['Tagged', 'Untagged', 'Native', 'Management', 'Other'];
    const parallelLinkOffsetStep = 34;
    const canvasPresets = [
        { value: 'small', label: 'Small', width: 4000, height: 2828 },
        { value: 'medium', label: 'Medium', width: 5656, height: 4000 },
        { value: 'large', label: 'Large', width: 8000, height: 5657 },
        { value: 'extra-large', label: 'Extra large', width: 11314, height: 8000 }
    ];

    const state = {
        nodes: [],
        links: [],
        areas: [],
        selectedNodeIds: [],
        selectedLinkId: null,
        selectedAreaId: null,
        activeSelectionType: 'none',
        currentTool: 'select',
        zoom: 1,
        panX: 0,
        panY: 0,
        virtualCanvasWidth: 4000,
        virtualCanvasHeight: 2828,
        name: '',
        description: '',
        dirty: false,
        loading: true,
        selectedDrawMediaType: 'Copper',
        selectedDrawLinkType: 'Standard'
    };

    let nodeSequence = 0;
    let linkSequence = 0;
    let areaSequence = 0;
    let dragState = null;
    let panState = null;
    let pendingLinkSourceId = null;
    let vlanSequence = 0;

    function updateNavHeight() {
        const height = nav ? nav.getBoundingClientRect().height : 0;
        document.documentElement.style.setProperty('--network-diagrams-nav-height', `${height}px`);
    }

    function getCanvasRect() {
        return canvas.getBoundingClientRect();
    }

    function clamp(value, min, max) {
        if (max < min) {
            return min;
        }

        return Math.min(Math.max(value, min), max);
    }

    function getNodeWidth(node) {
        return node.element.offsetWidth || 178;
    }

    function getNodeHeight(node) {
        return node.element.offsetHeight || 78;
    }

    function applyNodePosition(node) {
        node.element.style.transform = `translate(${Math.round(node.x)}px, ${Math.round(node.y)}px)`;
    }

    function clampNodePosition(node) {
        const width = getNodeWidth(node);
        const height = getNodeHeight(node);

        node.x = clamp(node.x, nodeMargin, state.virtualCanvasWidth - width - nodeMargin);
        node.y = clamp(node.y, nodeMargin, state.virtualCanvasHeight - height - nodeMargin);
        applyNodePosition(node);
        renderLinks();
    }

    function clampAllNodes() {
        state.nodes.forEach(clampNodePosition);
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
        canvasHost.style.setProperty('--diagram-grid-scale', String(state.zoom));

        if (zoomLabel) {
            zoomLabel.textContent = `${Math.round(state.zoom * 100)}%`;
        }
    }

    function updateCanvasSize() {
        updateNavHeight();

        const rect = getCanvasRect();
        canvasHost.style.setProperty('--diagram-canvas-width', `${Math.round(rect.width)}px`);
        canvasHost.style.setProperty('--diagram-canvas-height', `${Math.round(rect.height)}px`);

        const orientation = state.virtualCanvasWidth >= state.virtualCanvasHeight ? 'landscape' : 'portrait';
        const aSeries = isASeriesLandscapeCanvas() ? 'A-series' : 'legacy ratio';
        if (sizeLabel) {
            sizeLabel.textContent = `${Math.round(rect.width)} x ${Math.round(rect.height)} visible • ${state.virtualCanvasWidth} x ${state.virtualCanvasHeight} world • ${orientation} ${aSeries} • ${Math.round(state.zoom * 100)}% zoom`;
        }

        if (canvasRatioWarning) {
            canvasRatioWarning.hidden = isASeriesLandscapeCanvas();
        }

        syncCanvasPresetSelector();
        applyViewTransform();
        clampAllNodes();
        renderLinks();
    }

    function isASeriesLandscapeCanvas() {
        if (state.virtualCanvasWidth <= 0 || state.virtualCanvasHeight <= 0) {
            return false;
        }

        return Math.abs((state.virtualCanvasWidth / state.virtualCanvasHeight) - aSeriesLandscapeRatio) < 0.01;
    }

    function syncCanvasPresetSelector() {
        if (!canvasSizePresetSelect) {
            return;
        }

        const preset = canvasPresets.find(item => item.width === state.virtualCanvasWidth && item.height === state.virtualCanvasHeight);
        canvasSizePresetSelect.value = preset ? preset.value : '';
    }

    function canFitNodesWithin(width, height) {
        return state.nodes.every(node => node.x + getNodeWidth(node) + nodeMargin <= width && node.y + getNodeHeight(node) + nodeMargin <= height);
    }

    function applyCanvasPreset(value) {
        const preset = canvasPresets.find(item => item.value === value);
        if (!preset) {
            return;
        }

        if (!canFitNodesWithin(preset.width, preset.height)) {
            setSaveStatus(`Canvas cannot shrink to ${preset.label}; one or more nodes would be outside the A-series canvas. Move nodes inward or choose a larger size.`, { error: true });
            syncCanvasPresetSelector();
            return;
        }

        state.virtualCanvasWidth = preset.width;
        state.virtualCanvasHeight = preset.height;
        updateCanvasSize();
        markDirty();
        setSaveStatus(`${preset.label} A-series landscape canvas selected. Save to persist this canvas size.`, { dirty: true });
    }

    function formatNodeType(type) {
        return type.replace(/-/g, ' ');
    }

    function setEmptyState() {
        if (emptyState) {
            emptyState.hidden = state.nodes.length > 0 || state.areas.length > 0;
        }
    }

    function screenToWorld(clientX, clientY) {
        const rect = getCanvasRect();
        return {
            x: (clientX - rect.left - state.panX) / state.zoom,
            y: (clientY - rect.top - state.panY) / state.zoom
        };
    }

    function getVisibleWorldCenter() {
        const rect = getCanvasRect();
        return {
            x: (rect.width / 2 - state.panX) / state.zoom,
            y: (rect.height / 2 - state.panY) / state.zoom
        };
    }

    function makeClientId(prefix) {
        return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`;
    }

    function getNextAreaPosition()
    {
        areaSequence += 1;
        const center = getVisibleWorldCenter();
        const width = 600;
        const height = 350;
        const offset = ((areaSequence - 1) % 6) * 34;
        return { id: makeClientId('diagram-area'), x: clamp(center.x - width / 2 + offset, -1000, state.virtualCanvasWidth - 80), y: clamp(center.y - height / 2 + offset, -1000, state.virtualCanvasHeight - 60), width, height };
    }

    function getNextNodePosition() {
        nodeSequence += 1;
        const center = getVisibleWorldCenter();
        const offset = ((nodeSequence - 1) % 8) * 26;

        return {
            id: makeClientId('diagram-node'),
            x: center.x - 89 + offset,
            y: center.y - 39 + offset
        };
    }


    function getExistingEndpointIdsOnDiagram() {
        return new Set(state.nodes
            .filter(node => node.nodeKind === 'monitored-endpoint' && node.endpointId)
            .map(node => node.endpointId));
    }

    function formatEndpointCount(visibleCount, totalCount, filtersActive) {
        const endpointLabel = visibleCount === 1 ? 'endpoint' : 'endpoints';
        if (visibleCount === 0) {
            return 'No endpoints match';
        }

        if (filtersActive) {
            const totalLabel = totalCount === 1 ? 'endpoint' : 'endpoints';
            return `${visibleCount} of ${totalCount} ${totalLabel}`;
        }

        return `${visibleCount} ${endpointLabel}`;
    }

    function updateEndpointToolboxFilters() {
        if (endpointToolboxItems.length === 0) {
            return;
        }

        const searchTerm = (endpointSearchInput?.value || '').trim().toLocaleLowerCase();
        const selectedGroup = endpointGroupFilter?.value || '';
        const hideExisting = Boolean(hideExistingEndpointsCheckbox?.checked);
        const existingEndpointIds = hideExisting ? getExistingEndpointIdsOnDiagram() : new Set();
        let visibleCount = 0;

        endpointToolboxItems.forEach(item => {
            const searchText = (item.dataset.endpointSearchText || '').toLocaleLowerCase();
            const groups = (item.dataset.endpointGroups || '')
                .split('|')
                .map(group => group.trim())
                .filter(group => group.length > 0);
            const endpointId = item.querySelector('[data-add-endpoint-node]')?.dataset.endpointId || '';

            const matchesSearch = !searchTerm || searchText.includes(searchTerm);
            const matchesGroup = !selectedGroup
                || (selectedGroup === '__ungrouped' && groups.length === 0)
                || groups.some(group => group.localeCompare(selectedGroup, undefined, { sensitivity: 'accent' }) === 0);
            const matchesExisting = !hideExisting || !existingEndpointIds.has(endpointId);
            const isVisible = matchesSearch && matchesGroup && matchesExisting;

            item.hidden = !isVisible;
            if (isVisible) {
                visibleCount += 1;
            }
        });

        const filtersActive = Boolean(searchTerm || selectedGroup || hideExisting);
        if (endpointResultCount) {
            endpointResultCount.textContent = formatEndpointCount(visibleCount, endpointToolboxItems.length, filtersActive);
        }
        if (endpointFilterEmpty) {
            endpointFilterEmpty.hidden = visibleCount !== 0;
        }
        if (clearEndpointSearchButton) {
            clearEndpointSearchButton.hidden = searchTerm.length === 0;
        }
    }

    function clearEndpointFilters() {
        if (endpointSearchInput) {
            endpointSearchInput.value = '';
        }
        if (endpointGroupFilter) {
            endpointGroupFilter.value = '';
        }
        if (hideExistingEndpointsCheckbox) {
            hideExistingEndpointsCheckbox.checked = false;
        }
        updateEndpointToolboxFilters();
        endpointSearchInput?.focus();
    }

    function createNodeElement(options) {
        const node = document.createElement('div');
        node.className = 'diagram-node';
        node.tabIndex = 0;
        node.setAttribute('role', 'button');
        node.setAttribute('aria-label', `${options.label} draft ${formatNodeType(options.type)} node`);
        node.dataset.nodeId = options.id;
        node.dataset.nodeKind = options.kind;
        node.dataset.selected = 'false';

        const symbol = document.createElement('span');
        symbol.className = 'diagram-node-symbol';
        symbol.textContent = options.symbol;
        symbol.setAttribute('aria-hidden', 'true');

        const main = document.createElement('span');
        main.className = 'diagram-node-main';

        const name = document.createElement('span');
        name.className = 'diagram-node-name';
        name.dataset.nodeName = 'true';
        name.textContent = options.label;

        const type = document.createElement('span');
        type.className = 'diagram-node-type';
        type.textContent = options.kind === 'monitored-endpoint' ? 'monitored endpoint' : formatNodeType(options.type);

        main.append(name, type);

        if (options.target) {
            const target = document.createElement('span');
            target.className = 'diagram-node-target';
            target.textContent = options.target;
            main.appendChild(target);
        }

        const draft = document.createElement('span');
        draft.className = 'diagram-node-draft';
        draft.textContent = options.kind === 'monitored-endpoint' ? 'Visual instance' : 'Diagram node';
        main.appendChild(draft);

        node.append(symbol, main);
        return node;
    }

    function updateNodeElement(node) {
        const name = node.element.querySelector('[data-node-name]');
        if (name) {
            name.textContent = node.label;
        }

        node.element.setAttribute('aria-label', `${node.label} draft ${formatNodeType(node.nodeType)} node`);
    }

    function addNodeFromOptions(options) {
        const position = getNextNodePosition();
        const node = {
            id: position.id,
            nodeType: options.type,
            nodeKind: options.kind,
            endpointId: options.endpointId || '',
            endpointName: options.endpointName || options.label,
            label: options.label,
            target: options.target || '',
            iconKey: options.iconKey || options.symbol || '',
            notes: '',
            x: position.x,
            y: position.y,
            element: createNodeElement({
                id: position.id,
                label: options.label,
                target: options.target || '',
                type: options.type,
                kind: options.kind,
                symbol: options.symbol
            })
        };

        nodeLayer.appendChild(node.element);
        state.nodes.push(node);
        markDirty();

        clampNodePosition(node);
        setEmptyState();
        selectOnlyNode(node.id);
        node.element.focus({ preventScroll: true });
        updateEndpointToolboxFilters();
    }

    function addCustomNode(button) {
        addNodeFromOptions({
            type: button.dataset.nodeType || 'generic-device',
            kind: 'custom-device',
            label: button.dataset.nodeLabel || 'Device',
            symbol: button.dataset.nodeSymbol || 'DEV'
        });
    }

    function addEndpointNode(button) {
        addNodeFromOptions({
            type: 'monitored-endpoint',
            kind: 'monitored-endpoint',
            endpointId: button.dataset.endpointId || '',
            endpointName: button.dataset.endpointName || 'Monitored endpoint',
            label: button.dataset.endpointName || 'Monitored endpoint',
            target: button.dataset.endpointTarget || '',
            iconKey: button.dataset.endpointIcon || 'generic',
            symbol: button.dataset.endpointIcon || 'END'
        });
    }


    function normalizeAreaStyleKey(value) {
        const requested = String(value || 'neutral').trim().toLowerCase();
        return ['neutral', 'blue', 'green', 'amber', 'red', 'purple'].includes(requested) ? requested : 'neutral';
    }

    function applyAreaPosition(area) {
        area.element.style.transform = `translate(${Math.round(area.x)}px, ${Math.round(area.y)}px)`;
        area.element.style.width = `${Math.round(area.width)}px`;
        area.element.style.height = `${Math.round(area.height)}px`;
    }

    function updateAreaElement(area) {
        area.element.dataset.styleKey = normalizeAreaStyleKey(area.styleKey);
        const label = area.element.querySelector('[data-area-label]');
        if (label) { label.textContent = area.label || 'Area'; }
        const notes = area.element.querySelector('[data-area-notes]');
        if (notes) {
            notes.textContent = area.notes || '';
            notes.hidden = !area.notes;
        }
        area.element.setAttribute('aria-label', `${area.label || 'Area'} visual area box`);
        applyAreaPosition(area);
    }

    function createAreaElement(area) {
        const element = document.createElement('div');
        element.className = 'diagram-area';
        element.tabIndex = 0;
        element.setAttribute('role', 'button');
        element.dataset.areaId = area.id;
        element.dataset.selected = 'false';
        const header = document.createElement('div');
        header.className = 'diagram-area-header';
        header.dataset.areaDragHandle = 'true';
        header.innerHTML = '<span data-area-label></span>';
        const notes = document.createElement('div');
        notes.className = 'diagram-area-notes';
        notes.dataset.areaNotes = 'true';
        const resize = document.createElement('span');
        resize.className = 'diagram-area-resize-handle';
        resize.dataset.areaResizeHandle = 'true';
        resize.setAttribute('aria-hidden', 'true');
        element.append(header, notes, resize);
        return element;
    }

    function addArea() {
        const position = getNextAreaPosition();
        const area = { id: position.id, label: 'New area', notes: '', x: position.x, y: position.y, width: position.width, height: position.height, styleKey: 'neutral', sortOrder: state.areas.length, element: null };
        area.element = createAreaElement(area);
        areaLayer.appendChild(area.element);
        state.areas.push(area);
        updateAreaElement(area);
        selectArea(area.id);
        markDirty();
    }

    function findNodeByElement(element) {
        return state.nodes.find(node => node.element === element);
    }

    function findNodeById(nodeId) {
        return state.nodes.find(node => node.id === nodeId) || null;
    }

    function findLinkById(linkId) {
        return state.links.find(link => link.id === linkId) || null;
    }

    function findAreaById(areaId) {
        return state.areas.find(area => area.id === areaId) || null;
    }

    function selectedArea() {
        return state.selectedAreaId ? findAreaById(state.selectedAreaId) : null;
    }

    function selectedNodes() {
        return state.selectedNodeIds.map(findNodeById).filter(Boolean);
    }

    function hasSelection() {
        return state.selectedNodeIds.length > 0 || Boolean(state.selectedLinkId) || Boolean(state.selectedAreaId);
    }

    function syncSelectionDom() {
        state.nodes.forEach(node => {
            node.element.dataset.selected = state.selectedNodeIds.includes(node.id) ? 'true' : 'false';
        });
        state.areas.forEach(area => {
            area.element.dataset.selected = state.selectedAreaId === area.id ? 'true' : 'false';
        });
        renderLinks();
        updatePropertiesPanel();
        updateSelectionButtons();
    }

    function setTool(tool) {
        state.currentTool = tool;
        if (tool !== 'draw-link') {
            setPendingLinkSource(null);
        }

        toolButtons.forEach(button => {
            button.setAttribute('aria-pressed', button.dataset.toolButton === tool ? 'true' : 'false');
        });

        if (toolHint) {
            toolHint.textContent = tool === 'draw-link'
                ? 'Draw link mode: select a source node, then select a different target node. Drag empty space to pan. Use mouse wheel to zoom.'
                : 'Select nodes or links. Shift/Ctrl-click nodes to multi-select; drag selected nodes to move the group. Drag empty space to pan.';
        }
    }

    function selectOnlyNode(nodeId) {
        state.selectedNodeIds = [nodeId];
        state.selectedLinkId = null;
        state.selectedAreaId = null;
        state.activeSelectionType = 'nodes';
        syncSelectionDom();
    }

    function toggleNodeSelection(nodeId) {
        state.selectedLinkId = null;
        state.selectedAreaId = null;
        if (state.selectedNodeIds.includes(nodeId)) {
            state.selectedNodeIds = state.selectedNodeIds.filter(id => id !== nodeId);
        } else {
            state.selectedNodeIds = [...state.selectedNodeIds, nodeId];
        }

        state.activeSelectionType = state.selectedNodeIds.length > 0 ? 'nodes' : 'none';
        syncSelectionDom();
    }

    function selectAllNodes() {
        state.selectedNodeIds = state.nodes.map(node => node.id);
        state.selectedLinkId = null;
        state.selectedAreaId = null;
        state.activeSelectionType = state.selectedNodeIds.length > 0 ? 'nodes' : 'none';
        syncSelectionDom();
    }

    function clearSelection() {
        state.selectedNodeIds = [];
        state.selectedLinkId = null;
        state.selectedAreaId = null;
        state.activeSelectionType = 'none';
        syncSelectionDom();
    }

    function selectLink(linkId) {
        state.selectedLinkId = linkId;
        state.selectedNodeIds = [];
        state.selectedAreaId = null;
        state.activeSelectionType = 'link';
        syncSelectionDom();
    }

    function selectArea(areaId) {
        state.selectedAreaId = areaId;
        state.selectedLinkId = null;
        state.selectedNodeIds = [];
        state.activeSelectionType = 'area';
        syncSelectionDom();
    }

    function updateSelectionButtons() {
        if (selectAllButton) {
            selectAllButton.disabled = state.nodes.length === 0;
        }

        if (clearSelectionButton) {
            clearSelectionButton.disabled = !hasSelection();
        }

        deleteSelectionButtons.forEach(button => {
            button.disabled = !hasSelection();
        });
    }

    function setPendingLinkSource(nodeId) {
        pendingLinkSourceId = nodeId;
        state.nodes.forEach(node => {
            node.element.dataset.linkPending = node.id === nodeId ? 'true' : 'false';
        });
    }

    function canLinkToNode(node) {
        return node && node.nodeType !== 'note';
    }

    function normalizeMediaType(value, legacyLinkType) {
        const requested = (value || '').trim();
        if (requested) {
            return mediaTypes.find(type => type.toLowerCase() === requested.toLowerCase()) || 'Other';
        }

        const legacy = (legacyLinkType || '').trim().toLowerCase();
        if (!legacy || legacy === 'default') {
            return 'Copper';
        }
        if (legacy === 'fibre') { return 'Fibre'; }
        if (legacy === 'wireless') { return 'Wireless'; }
        if (legacy === 'vpn') { return 'VPN'; }
        if (legacy === 'logical') { return 'Virtual'; }
        if (legacy === 'lacp') { return 'Other'; }
        return mediaTypes.find(type => type.toLowerCase() === legacy) || 'Copper';
    }

    function normalizeLinkType(value) {
        const requested = (value || '').trim();
        if (!requested || requested.toLowerCase() === 'default') {
            return 'Standard';
        }

        const normalized = linkTypes.find(type => type.toLowerCase() === requested.toLowerCase());
        if (normalized) {
            return normalized;
        }

        const legacyMedia = mediaTypes.find(type => type.toLowerCase() === requested.toLowerCase());
        return legacyMedia ? 'Standard' : 'Other';
    }

    function getMediaSubtypeOptions(mediaType) {
        return mediaSubtypeOptions[normalizeMediaType(mediaType)] || mediaSubtypeOptions.Other;
    }

    function normalizeMediaSubtype(value, mediaType) {
        const requested = (value || '').trim();
        if (!requested) {
            return '';
        }

        const options = getMediaSubtypeOptions(mediaType);
        const normalized = options.find(type => type.toLowerCase() === requested.toLowerCase()) || '';
        return normalized === 'None' ? '' : normalized;
    }

    function normalizeSpeedUnit(value) {
        const requested = (value || '').trim();
        return linkSpeedUnits.find(unit => unit.toLowerCase() === requested.toLowerCase()) || '';
    }

    function normalizeLacpMemberCount(value, linkType) {
        if (normalizeLinkType(linkType) !== 'LACP') {
            return '';
        }

        const parsed = Number.parseInt(value, 10);
        return String(clamp(Number.isFinite(parsed) ? parsed : 2, 1, 16));
    }

    function createLink(sourceNodeId, targetNodeId) {
        if (sourceNodeId === targetNodeId) {
            return;
        }

        linkSequence += 1;
        const link = {
            id: makeClientId('diagram-link'),
            sourceNodeId,
            targetNodeId,
            label: '',
            sourcePort: '',
            targetPort: '',
            notes: '',
            mediaType: normalizeMediaType(state.selectedDrawMediaType),
            mediaSubtype: '',
            linkType: normalizeLinkType(state.selectedDrawLinkType),
            linkSpeedValue: '',
            linkSpeedUnit: '',
            linkSpeedPreset: '',
            lacpMemberCount: normalizeLinkType(state.selectedDrawLinkType) === 'LACP' ? '2' : '',
            lacpMemberPorts: []
        };

        state.links.push(link);
        markDirty();
        selectLink(link.id);
    }

    function getGroupBounds(nodes) {
        return nodes.reduce((bounds, node) => ({
            minX: Math.min(bounds.minX, node.x),
            minY: Math.min(bounds.minY, node.y),
            maxX: Math.max(bounds.maxX, node.x + getNodeWidth(node)),
            maxY: Math.max(bounds.maxY, node.y + getNodeHeight(node))
        }), { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity });
    }

    function clampGroupDelta(nodes, requestedDeltaX, requestedDeltaY, startPositions) {
        if (nodes.length === 0) {
            return { deltaX: 0, deltaY: 0 };
        }

        const bounds = startPositions
            ? nodes.reduce((currentBounds, node) => {
                const start = startPositions.get(node.id) || { x: node.x, y: node.y };
                return {
                    minX: Math.min(currentBounds.minX, start.x),
                    minY: Math.min(currentBounds.minY, start.y),
                    maxX: Math.max(currentBounds.maxX, start.x + getNodeWidth(node)),
                    maxY: Math.max(currentBounds.maxY, start.y + getNodeHeight(node))
                };
            }, { minX: Infinity, minY: Infinity, maxX: -Infinity, maxY: -Infinity })
            : getGroupBounds(nodes);

        return {
            deltaX: clamp(requestedDeltaX, nodeMargin - bounds.minX, state.virtualCanvasWidth - nodeMargin - bounds.maxX),
            deltaY: clamp(requestedDeltaY, nodeMargin - bounds.minY, state.virtualCanvasHeight - nodeMargin - bounds.maxY)
        };
    }


    function handleAreaPointerDown(event) {
        const target = event.target instanceof Element ? event.target : null;
        const areaElement = target?.closest('.diagram-area');
        if (!areaElement || event.button !== 0 || state.currentTool !== 'select') {
            return;
        }

        const area = findAreaById(areaElement.dataset.areaId);
        if (!area) {
            return;
        }

        selectArea(area.id);
        const pointer = screenToWorld(event.clientX, event.clientY);
        const isResize = Boolean(target?.closest('[data-area-resize-handle]'));
        dragState = {
            pointerId: event.pointerId,
            area,
            mode: isResize ? 'resize-area' : 'move-area',
            startPointer: pointer,
            startArea: { x: area.x, y: area.y, width: area.width, height: area.height },
            moved: false
        };
        area.element.dataset.dragging = 'true';
        area.element.setPointerCapture(event.pointerId);
        area.element.focus({ preventScroll: true });
        event.preventDefault();
        event.stopPropagation();
    }

    function handleNodePointerDown(event) {
        const target = event.target.closest('.diagram-node');
        if (!target || !nodeLayer.contains(target)) {
            return;
        }

        const node = findNodeByElement(target);
        if (!node) {
            return;
        }

        if (state.currentTool === 'draw-link') {
            event.preventDefault();
            if (!canLinkToNode(node)) {
                return;
            }

            if (!pendingLinkSourceId) {
                setPendingLinkSource(node.id);
                selectOnlyNode(node.id);
                return;
            }

            createLink(pendingLinkSourceId, node.id);
            setPendingLinkSource(null);
            return;
        }

        const additiveSelection = event.shiftKey || event.ctrlKey || event.metaKey;
        if (additiveSelection) {
            toggleNodeSelection(node.id);
        } else if (!state.selectedNodeIds.includes(node.id)) {
            selectOnlyNode(node.id);
        }

        const nodesForDrag = state.selectedNodeIds.includes(node.id) ? selectedNodes() : [node];
        dragState = {
            pointerId: event.pointerId,
            startPointer: screenToWorld(event.clientX, event.clientY),
            nodes: nodesForDrag,
            startPositions: new Map(nodesForDrag.map(selectedNode => [selectedNode.id, { x: selectedNode.x, y: selectedNode.y }])),
            moved: false
        };

        nodesForDrag.forEach(selectedNode => {
            selectedNode.element.dataset.dragging = 'true';
        });
        target.focus({ preventScroll: true });
        target.setPointerCapture(event.pointerId);
        event.preventDefault();
        event.stopPropagation();
    }

    function moveDrag(event) {
        if (!dragState || dragState.pointerId !== event.pointerId) {
            return;
        }

        const pointer = screenToWorld(event.clientX, event.clientY);
        const requestedDeltaX = pointer.x - dragState.startPointer.x;
        const requestedDeltaY = pointer.y - dragState.startPointer.y;

        if (dragState.area) {
            const area = dragState.area;
            if (dragState.mode === 'resize-area') {
                area.width = clamp(dragState.startArea.width + requestedDeltaX, 80, 20000);
                area.height = clamp(dragState.startArea.height + requestedDeltaY, 60, 20000);
            } else {
                area.x = clamp(dragState.startArea.x + requestedDeltaX, -1000, state.virtualCanvasWidth - 80);
                area.y = clamp(dragState.startArea.y + requestedDeltaY, -1000, state.virtualCanvasHeight - 60);
            }

            dragState.moved = dragState.moved || Math.abs(requestedDeltaX) > 1 || Math.abs(requestedDeltaY) > 1;
            updateAreaElement(area);
            markDirty();
            updatePropertiesPanel();
            event.preventDefault();
            return;
        }

        const { deltaX, deltaY } = clampGroupDelta(dragState.nodes, requestedDeltaX, requestedDeltaY, dragState.startPositions);
        dragState.moved = dragState.moved || Math.abs(deltaX) > 1 || Math.abs(deltaY) > 1;

        dragState.nodes.forEach(node => {
            const start = dragState.startPositions.get(node.id);
            if (!start) {
                return;
            }

            node.x = start.x + deltaX;
            node.y = start.y + deltaY;
            applyNodePosition(node);
        });

        renderLinks();
        markDirty();
        event.preventDefault();
    }

    function endDrag(event) {
        if (!dragState || dragState.pointerId !== event.pointerId) {
            return;
        }

        if (dragState.area) {
            const area = dragState.area;
            area.element.dataset.dragging = 'false';
            if (area.element.hasPointerCapture(event.pointerId)) {
                area.element.releasePointerCapture(event.pointerId);
            }
            const moved = dragState.moved;
            dragState = null;
            if (moved) { markDirty(); }
            updatePropertiesPanel();
            return;
        }

        dragState.nodes.forEach(node => {
            node.element.dataset.dragging = 'false';
            if (node.element.hasPointerCapture(event.pointerId)) {
                node.element.releasePointerCapture(event.pointerId);
            }
        });
        const moved = dragState.moved;
        dragState = null;
        if (moved) { markDirty(); }
        updatePropertiesPanel();
    }

    function nudgeSelectedNodes(event) {
        const node = findNodeByElement(event.currentTarget);
        if (!node || state.currentTool !== 'select') {
            return;
        }

        const step = event.shiftKey ? 24 : 8;
        let deltaX = 0;
        let deltaY = 0;

        if (event.key === 'ArrowLeft') {
            deltaX = -step;
        } else if (event.key === 'ArrowRight') {
            deltaX = step;
        } else if (event.key === 'ArrowUp') {
            deltaY = -step;
        } else if (event.key === 'ArrowDown') {
            deltaY = step;
        } else {
            return;
        }

        if (!state.selectedNodeIds.includes(node.id)) {
            selectOnlyNode(node.id);
        }

        const nodes = selectedNodes();
        const clamped = clampGroupDelta(nodes, deltaX, deltaY);
        nodes.forEach(selectedNode => {
            selectedNode.x += clamped.deltaX;
            selectedNode.y += clamped.deltaY;
            applyNodePosition(selectedNode);
        });
        renderLinks();
        event.preventDefault();
    }

    function getNodeCenter(node) {
        return {
            x: node.x + getNodeWidth(node) / 2,
            y: node.y + getNodeHeight(node) / 2
        };
    }

    function createSvgElement(name) {
        return document.createElementNS('http://www.w3.org/2000/svg', name);
    }

    function getUnorderedLinkPairKey(link) {
        return [link.sourceNodeId, link.targetNodeId].sort().join('::');
    }

    function getParallelOffsetIndexes() {
        const groups = new Map();
        state.links.forEach(link => {
            const key = getUnorderedLinkPairKey(link);
            const group = groups.get(key) || [];
            group.push(link);
            groups.set(key, group);
        });

        const offsets = new Map();
        groups.forEach(group => {
            const ordered = [...group].sort((left, right) => String(left.id).localeCompare(String(right.id)));
            const center = (ordered.length - 1) / 2;
            ordered.forEach((link, index) => offsets.set(link.id, index - center));
        });

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
        const control = {
            x: (start.x + end.x) / 2 + perpendicularX * offset,
            y: (start.y + end.y) / 2 + perpendicularY * offset
        };
        const midpoint = {
            x: start.x * 0.25 + control.x * 0.5 + end.x * 0.25,
            y: start.y * 0.25 + control.y * 0.5 + end.y * 0.25
        };
        const labelNormal = offset === 0 ? -14 : Math.sign(offset) * 14;
        const label = {
            x: midpoint.x + perpendicularX * labelNormal,
            y: midpoint.y + perpendicularY * labelNormal - 4
        };

        return {
            start,
            end,
            control,
            midpoint,
            label,
            path: `M ${start.x} ${start.y} Q ${control.x} ${control.y} ${end.x} ${end.y}`
        };
    }

    function truncateLinkText(value, maxLength) {
        const normalized = (value || '').replace(/\s+/g, ' ').trim();
        return normalized.length <= maxLength ? normalized : `${normalized.slice(0, Math.max(0, maxLength - 1))}…`;
    }


    function createVlanClientId() {
        vlanSequence += 1;
        return `vlan-${Date.now().toString(36)}-${vlanSequence.toString(36)}`;
    }

    function normalizeVlanMode(value) {
        const requested = (value || '').trim();
        return vlanModes.find(mode => mode.toLowerCase() === requested.toLowerCase()) || 'Tagged';
    }

    function normalizeVlans(vlans) {
        if (!Array.isArray(vlans)) {
            return [];
        }

        return vlans.map((vlan, index) => ({
            clientId: String(vlan?.clientId || vlan?.linkVlanId || createVlanClientId()),
            vlanId: vlan?.vlanId == null || vlan.vlanId === '' ? '' : String(vlan.vlanId).slice(0, 4),
            name: String(vlan?.name || '').slice(0, 128),
            mode: normalizeVlanMode(vlan?.mode),
            notes: String(vlan?.notes || '').slice(0, 512),
            sortOrder: Number.isFinite(Number(vlan?.sortOrder)) ? Number(vlan.sortOrder) : index
        })).sort((left, right) => left.sortOrder - right.sortOrder);
    }

    function isBlankVlan(vlan) {
        return !vlan.vlanId && !vlan.name && !vlan.notes;
    }

    function validateVlanForSave(vlan) {
        if (isBlankVlan(vlan)) {
            return null;
        }

        const vlanId = Number(vlan.vlanId);
        if (!Number.isInteger(vlanId) || vlanId < 1 || vlanId > 4094) {
            throw new Error('VLAN ID must be between 1 and 4094.');
        }

        if (!vlanModes.includes(normalizeVlanMode(vlan.mode))) {
            throw new Error('Select a VLAN mode.');
        }

        return vlanId;
    }

    function buildVlanSummary(link, maxLength = 72) {
        const vlans = normalizeVlans(link?.vlans);
        if (vlans.length === 0) {
            return '';
        }

        const labels = { Tagged: 'T', Untagged: 'U', Native: 'Native', Management: 'Mgmt', Other: 'Other' };
        const parts = vlanModes.map(mode => {
            const values = vlans
                .filter(vlan => vlan.mode === mode && vlan.vlanId)
                .map(vlan => vlan.name ? `${vlan.vlanId} ${vlan.name}` : vlan.vlanId);
            return values.length > 0 ? `${labels[mode]}:${values.join(',')}` : '';
        }).filter(Boolean);

        return truncateLinkText(parts.join(' · '), maxLength);
    }

    function formatSpeed(value, unit) {
        const speed = (value || '').toString().trim();
        const normalizedUnit = normalizeSpeedUnit(unit);
        return speed && normalizedUnit ? `${speed} ${normalizedUnit}` : '';
    }


    function getSpeedPreset(value, unit) {
        const formatted = formatSpeed(value, unit);
        if (!formatted) {
            return '';
        }

        return speedPresets.some(preset => preset.value === formatted) ? formatted : 'Other';
    }

    function buildLinkSummary(link) {
        const media = normalizeMediaType(link.mediaType, link.linkType);
        const type = normalizeLinkType(link.linkType);
        const speed = formatSpeed(link.linkSpeedValue, link.linkSpeedUnit);
        const mediaSubtype = normalizeMediaSubtype(link.mediaSubtype, media);
        const mediaLabel = mediaSubtype && media === 'Copper' ? mediaSubtype : [media.toLowerCase(), mediaSubtype].filter(Boolean).join(' ');
        if (type === 'LACP') {
            const count = normalizeLacpMemberCount(link.lacpMemberCount, type) || '2';
            return ['LACP', `${count} ×`, speed, mediaLabel].filter(Boolean).join(' ');
        }

        const typeLabel = type === 'Standard' ? '' : type;
        return [typeLabel, speed, mediaLabel].filter(Boolean).join(' ');
    }

    function buildVisibleLinkLabel(link) {
        const summary = buildLinkSummary(link);
        const main = [summary, link.label, link.notes].filter(value => value && value.trim()).join(' — ');
        const ports = normalizeLinkType(link.linkType) !== 'LACP' && (link.sourcePort || link.targetPort)
            ? [link.sourcePort || '?', link.targetPort || '?'].join(' ↔ ')
            : '';
        return truncateLinkText([main, ports, buildVlanSummary(link)].filter(Boolean).join(' • '), 116);
    }

    function renderLinks() {
        linkLayer.replaceChildren();
        const offsetIndexes = getParallelOffsetIndexes();

        state.links.forEach(link => {
            const source = findNodeById(link.sourceNodeId);
            const target = findNodeById(link.targetNodeId);
            if (!source || !target) {
                return;
            }

            const geometry = buildLinkGeometry(source, target, offsetIndexes.get(link.id) || 0);
            const group = createSvgElement('g');
            group.classList.add('diagram-link-group');
            group.dataset.linkId = link.id;
            group.dataset.selected = state.selectedLinkId === link.id ? 'true' : 'false';
            group.dataset.mediaType = normalizeMediaType(link.mediaType, link.linkType).toLowerCase();
            group.dataset.linkType = normalizeLinkType(link.linkType).toLowerCase();

            const selectRenderedLink = event => {
                if (state.currentTool !== 'select') {
                    return;
                }

                selectLink(link.id);
                canvas.focus({ preventScroll: true });
                event.preventDefault();
                event.stopPropagation();
            };

            const line = createSvgElement('path');
            line.classList.add('diagram-link-line');
            line.dataset.linkId = link.id;
            line.setAttribute('d', geometry.path);
            line.addEventListener('pointerdown', selectRenderedLink);

            const hit = createSvgElement('path');
            hit.classList.add('diagram-link-hit');
            hit.dataset.linkId = link.id;
            hit.setAttribute('d', geometry.path);
            hit.addEventListener('pointerdown', selectRenderedLink);

            group.append(line, hit);

            const visibleLabel = buildVisibleLinkLabel(link);
            if (visibleLabel) {
                const label = createSvgElement('text');
                label.classList.add('diagram-link-label');
                label.setAttribute('x', String(geometry.label.x));
                label.setAttribute('y', String(geometry.label.y));
                label.setAttribute('text-anchor', 'middle');
                label.textContent = visibleLabel;
                label.addEventListener('pointerdown', selectRenderedLink);
                group.appendChild(label);
            }

            linkLayer.appendChild(group);
        });
    }

    function updatePropertiesPanel() {
        const selectedLink = state.selectedLinkId ? findLinkById(state.selectedLinkId) : null;
        const area = selectedArea();
        const nodes = selectedNodes();
        const singleNode = nodes.length === 1 ? nodes[0] : null;
        const multipleNodes = nodes.length > 1;

        if (noSelectionPanel) {
            noSelectionPanel.hidden = Boolean(selectedLink) || Boolean(area) || Boolean(singleNode) || multipleNodes;
        }

        if (areaProperties) {
            areaProperties.hidden = !area;
        }

        if (nodeProperties) {
            nodeProperties.hidden = !singleNode;
        }

        if (multiNodeProperties) {
            multiNodeProperties.hidden = !multipleNodes;
        }

        if (linkProperties) {
            linkProperties.hidden = !selectedLink;
        }

        if (singleNode) {
            if (selectedNodeKindLabel) {
                selectedNodeKindLabel.textContent = singleNode.nodeKind === 'monitored-endpoint'
                    ? 'Selected monitored endpoint visual node'
                    : 'Selected custom draft node';
            }

            if (selectedNodeTypeLabel) {
                selectedNodeTypeLabel.textContent = singleNode.nodeKind === 'monitored-endpoint'
                    ? 'Monitored endpoint'
                    : formatNodeType(singleNode.nodeType);
            }

            if (selectedNodeHelp) {
                selectedNodeHelp.textContent = singleNode.nodeKind === 'monitored-endpoint'
                    ? 'Editing this diagram label does not rename the monitored endpoint. Changes remain client-side draft layout only.'
                    : 'Editing this node changes only the current client-side draft diagram.';
            }

            if (selectedNodeEndpointDetails) {
                selectedNodeEndpointDetails.hidden = singleNode.nodeKind !== 'monitored-endpoint';
            }

            if (selectedNodeEndpointName) {
                selectedNodeEndpointName.textContent = singleNode.endpointName || singleNode.label;
            }

            if (selectedNodeEndpointTarget) {
                selectedNodeEndpointTarget.textContent = singleNode.target || 'No target recorded';
            }
        }

        if (selectedNodeCount) {
            selectedNodeCount.textContent = String(nodes.length);
        }

        areaFields.forEach(field => {
            const propertyName = field.dataset.areaField;
            if (!area || !propertyName) {
                field.value = '';
            } else {
                field.value = area[propertyName] ?? '';
            }
        });

        nodeFields.forEach(field => {
            const propertyName = field.dataset.nodeField;
            field.value = singleNode && propertyName ? singleNode[propertyName] || '' : '';
        });

        linkFields.forEach(field => {
            const propertyName = field.dataset.linkField;
            if (!selectedLink || !propertyName) {
                field.value = '';
            } else if (propertyName === 'linkSpeedPreset') {
                field.value = getSpeedPreset(selectedLink.linkSpeedValue, selectedLink.linkSpeedUnit);
            } else {
                field.value = selectedLink[propertyName] || '';
            }
        });

        const selectedMediaType = selectedLink ? normalizeMediaType(selectedLink.mediaType, selectedLink.linkType) : '';
        const selectedLinkType = selectedLink ? normalizeLinkType(selectedLink.linkType) : '';
        if (mediaSubtypeSelect) {
            const currentValue = selectedLink ? normalizeMediaSubtype(selectedLink.mediaSubtype, selectedMediaType) : '';
            mediaSubtypeSelect.replaceChildren();
            const none = document.createElement('option');
            none.value = '';
            none.textContent = 'None';
            mediaSubtypeSelect.appendChild(none);
            getMediaSubtypeOptions(selectedMediaType || 'Other').forEach(optionValue => {
                const option = document.createElement('option');
                option.value = optionValue === 'None' ? '' : optionValue;
                option.textContent = optionValue;
                mediaSubtypeSelect.appendChild(option);
            });
            mediaSubtypeSelect.value = currentValue;
        }
        if (mediaSubtypeField) {
            mediaSubtypeField.hidden = !selectedLink;
        }
        if (customSpeedFields) {
            customSpeedFields.hidden = !selectedLink || getSpeedPreset(selectedLink.linkSpeedValue, selectedLink.linkSpeedUnit) !== 'Other';
        }
        if (lacpFields) {
            lacpFields.hidden = selectedLinkType !== 'LACP';
        }
        renderLacpMemberPortFields(selectedLink);
        renderVlanFields(selectedLink);
    }

    function addLinkVlan(event) {
        event?.preventDefault();
        const selectedLink = state.selectedLinkId ? findLinkById(state.selectedLinkId) : null;
        if (!selectedLink) {
            return;
        }

        selectedLink.vlans = normalizeVlans(selectedLink.vlans);
        const nextSortOrder = selectedLink.vlans.length === 0
            ? 0
            : Math.max(...selectedLink.vlans.map(vlan => vlan.sortOrder || 0)) + 1;
        const newVlan = {
            clientId: createVlanClientId(),
            vlanId: '',
            name: '',
            mode: 'Tagged',
            notes: '',
            sortOrder: nextSortOrder
        };
        selectedLink.vlans.push(newVlan);
        renderVlanFields(selectedLink);
        renderLinks();
        markDirty();
        linkVlanList?.querySelector(`[data-vlan-client-id="${CSS.escape(newVlan.clientId)}"] [data-vlan-field="vlanId"]`)?.focus();
    }

    function renderVlanFields(link) {
        if (!linkVlanList) {
            return;
        }

        linkVlanList.replaceChildren();
        if (!link) {
            return;
        }

        link.vlans = normalizeVlans(link.vlans);
        if (link.vlans.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'toolbox-help';
            empty.textContent = 'No VLAN metadata has been added to this visual link.';
            linkVlanList.appendChild(empty);
            return;
        }

        link.vlans.forEach((vlan, index) => {
            const card = document.createElement('div');
            card.className = 'link-vlan-card';
            card.dataset.vlanClientId = vlan.clientId;
            card.innerHTML = `<div class="link-vlan-card-header"><strong>VLAN ${index + 1}</strong><button type="button" aria-label="Remove VLAN ${index + 1}">Remove</button></div><label>ID<input type="number" data-vlan-field="vlanId" min="1" max="4094" step="1" value="${escapeHtml(vlan.vlanId)}" placeholder="10"></label><label>Name<input type="text" data-vlan-field="name" maxlength="128" value="${escapeHtml(vlan.name)}" placeholder="LAN"></label><label>Mode<select data-vlan-field="mode">${vlanModes.map(mode => `<option value="${mode}"${mode === vlan.mode ? ' selected' : ''}>${mode}</option>`).join('')}</select></label><label>Notes<textarea data-vlan-field="notes" rows="2" maxlength="512">${escapeHtml(vlan.notes)}</textarea></label>`;
            const remove = card.querySelector('button');
            const inputs = card.querySelectorAll('input, select, textarea');
            remove.addEventListener('click', event => {
                event.preventDefault();
                const currentIndex = link.vlans.findIndex(entry => entry.clientId === vlan.clientId);
                if (currentIndex >= 0) {
                    link.vlans.splice(currentIndex, 1);
                }
                renderVlanFields(link);
                renderLinks();
                markDirty();
            });
            inputs[0].addEventListener('input', event => { vlan.vlanId = event.currentTarget.value; renderLinks(); markDirty(); });
            inputs[1].addEventListener('input', event => { vlan.name = event.currentTarget.value; renderLinks(); markDirty(); });
            inputs[2].addEventListener('change', event => { vlan.mode = normalizeVlanMode(event.currentTarget.value); renderLinks(); markDirty(); });
            inputs[3].addEventListener('input', event => { vlan.notes = event.currentTarget.value; markDirty(); });
            linkVlanList.appendChild(card);
        });
    }


    function updateSelectedAreaField(event) {
        const area = selectedArea();
        const field = event.currentTarget;
        const propertyName = field.dataset.areaField;
        if (!area || !propertyName) {
            return;
        }

        if (['x', 'y', 'width', 'height'].includes(propertyName)) {
            const min = propertyName === 'width' ? 80 : propertyName === 'height' ? 60 : -1000;
            const max = propertyName === 'x' || propertyName === 'y' ? 21000 : 20000;
            area[propertyName] = clamp(Number(field.value) || 0, min, max);
        } else if (propertyName === 'styleKey') {
            area.styleKey = normalizeAreaStyleKey(field.value);
        } else {
            area[propertyName] = field.value;
        }

        updateAreaElement(area);
        markDirty();
        updatePropertiesPanel();
    }

    function updateSelectedNodeField(event) {
        const nodes = selectedNodes();
        if (nodes.length !== 1) {
            return;
        }

        const node = nodes[0];
        const field = event.currentTarget;
        const propertyName = field.dataset.nodeField;
        if (!propertyName) {
            return;
        }

        node[propertyName] = field.value;
        updateNodeElement(node);
        markDirty();
    }


    function parseLacpMemberPorts(json) {
        if (!json) {
            return [];
        }
        try {
            const parsed = JSON.parse(json);
            return Array.isArray(parsed) ? parsed.slice(0, 16).map(member => ({
                sourcePort: String(member?.sourcePort || '').slice(0, 128),
                targetPort: String(member?.targetPort || '').slice(0, 128)
            })) : [];
        } catch {
            return [];
        }
    }

    function syncLacpMemberPortCount(link) {
        if (!link || normalizeLinkType(link.linkType) !== 'LACP') {
            return;
        }

        const count = Number.parseInt(normalizeLacpMemberCount(link.lacpMemberCount, link.linkType), 10);
        link.lacpMemberPorts = Array.isArray(link.lacpMemberPorts) ? link.lacpMemberPorts.slice(0, count) : [];
        while (link.lacpMemberPorts.length < count) {
            link.lacpMemberPorts.push({ sourcePort: '', targetPort: '' });
        }
    }

    function renderLacpMemberPortFields(link) {
        if (!lacpMemberPorts) {
            return;
        }

        lacpMemberPorts.replaceChildren();
        if (!link || normalizeLinkType(link.linkType) !== 'LACP') {
            return;
        }

        syncLacpMemberPortCount(link);
        link.lacpMemberPorts.forEach((member, index) => {
            const row = document.createElement('div');
            row.className = 'lacp-member-port-row';
            row.innerHTML = `<span>Member ${index + 1}</span><input type="text" maxlength="80" aria-label="LACP member ${index + 1} source port" placeholder="Source port" value="${escapeHtml(member.sourcePort || '')}"><input type="text" maxlength="80" aria-label="LACP member ${index + 1} target port" placeholder="Target port" value="${escapeHtml(member.targetPort || '')}">`;
            const inputs = row.querySelectorAll('input');
            inputs[0].addEventListener('input', event => { member.sourcePort = event.currentTarget.value; renderLinks(); markDirty(); });
            inputs[1].addEventListener('input', event => { member.targetPort = event.currentTarget.value; renderLinks(); markDirty(); });
            lacpMemberPorts.appendChild(row);
        });
    }

    function escapeHtml(value) {
        return String(value).replace(/[&<>"]/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[char]);
    }

    function updateSelectedLinkField(event) {
        const selectedLink = state.selectedLinkId ? findLinkById(state.selectedLinkId) : null;
        const field = event.currentTarget;
        const propertyName = field.dataset.linkField;
        if (!selectedLink || !propertyName) {
            return;
        }

        if (propertyName === 'mediaType') {
            selectedLink.mediaType = normalizeMediaType(field.value);
            selectedLink.mediaSubtype = normalizeMediaSubtype(selectedLink.mediaSubtype, selectedLink.mediaType);
        } else if (propertyName === 'mediaSubtype') {
            selectedLink.mediaSubtype = normalizeMediaSubtype(field.value, selectedLink.mediaType);
        } else if (propertyName === 'linkType') {
            selectedLink.linkType = normalizeLinkType(field.value);
            selectedLink.lacpMemberCount = normalizeLacpMemberCount(selectedLink.lacpMemberCount, selectedLink.linkType);
            syncLacpMemberPortCount(selectedLink);
        } else if (propertyName === 'linkSpeedPreset') {
            const preset = speedPresets.find(item => item.value === field.value) || speedPresets[0];
            selectedLink.linkSpeedPreset = preset.value;
            if (preset.value !== 'Other') {
                selectedLink.linkSpeedValue = preset.speedValue;
                selectedLink.linkSpeedUnit = preset.unit;
            } else if (!selectedLink.linkSpeedUnit) {
                selectedLink.linkSpeedUnit = 'Gbps';
            }
        } else if (propertyName === 'linkSpeedUnit') {
            selectedLink.linkSpeedUnit = normalizeSpeedUnit(field.value);
            selectedLink.linkSpeedPreset = getSpeedPreset(selectedLink.linkSpeedValue, selectedLink.linkSpeedUnit);
        } else if (propertyName === 'lacpMemberCount') {
            selectedLink.lacpMemberCount = normalizeLacpMemberCount(field.value, selectedLink.linkType);
            syncLacpMemberPortCount(selectedLink);
        } else {
            selectedLink[propertyName] = field.value;
            if (propertyName === 'linkSpeedValue') {
                const parsed = Number(field.value);
                if (field.value !== '' && (!Number.isFinite(parsed) || parsed <= 0)) {
                    selectedLink.linkSpeedValue = '';
                }
                selectedLink.linkSpeedPreset = getSpeedPreset(selectedLink.linkSpeedValue, selectedLink.linkSpeedUnit);
            }
        }
        renderLinks();
        updatePropertiesPanel();
        markDirty();
    }

    function deleteSelection() {
        if (state.selectedAreaId) {
            const confirmed = window.confirm('Remove selected area box from this draft diagram only? Nodes, links, endpoints, monitoring data, dependencies, alerts, and agents will not be changed.');
            if (!confirmed) {
                return;
            }

            const area = selectedArea();
            area?.element.remove();
            state.areas = state.areas.filter(existing => existing.id !== state.selectedAreaId);
            state.selectedAreaId = null;
            markDirty();
            syncSelectionDom();
            return;
        }

        if (state.selectedNodeIds.length > 0) {
            const confirmed = window.confirm('Remove selected item(s) from this draft diagram only? Monitored endpoints and monitoring data will not be deleted.');
            if (!confirmed) {
                return;
            }

            const nodeIds = new Set(state.selectedNodeIds);
            state.nodes = state.nodes.filter(node => {
                if (!nodeIds.has(node.id)) {
                    return true;
                }

                node.element.remove();
                return false;
            });
            state.links = state.links.filter(link => !nodeIds.has(link.sourceNodeId) && !nodeIds.has(link.targetNodeId));
            state.selectedNodeIds = [];
            state.selectedLinkId = null;
            state.activeSelectionType = 'none';
            markDirty();
            setPendingLinkSource(null);
            setEmptyState();
            syncSelectionDom();
            updateEndpointToolboxFilters();
            return;
        }

        if (state.selectedLinkId) {
            const confirmed = window.confirm('Remove this visual link from the draft diagram only? Monitoring dependencies will not be changed.');
            if (!confirmed) {
                return;
            }

            state.links = state.links.filter(link => link.id !== state.selectedLinkId);
            state.selectedLinkId = null;
            state.activeSelectionType = 'none';
            markDirty();
            syncSelectionDom();
        }
    }

    function zoomAt(clientX, clientY, requestedZoom) {
        const nextZoom = clamp(requestedZoom, minimumZoom, maximumZoom);
        const rect = getCanvasRect();
        const screenX = clientX - rect.left;
        const screenY = clientY - rect.top;
        const worldX = (screenX - state.panX) / state.zoom;
        const worldY = (screenY - state.panY) / state.zoom;

        state.zoom = nextZoom;
        state.panX = screenX - worldX * state.zoom;
        state.panY = screenY - worldY * state.zoom;
        updateCanvasSize();
        markDirty();
    }

    function zoomFromCenter(factor) {
        const rect = getCanvasRect();
        zoomAt(rect.left + rect.width / 2, rect.top + rect.height / 2, state.zoom * factor);
    }

    function resetView() {
        state.zoom = 1;
        state.panX = 0;
        state.panY = 0;
        updateCanvasSize();
    }

    function fitContent() {
        if (state.nodes.length === 0 && state.areas.length === 0) {
            resetView();
            return;
        }

        const padding = 96;
        const rect = getCanvasRect();
        const items = [
            ...state.areas.map(area => ({ x: area.x, y: area.y, width: area.width, height: area.height })),
            ...state.nodes.map(node => ({ x: node.x, y: node.y, width: getNodeWidth(node), height: getNodeHeight(node) }))
        ];
        const minX = Math.min(...items.map(item => item.x));
        const minY = Math.min(...items.map(item => item.y));
        const maxX = Math.max(...items.map(item => item.x + item.width));
        const maxY = Math.max(...items.map(item => item.y + item.height));
        const contentWidth = Math.max(1, maxX - minX);
        const contentHeight = Math.max(1, maxY - minY);
        const nextZoom = clamp(Math.min(
            (rect.width - padding) / contentWidth,
            (rect.height - padding) / contentHeight,
            1.5), minimumZoom, maximumZoom);

        state.zoom = nextZoom;
        state.panX = (rect.width - contentWidth * nextZoom) / 2 - minX * nextZoom;
        state.panY = (rect.height - contentHeight * nextZoom) / 2 - minY * nextZoom;
        updateCanvasSize();
        markDirty();
    }

    function beginPan(event) {
        if (event.button !== 0 || event.target.closest('.diagram-node') || event.target.closest('.diagram-area') || event.target.closest('.diagram-link-group')) {
            return;
        }

        panState = {
            pointerId: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            panX: state.panX,
            panY: state.panY,
            moved: false
        };
        canvas.dataset.panning = 'true';
        canvas.setPointerCapture(event.pointerId);
        canvas.focus({ preventScroll: true });
        event.preventDefault();
    }

    function movePan(event) {
        if (!panState || panState.pointerId !== event.pointerId) {
            return;
        }

        const deltaX = event.clientX - panState.startX;
        const deltaY = event.clientY - panState.startY;
        panState.moved = panState.moved || Math.abs(deltaX) > 2 || Math.abs(deltaY) > 2;
        state.panX = panState.panX + deltaX;
        state.panY = panState.panY + deltaY;
        updateCanvasSize();
        event.preventDefault();
    }

    function endPan(event) {
        if (!panState || panState.pointerId !== event.pointerId) {
            return;
        }

        const moved = panState.moved;
        panState = null;
        canvas.dataset.panning = 'false';
        if (canvas.hasPointerCapture(event.pointerId)) {
            canvas.releasePointerCapture(event.pointerId);
        }

        if (moved) {
            markDirty();
        } else {
            clearSelection();
        }
    }

    function isEditableTarget(target) {
        if (!(target instanceof HTMLElement)) {
            return false;
        }

        return Boolean(target.closest('input, textarea, select, [contenteditable="true"]'));
    }

    function handleEditorKeyDown(event) {
        if (isEditableTarget(event.target)) {
            return;
        }

        const isCanvasFocused = canvas.contains(document.activeElement) || document.activeElement === canvas;
        if (!isCanvasFocused) {
            return;
        }

        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a') {
            selectAllNodes();
            event.preventDefault();
            return;
        }

        if (event.key === 'Escape') {
            clearSelection();
            event.preventDefault();
            return;
        }

        if (event.key === 'Delete' || event.key === 'Backspace') {
            deleteSelection();
            event.preventDefault();
        }
    }


    function setSaveStatus(message, options = {}) {
        if (!saveStatus) {
            return;
        }

        saveStatus.textContent = message;
        saveStatus.dataset.dirty = options.dirty ? 'true' : 'false';
        saveStatus.dataset.error = options.error ? 'true' : 'false';
    }

    function markDirty() {
        if (state.loading) {
            return;
        }

        state.dirty = true;
        setSaveStatus('Unsaved changes', { dirty: true });
    }

    function normalizeNodeType(serverType) {
        if (serverType === 'MonitoredEndpoint') {
            return { nodeType: 'monitored-endpoint', nodeKind: 'monitored-endpoint' };
        }
        if (serverType === 'Note') {
            return { nodeType: 'note', nodeKind: 'custom-device' };
        }

        return { nodeType: 'generic-device', nodeKind: 'custom-device' };
    }


    function addLoadedArea(savedArea) {
        const area = {
            id: savedArea.areaId,
            label: savedArea.label || 'Area',
            notes: savedArea.notes || '',
            x: Number(savedArea.x) || 0,
            y: Number(savedArea.y) || 0,
            width: Math.max(80, Number(savedArea.width) || 600),
            height: Math.max(60, Number(savedArea.height) || 350),
            styleKey: normalizeAreaStyleKey(savedArea.styleKey),
            sortOrder: Number(savedArea.sortOrder) || state.areas.length,
            element: null
        };
        area.element = createAreaElement(area);
        areaLayer.appendChild(area.element);
        state.areas.push(area);
        updateAreaElement(area);
    }

    function addLoadedNode(savedNode) {
        const normalized = normalizeNodeType(savedNode.nodeType);
        const node = {
            id: savedNode.nodeId,
            nodeType: normalized.nodeType,
            nodeKind: normalized.nodeKind,
            endpointId: savedNode.endpointId || '',
            endpointName: savedNode.displayLabel,
            label: savedNode.displayLabel,
            target: '',
            iconKey: savedNode.iconKey || 'generic',
            notes: savedNode.notes || '',
            x: savedNode.x,
            y: savedNode.y,
            savedWidth: savedNode.width,
            savedHeight: savedNode.height,
            element: createNodeElement({
                id: savedNode.nodeId,
                label: savedNode.displayLabel,
                target: '',
                type: normalized.nodeType,
                kind: normalized.nodeKind,
                symbol: savedNode.iconKey || 'DEV'
            })
        };

        const endpointButton = savedNode.endpointId
            ? editor.querySelector(`[data-add-endpoint-node][data-endpoint-id="${CSS.escape(savedNode.endpointId)}"]`)
            : null;
        if (endpointButton) {
            node.endpointName = endpointButton.dataset.endpointName || node.label;
            node.target = endpointButton.dataset.endpointTarget || '';
        }

        nodeLayer.appendChild(node.element);
        state.nodes.push(node);
        applyNodePosition(node);
    }

    async function loadDiagram() {
        if (!loadUrl) {
            state.loading = false;
            return;
        }

        const response = await fetch(loadUrl, { headers: { Accept: 'application/json' } });
        if (!response.ok) {
            setSaveStatus('Failed to load diagram', { error: true });
            state.loading = false;
            return;
        }

        const diagram = await response.json();
        state.name = diagram.name || '';
        state.description = diagram.description || '';
        state.virtualCanvasWidth = diagram.canvasWidth || state.virtualCanvasWidth;
        state.virtualCanvasHeight = diagram.canvasHeight || state.virtualCanvasHeight;
        state.panX = diagram.viewportPanX || 0;
        state.panY = diagram.viewportPanY || 0;
        state.zoom = diagram.viewportZoom || 1;
        nodeLayer.replaceChildren();
        areaLayer.replaceChildren();
        state.nodes = [];
        state.links = [];
        state.areas = [];
        (diagram.areas || []).forEach(addLoadedArea);
        (diagram.nodes || []).forEach(addLoadedNode);
        updateEndpointToolboxFilters();
        state.links = (diagram.links || []).map(link => ({
            id: link.linkId,
            sourceNodeId: link.sourceNodeId,
            targetNodeId: link.targetNodeId,
            label: link.label || '',
            sourcePort: link.sourcePortLabel || '',
            targetPort: link.targetPortLabel || '',
            notes: link.notes || '',
            mediaType: normalizeMediaType(link.mediaType, link.linkType),
            mediaSubtype: normalizeMediaSubtype(link.mediaSubtype, link.mediaType),
            linkType: normalizeLinkType(link.linkType),
            linkSpeedValue: link.linkSpeedValue == null ? '' : String(link.linkSpeedValue),
            linkSpeedUnit: normalizeSpeedUnit(link.linkSpeedUnit),
            linkSpeedPreset: getSpeedPreset(link.linkSpeedValue == null ? '' : String(link.linkSpeedValue), link.linkSpeedUnit),
            lacpMemberCount: normalizeLacpMemberCount(link.lacpMemberCount, link.linkType),
            lacpMemberPorts: parseLacpMemberPorts(link.lacpMemberPortsJson),
            vlans: normalizeVlans(link.vlans)
        }));
        state.loading = false;
        state.dirty = false;
        applyViewTransform();
        updateCanvasSize();
        setEmptyState();
        syncSelectionDom();
        setSaveStatus(diagram.updatedAtUtc ? `Saved ${new Date(diagram.updatedAtUtc).toLocaleString()}` : 'Saved');
    }

    function toServerNodeType(node) {
        if (node.nodeKind === 'monitored-endpoint') {
            return 'MonitoredEndpoint';
        }
        if (node.nodeType === 'note') {
            return 'Note';
        }

        return 'CustomDevice';
    }

    function buildSavePayload() {
        return {
            name: state.name || document.querySelector('#network-diagrams-title')?.textContent || 'Network diagram',
            description: state.description || null,
            canvasWidth: state.virtualCanvasWidth,
            canvasHeight: state.virtualCanvasHeight,
            viewportPanX: state.panX,
            viewportPanY: state.panY,
            viewportZoom: state.zoom,
            areas: state.areas.map((area, index) => ({
                areaId: area.id,
                label: area.label || 'Area',
                notes: area.notes || null,
                x: Number(area.x) || 0,
                y: Number(area.y) || 0,
                width: Math.max(80, Number(area.width) || 600),
                height: Math.max(60, Number(area.height) || 350),
                styleKey: normalizeAreaStyleKey(area.styleKey),
                sortOrder: index
            })),
            nodes: state.nodes.map(node => ({
                nodeId: node.id,
                nodeType: toServerNodeType(node),
                endpointId: node.nodeKind === 'monitored-endpoint' ? node.endpointId : null,
                displayLabel: node.label,
                iconKey: node.iconKey || 'generic',
                x: node.x,
                y: node.y,
                width: getNodeWidth(node),
                height: getNodeHeight(node),
                notes: node.notes || null,
                metadataJson: null
            })),
            links: state.links.map(link => ({
                linkId: link.id,
                sourceNodeId: link.sourceNodeId,
                targetNodeId: link.targetNodeId,
                label: link.label || null,
                sourcePortLabel: link.sourcePort || null,
                targetPortLabel: link.targetPort || null,
                notes: link.notes || null,
                mediaType: normalizeMediaType(link.mediaType, link.linkType),
                mediaSubtype: normalizeMediaSubtype(link.mediaSubtype, link.mediaType) || null,
                fibreSubtype: normalizeMediaType(link.mediaType, link.linkType) === 'Fibre' ? normalizeMediaSubtype(link.mediaSubtype, link.mediaType) || null : null,
                linkType: normalizeLinkType(link.linkType),
                linkSpeedValue: link.linkSpeedValue === '' || link.linkSpeedValue == null ? null : Number(link.linkSpeedValue),
                linkSpeedUnit: link.linkSpeedValue === '' || link.linkSpeedValue == null ? null : normalizeSpeedUnit(link.linkSpeedUnit) || null,
                lacpMemberCount: normalizeLinkType(link.linkType) === 'LACP' ? Number(normalizeLacpMemberCount(link.lacpMemberCount, link.linkType)) : null,
                lacpMemberPortsJson: normalizeLinkType(link.linkType) === 'LACP' ? JSON.stringify(link.lacpMemberPorts || []) : null,
                metadataJson: null,
                vlans: normalizeVlans(link.vlans).map((vlan, index) => {
                    const vlanId = validateVlanForSave(vlan);
                    return vlanId === null ? null : {
                        vlanId,
                        name: vlan.name || null,
                        mode: normalizeVlanMode(vlan.mode),
                        notes: vlan.notes || null,
                        sortOrder: index
                    };
                }).filter(Boolean)
            }))
        };
    }

    function exportPdf() {
        if (!exportPdfUrl) {
            return;
        }

        if (state.dirty) {
            const confirmed = window.confirm('Export uses the last saved diagram. Save your changes before exporting if you want them in the PDF. Continue exporting the saved version?');
            if (!confirmed) {
                return;
            }
        }

        const paper = exportPaperSelect ? exportPaperSelect.value : 'A4';
        const separator = exportPdfUrl.includes('?') ? '&' : '?';
        window.location.href = `${exportPdfUrl}${separator}paper=${encodeURIComponent(paper)}`;
    }


    function exportImage(baseUrl, label) {
        if (!baseUrl) {
            return;
        }

        if (state.dirty) {
            const confirmed = window.confirm(`Export uses the last saved diagram layout. Save your changes before exporting if you want them in the ${label}. Continue exporting the saved version?`);
            if (!confirmed) {
                return;
            }
        }

        const scale = exportScaleSelect ? exportScaleSelect.value : '1';
        const separator = baseUrl.includes('?') ? '&' : '?';
        window.location.href = `${baseUrl}${separator}scale=${encodeURIComponent(scale)}&background=light`;
    }

    async function saveDiagram() {
        if (!saveUrl || !saveButton) {
            return;
        }

        saveButton.disabled = true;
        setSaveStatus('Saving…', { dirty: true });
        try {
            const response = await fetch(saveUrl, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    Accept: 'application/json',
                    RequestVerificationToken: antiforgeryToken
                },
                body: JSON.stringify(buildSavePayload())
            });
            const result = await response.json().catch(() => ({}));
            if (!response.ok) {
                throw new Error(result.error || 'Save failed.');
            }

            state.dirty = false;
            setSaveStatus(result.updatedAtUtc ? `Saved ${new Date(result.updatedAtUtc).toLocaleString()}` : 'Saved');
        } catch (error) {
            setSaveStatus(error.message || 'Save failed', { dirty: true, error: true });
        } finally {
            saveButton.disabled = false;
        }
    }

    addButtons.forEach(button => {
        button.addEventListener('click', () => addCustomNode(button));
    });

    addEndpointButtons.forEach(button => {
        button.addEventListener('click', () => addEndpointNode(button));
    });

    addAreaButton?.addEventListener('click', addArea);

    endpointSearchInput?.addEventListener('input', updateEndpointToolboxFilters);
    endpointSearchInput?.addEventListener('keydown', event => {
        event.stopPropagation();
        if (event.key === 'Escape' && endpointSearchInput.value) {
            endpointSearchInput.value = '';
            updateEndpointToolboxFilters();
            event.preventDefault();
        }
    });
    clearEndpointSearchButton?.addEventListener('click', () => {
        if (endpointSearchInput) {
            endpointSearchInput.value = '';
        }
        updateEndpointToolboxFilters();
        endpointSearchInput?.focus();
    });
    endpointGroupFilter?.addEventListener('change', updateEndpointToolboxFilters);
    endpointGroupFilter?.addEventListener('keydown', event => event.stopPropagation());
    hideExistingEndpointsCheckbox?.addEventListener('change', updateEndpointToolboxFilters);
    clearEndpointFiltersButton?.addEventListener('click', clearEndpointFilters);
    updateEndpointToolboxFilters();

    toolButtons.forEach(button => {
        button.addEventListener('click', () => setTool(button.dataset.toolButton || 'select'));
    });

    if (drawMediaTypeSelect) {
        drawMediaTypeSelect.addEventListener('change', () => {
            state.selectedDrawMediaType = normalizeMediaType(drawMediaTypeSelect.value);
            drawMediaTypeSelect.value = state.selectedDrawMediaType;
        });
    }

    if (drawLinkTypeSelect) {
        drawLinkTypeSelect.addEventListener('change', () => {
            state.selectedDrawLinkType = normalizeLinkType(drawLinkTypeSelect.value);
            drawLinkTypeSelect.value = state.selectedDrawLinkType;
        });
    }

    areaFields.forEach(field => {
        field.addEventListener('input', updateSelectedAreaField);
    });

    nodeFields.forEach(field => {
        field.addEventListener('input', updateSelectedNodeField);
    });

    linkFields.forEach(field => {
        field.addEventListener('input', updateSelectedLinkField);
    });

    deleteSelectionButtons.forEach(button => {
        button.addEventListener('click', deleteSelection);
    });

    if (selectAllButton) {
        selectAllButton.addEventListener('click', selectAllNodes);
    }

    if (clearSelectionButton) {
        clearSelectionButton.addEventListener('click', clearSelection);
    }

    if (zoomInButton) {
        zoomInButton.addEventListener('click', () => zoomFromCenter(zoomStep));
    }

    if (zoomOutButton) {
        zoomOutButton.addEventListener('click', () => zoomFromCenter(1 / zoomStep));
    }

    if (resetViewButton) {
        resetViewButton.addEventListener('click', resetView);
    }

    if (fitContentButton) {
        fitContentButton.addEventListener('click', fitContent);
    }

    if (canvasSizePresetSelect) {
        canvasSizePresetSelect.addEventListener('change', event => applyCanvasPreset(event.currentTarget.value));
    }

    if (exportPdfButton) {
        exportPdfButton.addEventListener('click', exportPdf);
    }
    if (exportPngButton) {
        exportPngButton.addEventListener('click', () => exportImage(exportPngUrl, 'PNG'));
    }
    if (exportSvgButton) {
        exportSvgButton.addEventListener('click', () => exportImage(exportSvgUrl, 'SVG'));
    }

    if (addLinkVlanButton) {
        addLinkVlanButton.addEventListener('click', addLinkVlan);
    }

    if (saveButton) {
        saveButton.addEventListener('click', saveDiagram);
    }

    window.addEventListener('beforeunload', event => {
        if (!state.dirty) {
            return;
        }

        event.preventDefault();
        event.returnValue = '';
    });

    areaLayer?.addEventListener('pointerdown', handleAreaPointerDown);
    nodeLayer.addEventListener('pointerdown', handleNodePointerDown);
    nodeLayer.addEventListener('pointermove', moveDrag);
    nodeLayer.addEventListener('pointerup', endDrag);
    nodeLayer.addEventListener('pointercancel', endDrag);
    nodeLayer.addEventListener('keydown', event => {
        if (event.target instanceof HTMLElement && event.target.classList.contains('diagram-node')) {
            nudgeSelectedNodes(event);
        }
    });

    canvas.addEventListener('pointerdown', beginPan);
    canvas.addEventListener('pointermove', movePan);
    canvas.addEventListener('pointerup', endPan);
    canvas.addEventListener('pointercancel', endPan);
    canvas.addEventListener('keydown', handleEditorKeyDown);
    canvas.addEventListener('wheel', event => {
        const factor = event.deltaY < 0 ? zoomStep : 1 / zoomStep;
        zoomAt(event.clientX, event.clientY, state.zoom * factor);
        event.preventDefault();
    }, { passive: false });

    const resizeObserver = 'ResizeObserver' in window
        ? new ResizeObserver(updateCanvasSize)
        : null;

    if (resizeObserver) {
        resizeObserver.observe(canvasHost);
    }

    window.addEventListener('resize', updateCanvasSize);
    applyViewTransform();
    updateCanvasSize();
    setTool('select');
    setEmptyState();
    updatePropertiesPanel();
    updateSelectionButtons();
    loadDiagram();
})();
