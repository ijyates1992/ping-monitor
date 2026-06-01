(() => {
    const editor = document.querySelector('[data-network-diagram-editor]');
    if (!editor) {
        return;
    }

    const nav = document.querySelector('.site-nav');
    const canvasHost = editor.querySelector('[data-diagram-canvas-host]');
    const canvas = editor.querySelector('[data-diagram-canvas]');
    const nodeLayer = editor.querySelector('[data-node-layer]');
    const linkLayer = editor.querySelector('[data-link-layer]');
    const emptyState = editor.querySelector('[data-empty-state]');
    const sizeLabel = editor.querySelector('[data-canvas-size]');
    const addButtons = Array.from(editor.querySelectorAll('[data-add-node]'));
    const addEndpointButtons = Array.from(editor.querySelectorAll('[data-add-endpoint-node]'));
    const toolButtons = Array.from(editor.querySelectorAll('[data-tool-button]'));
    const toolHint = editor.querySelector('[data-tool-hint]');
    const linkProperties = editor.querySelector('[data-link-properties]');
    const noSelectionPanel = editor.querySelector('[data-no-selection-panel]');
    const deleteLinkButton = editor.querySelector('[data-delete-link]');
    const linkFields = Array.from(editor.querySelectorAll('[data-link-field]'));

    if (!canvasHost || !canvas || !nodeLayer || !linkLayer) {
        return;
    }

    const state = {
        nodes: [],
        links: [],
        selectedNodeId: null,
        selectedLinkId: null,
        currentTool: 'select'
    };

    let nodeSequence = 0;
    let linkSequence = 0;
    let dragState = null;
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

    function clampNodePosition(node) {
        const rect = getCanvasRect();
        const width = node.element.offsetWidth || 178;
        const height = node.element.offsetHeight || 78;
        const margin = 8;

        node.x = clamp(node.x, margin, rect.width - width - margin);
        node.y = clamp(node.y, margin, rect.height - height - margin);
        node.element.style.transform = `translate(${Math.round(node.x)}px, ${Math.round(node.y)}px)`;
        renderLinks();
    }

    function clampAllNodes() {
        state.nodes.forEach(clampNodePosition);
    }

    function updateCanvasSize() {
        updateNavHeight();

        const rect = getCanvasRect();
        canvasHost.style.setProperty('--diagram-canvas-width', `${Math.round(rect.width)}px`);
        canvasHost.style.setProperty('--diagram-canvas-height', `${Math.round(rect.height)}px`);
        linkLayer.setAttribute('viewBox', `0 0 ${Math.max(1, Math.round(rect.width))} ${Math.max(1, Math.round(rect.height))}`);

        if (sizeLabel) {
            sizeLabel.textContent = `${Math.round(rect.width)} x ${Math.round(rect.height)} canvas`;
        }

        clampAllNodes();
        renderLinks();
    }

    function formatNodeType(type) {
        return type.replace(/-/g, ' ');
    }

    function setEmptyState() {
        if (emptyState) {
            emptyState.hidden = state.nodes.length > 0;
        }
    }

    function createNodeElement(options) {
        const node = document.createElement('div');
        node.className = 'diagram-node';
        node.tabIndex = 0;
        node.setAttribute('role', 'button');
        node.setAttribute('aria-label', `${options.label} draft ${formatNodeType(options.type)} node`);
        node.dataset.nodeId = options.id;
        node.dataset.nodeKind = options.kind;

        const symbol = document.createElement('span');
        symbol.className = 'diagram-node-symbol';
        symbol.textContent = options.symbol;
        symbol.setAttribute('aria-hidden', 'true');

        const main = document.createElement('span');
        main.className = 'diagram-node-main';

        const name = document.createElement('span');
        name.className = 'diagram-node-name';
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
        draft.textContent = options.kind === 'monitored-endpoint' ? 'Draft visual instance' : 'Draft';
        main.appendChild(draft);

        node.append(symbol, main);
        return node;
    }

    function getNextNodePosition() {
        nodeSequence += 1;
        return {
            id: `draft-node-${nodeSequence}`,
            x: 24 + ((nodeSequence - 1) % 6) * 28,
            y: 24 + ((nodeSequence - 1) % 8) * 24
        };
    }

    function addNodeFromOptions(options) {
        const rect = getCanvasRect();
        const position = getNextNodePosition();
        const node = {
            id: position.id,
            nodeType: options.type,
            nodeKind: options.kind,
            endpointId: options.endpointId || '',
            label: options.label,
            target: options.target || '',
            iconKey: options.iconKey || options.symbol || '',
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

        node.x = clamp(node.x, 8, rect.width - node.element.offsetWidth - 8);
        node.y = clamp(node.y, 8, rect.height - node.element.offsetHeight - 8);
        clampNodePosition(node);
        setEmptyState();
        selectNode(node.id);
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
                ? 'Draw link mode: select a source node, then select a different target node.'
                : 'Select nodes to move them. Draw link mode creates documentation-only links.';
        }
    }

    function selectNode(nodeId) {
        state.selectedNodeId = nodeId;
        state.selectedLinkId = null;
        state.nodes.forEach(node => {
            node.element.dataset.selected = node.id === nodeId ? 'true' : 'false';
        });
        renderLinks();
        updatePropertiesPanel();
    }

    function selectLink(linkId) {
        state.selectedLinkId = linkId;
        state.selectedNodeId = null;
        state.nodes.forEach(node => {
            node.element.dataset.selected = 'false';
        });
        renderLinks();
        updatePropertiesPanel();
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
            id: `draft-link-${linkSequence}`,
            sourceNodeId,
            targetNodeId,
            label: '',
            sourcePort: '',
            targetPort: '',
            notes: ''
        };

        state.links.push(link);
        selectLink(link.id);
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
                selectNode(node.id);
                return;
            }

            createLink(pendingLinkSourceId, node.id);
            setPendingLinkSource(null);
            return;
        }

        const rect = getCanvasRect();
        dragState = {
            node,
            offsetX: event.clientX - rect.left - node.x,
            offsetY: event.clientY - rect.top - node.y
        };

        selectNode(node.id);
        target.dataset.dragging = 'true';
        target.setPointerCapture(event.pointerId);
        event.preventDefault();
    }

    function moveDrag(event) {
        if (!dragState) {
            return;
        }

        const rect = getCanvasRect();
        dragState.node.x = event.clientX - rect.left - dragState.offsetX;
        dragState.node.y = event.clientY - rect.top - dragState.offsetY;
        clampNodePosition(dragState.node);
        event.preventDefault();
    }

    function endDrag(event) {
        if (!dragState) {
            return;
        }

        dragState.node.element.dataset.dragging = 'false';
        if (dragState.node.element.hasPointerCapture(event.pointerId)) {
            dragState.node.element.releasePointerCapture(event.pointerId);
        }
        dragState = null;
    }

    function nudgeNode(event) {
        const node = findNodeByElement(event.currentTarget);
        if (!node || state.currentTool !== 'select') {
            return;
        }

        const step = event.shiftKey ? 24 : 8;
        let handled = true;

        if (event.key === 'ArrowLeft') {
            node.x -= step;
        } else if (event.key === 'ArrowRight') {
            node.x += step;
        } else if (event.key === 'ArrowUp') {
            node.y -= step;
        } else if (event.key === 'ArrowDown') {
            node.y += step;
        } else {
            handled = false;
        }

        if (handled) {
            selectNode(node.id);
            clampNodePosition(node);
            event.preventDefault();
        }
    }

    function getNodeCenter(node) {
        return {
            x: node.x + (node.element.offsetWidth || 178) / 2,
            y: node.y + (node.element.offsetHeight || 78) / 2
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
        if (linkProperties) {
            linkProperties.hidden = !selectedLink;
        }
        if (noSelectionPanel) {
            noSelectionPanel.hidden = Boolean(selectedLink);
        }

        linkFields.forEach(field => {
            const propertyName = field.dataset.linkField;
            field.value = selectedLink && propertyName ? selectedLink[propertyName] || '' : '';
        });
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
    }

    function deleteSelectedLink() {
        if (!state.selectedLinkId) {
            return;
        }

        state.links = state.links.filter(link => link.id !== state.selectedLinkId);
        state.selectedLinkId = null;
        renderLinks();
        updatePropertiesPanel();
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

    linkFields.forEach(field => {
        field.addEventListener('input', updateSelectedLinkField);
    });

    if (deleteLinkButton) {
        deleteLinkButton.addEventListener('click', deleteSelectedLink);
    }

    nodeLayer.addEventListener('pointerdown', handleNodePointerDown);
    nodeLayer.addEventListener('pointermove', moveDrag);
    nodeLayer.addEventListener('pointerup', endDrag);
    nodeLayer.addEventListener('pointercancel', endDrag);
    nodeLayer.addEventListener('keydown', event => {
        if (event.target instanceof HTMLElement && event.target.classList.contains('diagram-node')) {
            nudgeNode(event);
        }
    });

    canvas.addEventListener('pointerdown', event => {
        if (event.target === canvas || event.target === linkLayer) {
            state.selectedNodeId = null;
            state.selectedLinkId = null;
            state.nodes.forEach(node => {
                node.element.dataset.selected = 'false';
            });
            renderLinks();
            updatePropertiesPanel();
        }
    });

    const resizeObserver = 'ResizeObserver' in window
        ? new ResizeObserver(updateCanvasSize)
        : null;

    if (resizeObserver) {
        resizeObserver.observe(canvasHost);
    }

    window.addEventListener('resize', updateCanvasSize);
    updateCanvasSize();
    setTool('select');
    setEmptyState();
    updatePropertiesPanel();
})();
