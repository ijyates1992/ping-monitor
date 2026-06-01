(() => {
    const editor = document.querySelector('[data-network-diagram-editor]');
    if (!editor) {
        return;
    }

    const nav = document.querySelector('.site-nav-shell');
    const canvasHost = editor.querySelector('[data-diagram-canvas-host]');
    const nodeLayer = editor.querySelector('[data-node-layer]');
    const emptyState = editor.querySelector('[data-empty-state]');
    const sizeLabel = editor.querySelector('[data-canvas-size]');
    const addButtons = Array.from(editor.querySelectorAll('[data-add-node]'));

    if (!canvasHost || !nodeLayer) {
        return;
    }

    const nodes = [];
    let nodeSequence = 0;
    let dragState = null;

    function updateNavHeight() {
        const height = nav ? Math.ceil(nav.getBoundingClientRect().height) : 0;
        document.documentElement.style.setProperty('--network-diagrams-nav-height', `${height}px`);
    }

    function getCanvasRect() {
        return canvasHost.getBoundingClientRect();
    }

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), Math.max(min, max));
    }

    function clampNodePosition(node) {
        const rect = getCanvasRect();
        const width = node.element.offsetWidth || 178;
        const height = node.element.offsetHeight || 78;
        const margin = 8;

        node.x = clamp(node.x, margin, rect.width - width - margin);
        node.y = clamp(node.y, margin, rect.height - height - margin);
        node.element.style.transform = `translate(${Math.round(node.x)}px, ${Math.round(node.y)}px)`;
    }

    function clampAllNodes() {
        nodes.forEach(clampNodePosition);
    }

    function updateCanvasSize() {
        updateNavHeight();

        const rect = getCanvasRect();
        canvasHost.style.setProperty('--diagram-canvas-width', `${Math.round(rect.width)}px`);
        canvasHost.style.setProperty('--diagram-canvas-height', `${Math.round(rect.height)}px`);

        if (sizeLabel) {
            sizeLabel.textContent = `${Math.round(rect.width)} x ${Math.round(rect.height)} canvas`;
        }

        clampAllNodes();
    }

    function formatNodeType(type) {
        return type.replace(/-/g, ' ');
    }

    function setEmptyState() {
        if (emptyState) {
            emptyState.hidden = nodes.length > 0;
        }
    }

    function createNodeElement(options) {
        const node = document.createElement('div');
        node.className = 'diagram-node';
        node.tabIndex = 0;
        node.setAttribute('role', 'button');
        node.setAttribute('aria-label', `${options.label} draft ${formatNodeType(options.type)} node`);
        node.dataset.nodeId = options.id;

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
        type.textContent = formatNodeType(options.type);

        const draft = document.createElement('span');
        draft.className = 'diagram-node-draft';
        draft.textContent = 'Draft';

        main.append(name, type, draft);
        node.append(symbol, main);
        return node;
    }

    function addNode(button) {
        const rect = getCanvasRect();
        nodeSequence += 1;

        const node = {
            id: `draft-node-${nodeSequence}`,
            x: 24 + ((nodeSequence - 1) % 6) * 28,
            y: 24 + ((nodeSequence - 1) % 8) * 24,
            element: createNodeElement({
                id: `draft-node-${nodeSequence}`,
                label: button.dataset.nodeLabel || 'Device',
                type: button.dataset.nodeType || 'generic-device',
                symbol: button.dataset.nodeSymbol || 'DEV'
            })
        };

        nodeLayer.appendChild(node.element);
        nodes.push(node);

        node.x = clamp(node.x, 8, rect.width - node.element.offsetWidth - 8);
        node.y = clamp(node.y, 8, rect.height - node.element.offsetHeight - 8);
        clampNodePosition(node);
        setEmptyState();
        node.element.focus({ preventScroll: true });
    }

    function findNodeByElement(element) {
        return nodes.find(node => node.element === element);
    }

    function startDrag(event) {
        const target = event.target.closest('.diagram-node');
        if (!target || !nodeLayer.contains(target)) {
            return;
        }

        const node = findNodeByElement(target);
        if (!node) {
            return;
        }

        const rect = getCanvasRect();
        dragState = {
            node,
            offsetX: event.clientX - rect.left - node.x,
            offsetY: event.clientY - rect.top - node.y
        };

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
        if (!node) {
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
            clampNodePosition(node);
            event.preventDefault();
        }
    }

    addButtons.forEach(button => {
        button.addEventListener('click', () => addNode(button));
    });

    nodeLayer.addEventListener('pointerdown', startDrag);
    nodeLayer.addEventListener('pointermove', moveDrag);
    nodeLayer.addEventListener('pointerup', endDrag);
    nodeLayer.addEventListener('pointercancel', endDrag);
    nodeLayer.addEventListener('keydown', event => {
        if (event.target instanceof HTMLElement && event.target.classList.contains('diagram-node')) {
            nudgeNode(event);
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
    setEmptyState();
})();
