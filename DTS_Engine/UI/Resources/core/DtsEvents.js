/**
 * DtsEvents.js - Canvas Event Handlers
 * Manages pan, zoom, box-zoom, and mouse interactions.
 * FIX: Restricted selection to Left Click only.
 * FIX: Restored 'dblclick' emission for Rebar Editing.
 */
(function (global) {
    'use strict';

    const DtsEvents = {
        // ===== INTERNAL STATE =====
        _canvas: null,
        _isDragging: false,
        _isBoxZoom: false,
        _lastX: 0,
        _lastY: 0,
        _boxStartX: 0,
        _boxStartY: 0,
        _boxEndX: 0,
        _boxEndY: 0,
        _onRender: null,
        _onBoxZoomDraw: null,

        /**
         * Initialize event handlers on canvas
         */
        init(canvas, renderCallback) {
            this._canvas = canvas;
            this._onRender = renderCallback;

            canvas.addEventListener('mousedown', this._onMouseDown.bind(this));
            canvas.addEventListener('mousemove', this._onMouseMove.bind(this));
            canvas.addEventListener('mouseup', this._onMouseUp.bind(this));
            canvas.addEventListener('mouseleave', this._onMouseLeave.bind(this));
            canvas.addEventListener('wheel', this._onWheel.bind(this), { passive: false });
            canvas.addEventListener('dblclick', this._onDblClick.bind(this));
            canvas.addEventListener('click', this._onClick.bind(this));
        },

        setBoxZoomDrawCallback(callback) {
            this._onBoxZoomDraw = callback;
        },

        // ===== EVENT HANDLERS =====

        _onMouseDown(e) {
            const rect = this._canvas.getBoundingClientRect();

            // Ctrl+Left = Box Zoom
            if (e.button === 0 && e.ctrlKey) {
                this._isBoxZoom = true;
                this._boxStartX = e.clientX - rect.left;
                this._boxStartY = e.clientY - rect.top;
                this._boxEndX = this._boxStartX;
                this._boxEndY = this._boxStartY;
                this._canvas.style.cursor = 'crosshair';
                e.preventDefault();
                return;
            }

            // Middle click or Alt+Left = Pan
            if (e.button === 1 || (e.button === 0 && e.altKey)) {
                this._isDragging = true;
                this._lastX = e.clientX;
                this._lastY = e.clientY;
                this._canvas.style.cursor = 'grabbing';
                e.preventDefault();
            }
        },

        _onMouseMove(e) {
            const rect = this._canvas.getBoundingClientRect();
            const state = global.Dts?.State;

            if (this._isBoxZoom) {
                this._boxEndX = e.clientX - rect.left;
                this._boxEndY = e.clientY - rect.top;
                if (this._onRender) this._onRender();
                return;
            }

            if (this._isDragging && state) {
                const dx = e.clientX - this._lastX;
                const dy = e.clientY - this._lastY;
                state.panX += dx;
                state.panY += dy;
                this._lastX = e.clientX;
                this._lastY = e.clientY;
                if (this._onRender) this._onRender();
                return;
            }

            // Emit mousemove for hover effects
            const physics = global.Dts?.Physics;
            if (physics && state) {
                const pos = physics.screenToCanvas(e.clientX, e.clientY, rect);
                state.emit?.('mousemove', pos.x, pos.y, e);
            }
        },

        _onMouseUp(e) {
            const state = global.Dts?.State;

            if (this._isBoxZoom) {
                this._isBoxZoom = false;
                this._canvas.style.cursor = 'default';

                const x1 = Math.min(this._boxStartX, this._boxEndX);
                const y1 = Math.min(this._boxStartY, this._boxEndY);
                const x2 = Math.max(this._boxStartX, this._boxEndX);
                const y2 = Math.max(this._boxStartY, this._boxEndY);
                const boxW = x2 - x1;
                const boxH = y2 - y1;

                if (boxW > 20 && boxH > 20 && state) {
                    const worldX1 = (x1 - state.panX) / state.zoom;
                    const worldY1 = (y1 - state.panY) / state.zoom;
                    const worldX2 = (x2 - state.panX) / state.zoom;
                    const worldY2 = (y2 - state.panY) / state.zoom;

                    const worldCenterX = (worldX1 + worldX2) / 2;
                    const worldCenterY = (worldY1 + worldY2) / 2;

                    // Zoom logic...
                    const canvasW = this._canvas.width;
                    const canvasH = this._canvas.height;
                    // Simple approximation for zoom fit
                    const newZoom = Math.min(canvasW / (worldX2 - worldX1), canvasH / (worldY2 - worldY1)) * 0.9;

                    state.zoom = Math.max(0.5, Math.min(5, newZoom));
                    state.panX = canvasW / 2 - worldCenterX * state.zoom;
                    state.panY = canvasH / 2 - worldCenterY * state.zoom;

                    if (this._onRender) this._onRender();
                }
                return;
            }

            this._isDragging = false;
            this._canvas.style.cursor = 'default';
        },

        _onMouseLeave(e) {
            this._isDragging = false;
            this._isBoxZoom = false;
            this._canvas.style.cursor = 'default';
        },

        _onWheel(e) {
            e.preventDefault();
            const state = global.Dts?.State;
            if (!state) return;

            const delta = e.deltaY > 0 ? 0.9 : 1.1;
            const newZoom = Math.max(0.5, Math.min(5, state.zoom * delta));
            const rect = this._canvas.getBoundingClientRect();
            const mouseX = e.clientX - rect.left;
            const mouseY = e.clientY - rect.top;

            state.panX = mouseX - (mouseX - state.panX) * (newZoom / state.zoom);
            state.panY = mouseY - (mouseY - state.panY) * (newZoom / state.zoom);
            state.zoom = newZoom;

            if (this._onRender) this._onRender();
        },

        _onDblClick(e) {
            // FIX: Restore dblclick emission instead of just resetting view
            const state = global.Dts?.State;
            const physics = global.Dts?.Physics;
            const rect = this._canvas.getBoundingClientRect();

            if (state && physics) {
                // Convert screen to world coordinates so we can hit-test labels
                const pos = physics.screenToCanvas(e.clientX, e.clientY, rect);

                // Emit dblclick event for BeamInit/BeamState to handle (e.g., editing rebar)
                state.emit?.('dblclick', pos.x, pos.y, e);

                // NOTE: We removed state.resetView() here because it conflicts with editing.
                // If you want "Double click background to reset", implement logic in the listener
                // to check if nothing was hit.
            }
        },

        _onClick(e) {
            // FIX: STRICTLY ALLOW ONLY LEFT CLICK (Button 0)
            if (e.button !== 0) return;

            const rect = this._canvas.getBoundingClientRect();
            const physics = global.Dts?.Physics;
            const state = global.Dts?.State;

            if (physics && state) {
                const pos = physics.screenToCanvas(e.clientX, e.clientY, rect);
                state.emit?.('click', pos.x, pos.y, e);
            }
        },

        isBoxZoomActive() { return this._isBoxZoom; },
        getBoxZoomRect() {
            return {
                x1: Math.min(this._boxStartX, this._boxEndX),
                y1: Math.min(this._boxStartY, this._boxEndY),
                x2: Math.max(this._boxStartX, this._boxEndX),
                y2: Math.max(this._boxStartY, this._boxEndY)
            };
        }
    };

    global.Dts = global.Dts || {};
    global.Dts.Events = DtsEvents;

})(window);
