/**
 * BeamInit.js - Beam Viewer Initialization
 * FIX: Added subscription to double-click for rebar editing
 */
(function (global) {
    'use strict';

    const BeamInit = {
        init(data) {
            console.log('BeamInit: Starting initialization...');

            global.Dts?.UI?.init();

            const canvas = document.getElementById('beamCanvas');
            if (!canvas) {
                console.error('BeamInit: Canvas element not found');
                return;
            }

            global.Dts?.Renderer?.init(canvas);
            global.Dts?.Renderer?.resizeToContainer('canvasContainer');

            global.Dts?.Events?.init(canvas, () => {
                global.Beam?.Renderer?.render();
            });

            // FIX: Subscribe to Double Click event for Rebar Editing
            if (global.Dts?.State && global.Dts.State.on) {
                global.Dts.State.on('dblclick', (x, y) => {
                    // Check if a label was hit
                    const hit = global.Beam?.Renderer?.hitTestLabel(x, y);
                    if (hit) {
                        console.log('Double clicked label:', hit);
                        global.Beam?.Actions?.editRebar(hit.spanIndex, hit.position);
                    } else {
                        // Optional: Reset view if clicked on empty space
                        // global.Dts.State.resetView();
                        // global.Beam?.Renderer?.render();
                    }
                });
            }

            global.Beam?.State?.init(data);
            global.Beam?.Actions?.populateOptionDropdown();
            global.Beam?.Actions?.updateMetrics();
            global.Beam?.Actions?.updateLockStatus();
            global.Beam?.Renderer?.render();
            global.Beam?.Table?.render();

            window.addEventListener('resize', () => {
                global.Dts?.Renderer?.resizeToContainer('canvasContainer');
                global.Beam?.Renderer?.render();
            });

            global.Dts?.State?.subscribe((eventType) => {
                if (eventType === 'group' || eventType === 'option') {
                    global.Beam?.Actions?.populateOptionDropdown();
                    global.Beam?.Actions?.updateMetrics();
                    global.Beam?.Actions?.updateLockStatus();
                }
                if (eventType === 'group' || eventType === 'option' || eventType === 'highlight') {
                    global.Beam?.Renderer?.render();
                    global.Beam?.Table?.render();
                }
            });

            console.log('BeamInit: Initialization complete');
            global.Dts?.UI?.showToast('✓ Viewer đã sẵn sàng', 'success', 1500);
        }
    };

    global.Beam = global.Beam || {};
    global.Beam.Init = BeamInit;

})(window);
