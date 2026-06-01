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
    const toolButtons = Array.from(editor.querySelectorAll('[data-tool-button]'));
    const toolHint = editor.querySelector('[data-tool-hint]');
    const nodeProperties = editor.querySelector('[data-node-properties]');
    const multiNodeProperties = editor.querySelector('[data-multi-node-properties]');
    const linkProperties = editor.querySelector('[data-link-properties]');
    const noSelectionPanel = editor.querySelector('[data-no-selection-panel]');
    const nodeFields = Array.from(editor.querySelectorAll('[data-node-field]'));
    const linkFields = Array.from(editor.querySelectorAll('[data-link-field]'));
    const selectedNodeKindLabel = editor.querySelector('[data-selected-node-kind]');
    const selectedNodeEndpointDetails = editor.querySelector('[data-selected-node-endpoint-details]');
    const selectedNodeEndpointName = editor.querySelector('[data-selected-node-endpoint-name]');
    const selectedNodeEndpointTarget = editor.querySelector('[data-selected-node-endpoint-target]');
    const selectedNodeTypeLabel = editor.querySelector('[data-selected-node-type]');
    const selectedNodeHelp = editor.querySelector('[data-selected-node-help]');
    const selectedNodeCount = editor.querySelector('[data-selected-node-count]');
    const saveButton = editor.querySelector('[data-save-diagram]');
    const exportPdfButton = editor.querySelector('[data-export-pdf]');
    const exportPaperSelect = editor.querySelector('[data-export-paper]');
    const canvasSizePresetSelect = editor.querySelector('[data-canvas-size-preset]');
    const canvasRatioWarning = editor.querySelector('[data-canvas-ratio-warning]');
    const saveStatus = editor.querySelector('[data-save-status]');
    const antiforgeryToken = editor.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const loadUrl = editor.dataset.loadUrl || '';
    const saveUrl = editor.dataset.saveUrl || '';
    const exportPdfUrl = editor.dataset.exportPdfUrl || '';

    if (!canvasHost || !canvas || !world || !nodeLayer || !linkLayer) {
        return;
    }

    const minimumZoom = 0.25;
    const maximumZoom = 3;
    const zoomStep = 1.1;
    const nodeMargin = 8;
    const aSeriesLandscapeRatio = 1.41421356237;
    const canvasPresets = [
        { value: 'small', label: 'Small', width: 4000, height: 2828 },
        { value: 'medium', label: 'Medium', width: 5656, height: 4000 },
        { value: 'large', label: 'Large', width: 8000, height: 5657 },
        { value: 'extra-large', label: 'Extra large', width: 11314, height: 8000 }
    ];

    const state = {
        nodes: [],
        links: [],
        selectedNodeIds: [],
        selectedLinkId: null,
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
        loading: true
    };

    let nodeSequence = 0;
    let linkSequence = 0;
    let dragState = null;
    let panState = null;
    let pendingLinkSourceId = null;

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
            emptyState.hidden = state.nodes.length > 0;
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

    function findNodeByElement(element) {
        return state.nodes.find(node => node.element === element);
    }

    function findNodeById(nodeId) {
        return state.nodes.find(node => node.id === nodeId) || null;
    }

    function findLinkById(linkId) {
        return state.links.find(link => link.id === linkId) || null;
    }

    function selectedNodes() {
        return state.selectedNodeIds.map(findNodeById).filter(Boolean);
    }

    function hasSelection() {
        return state.selectedNodeIds.length > 0 || Boolean(state.selectedLinkId);
    }

    function syncSelectionDom() {
        state.nodes.forEach(node => {
            node.element.dataset.selected = state.selectedNodeIds.includes(node.id) ? 'true' : 'false';
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
        state.activeSelectionType = 'nodes';
        syncSelectionDom();
    }

    function toggleNodeSelection(nodeId) {
        state.selectedLinkId = null;
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
        state.activeSelectionType = state.selectedNodeIds.length > 0 ? 'nodes' : 'none';
        syncSelectionDom();
    }

    function clearSelection() {
        state.selectedNodeIds = [];
        state.selectedLinkId = null;
        state.activeSelectionType = 'none';
        syncSelectionDom();
    }

    function selectLink(linkId) {
        state.selectedLinkId = linkId;
        state.selectedNodeIds = [];
        state.activeSelectionType = 'link';
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

    function linkExists(sourceNodeId, targetNodeId) {
        return state.links.some(link =>
            (link.sourceNodeId === sourceNodeId && link.targetNodeId === targetNodeId) ||
            (link.sourceNodeId === targetNodeId && link.targetNodeId === sourceNodeId));
    }

    function createLink(sourceNodeId, targetNodeId) {
        if (sourceNodeId === targetNodeId || linkExists(sourceNodeId, targetNodeId)) {
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
            notes: ''
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

    function renderLinks() {
        linkLayer.replaceChildren();

        state.links.forEach(link => {
            const source = findNodeById(link.sourceNodeId);
            const target = findNodeById(link.targetNodeId);
            if (!source || !target) {
                return;
            }

            const start = getNodeCenter(source);
            const end = getNodeCenter(target);
            const group = createSvgElement('g');
            group.classList.add('diagram-link-group');
            group.dataset.linkId = link.id;
            group.dataset.selected = state.selectedLinkId === link.id ? 'true' : 'false';

            const hit = createSvgElement('line');
            hit.classList.add('diagram-link-hit');
            setLineCoordinates(hit, start, end);
            hit.addEventListener('pointerdown', event => {
                selectLink(link.id);
                event.preventDefault();
                event.stopPropagation();
            });

            const line = createSvgElement('line');
            line.classList.add('diagram-link-line');
            setLineCoordinates(line, start, end);

            group.append(hit, line);

            if (link.label) {
                const label = createSvgElement('text');
                label.classList.add('diagram-link-label');
                label.setAttribute('x', String((start.x + end.x) / 2));
                label.setAttribute('y', String((start.y + end.y) / 2 - 8));
                label.setAttribute('text-anchor', 'middle');
                label.textContent = link.label;
                group.appendChild(label);
            }

            if (link.sourcePort) {
                const sourcePort = createSvgElement('text');
                sourcePort.classList.add('diagram-link-port-label');
                sourcePort.setAttribute('x', String(start.x + (end.x - start.x) * 0.18));
                sourcePort.setAttribute('y', String(start.y + (end.y - start.y) * 0.18 - 6));
                sourcePort.setAttribute('text-anchor', 'middle');
                sourcePort.textContent = link.sourcePort;
                group.appendChild(sourcePort);
            }

            if (link.targetPort) {
                const targetPort = createSvgElement('text');
                targetPort.classList.add('diagram-link-port-label');
                targetPort.setAttribute('x', String(start.x + (end.x - start.x) * 0.82));
                targetPort.setAttribute('y', String(start.y + (end.y - start.y) * 0.82 - 6));
                targetPort.setAttribute('text-anchor', 'middle');
                targetPort.textContent = link.targetPort;
                group.appendChild(targetPort);
            }

            linkLayer.appendChild(group);
        });
    }

    function setLineCoordinates(line, start, end) {
        line.setAttribute('x1', String(start.x));
        line.setAttribute('y1', String(start.y));
        line.setAttribute('x2', String(end.x));
        line.setAttribute('y2', String(end.y));
    }

    function updatePropertiesPanel() {
        const selectedLink = state.selectedLinkId ? findLinkById(state.selectedLinkId) : null;
        const nodes = selectedNodes();
        const singleNode = nodes.length === 1 ? nodes[0] : null;
        const multipleNodes = nodes.length > 1;

        if (noSelectionPanel) {
            noSelectionPanel.hidden = Boolean(selectedLink) || Boolean(singleNode) || multipleNodes;
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

        nodeFields.forEach(field => {
            const propertyName = field.dataset.nodeField;
            field.value = singleNode && propertyName ? singleNode[propertyName] || '' : '';
        });

        linkFields.forEach(field => {
            const propertyName = field.dataset.linkField;
            field.value = selectedLink && propertyName ? selectedLink[propertyName] || '' : '';
        });
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

    function updateSelectedLinkField(event) {
        const selectedLink = state.selectedLinkId ? findLinkById(state.selectedLinkId) : null;
        const field = event.currentTarget;
        const propertyName = field.dataset.linkField;
        if (!selectedLink || !propertyName) {
            return;
        }

        selectedLink[propertyName] = field.value;
        renderLinks();
        markDirty();
    }

    function deleteSelection() {
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
        if (state.nodes.length === 0) {
            resetView();
            return;
        }

        const padding = 96;
        const rect = getCanvasRect();
        const minX = Math.min(...state.nodes.map(node => node.x));
        const minY = Math.min(...state.nodes.map(node => node.y));
        const maxX = Math.max(...state.nodes.map(node => node.x + getNodeWidth(node)));
        const maxY = Math.max(...state.nodes.map(node => node.y + getNodeHeight(node)));
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
        if (event.button !== 0 || event.target.closest('.diagram-node') || event.target.closest('.diagram-link-group')) {
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
        state.nodes = [];
        state.links = [];
        (diagram.nodes || []).forEach(addLoadedNode);
        state.links = (diagram.links || []).map(link => ({
            id: link.linkId,
            sourceNodeId: link.sourceNodeId,
            targetNodeId: link.targetNodeId,
            label: link.label || '',
            sourcePort: link.sourcePortLabel || '',
            targetPort: link.targetPortLabel || '',
            notes: link.notes || ''
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
                linkType: 'default',
                metadataJson: null
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

    toolButtons.forEach(button => {
        button.addEventListener('click', () => setTool(button.dataset.toolButton || 'select'));
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
