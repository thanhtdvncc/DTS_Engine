/**
 * BeamTable.js - Beam Data Table Rendering
 * Renders editable table of span rebar data.
 * Supports N spans dynamically with RebarInfo structured data.
 */
(function (global) {
    'use strict';

    const BeamTable = {
        /**
         * Render the span data table for N spans
         */
        render() {
            const tbody = document.getElementById('spanTableBody');
            if (!tbody) return;

            const beamState = global.Beam?.State;
            const spans = beamState?.currentGroup?.Spans || [];

            if (spans.length === 0) {
                tbody.innerHTML = '<tr><td colspan="8" class="text-center text-slate-400 py-4">Không có dữ liệu</td></tr>';
                return;
            }

            // Render all N spans
            tbody.innerHTML = spans.map((span, i) => this._renderRow(span, i)).join('');
        },

        /**
         * Render a single table row for a span
         */
        _renderRow(span, index) {
            const isHighlighted = index === global.Beam?.State?.highlightedSpanIndex;
            const rowClass = isHighlighted ? 'bg-blue-50' : (index % 2 ? 'bg-slate-50' : '');
            const manualClass = span.IsManualModified ? 'border-l-4 border-yellow-400' : '';

            // Get rebar strings using structured RebarInfo
            const topRebar = this._getTopRebarLabel(span);
            const botRebar = this._getBotRebarLabel(span);
            const stirrup = this._getStirrupLabel(span);

            return `
                <tr class="${rowClass} ${manualClass} hover:bg-blue-100 cursor-pointer" 
                    data-span-index="${index}"
                    onclick="Beam.Table.onRowClick(${index})"
                    ondblclick="Beam.Table.showReport(${index})">
                    <td class="px-2 py-1 text-center font-bold">${span.SpanId || `S${index + 1}`}</td>
                    <td class="px-2 py-1 text-center">${(span.Length || 0).toFixed(2)}m</td>
                    <td class="px-2 py-1 text-center">${span.Width || 0}×${span.Height || 0}</td>
                    <td class="px-2 py-1 text-red-600">${topRebar}</td>
                    <td class="px-2 py-1 text-blue-600">${botRebar}</td>
                    <td class="px-2 py-1 text-center text-sm">${stirrup}</td>
                    <td class="px-2 py-1 text-center">${span.SideBar || '-'}</td>
                    <td class="px-2 py-1 text-center">
                        <button class="text-blue-500 hover:text-blue-700 px-1" 
                                onclick="event.stopPropagation(); Beam.Table.editSpan(${index})"
                                title="Chỉnh sửa">
                            <i class="fa-solid fa-edit"></i>
                        </button>
                    </td>
                </tr>
            `;
        },

        /**
         * Get TOP rebar label from structured data or legacy
         */
        _getTopRebarLabel(span) {
            // Use structured RebarInfo if available
            if (span.TopBackbone) {
                return this._getMergedLabel(span.TopBackbone, span.TopAddLeft, span.TopAddMid, span.TopAddRight);
            }

            // Legacy: Use TopRebar array
            return this._getLegacyRebarString(span.TopRebar) || '-';
        },

        /**
         * Get BOT rebar label from structured data or legacy
         */
        _getBotRebarLabel(span) {
            // Use structured RebarInfo if available
            if (span.BotBackbone) {
                return this._getMergedLabel(span.BotBackbone, span.BotAddLeft, span.BotAddMid, span.BotAddRight);
            }

            // Legacy: Use BotRebar array
            return this._getLegacyRebarString(span.BotRebar) || '-';
        },

        /**
         * Get stirrup label (position 1 = Mid span is governing typically)
         */
        _getStirrupLabel(span) {
            if (!span.Stirrup) return '-';

            // Show governing (usually middle or max)
            const stirrups = span.Stirrup;
            if (Array.isArray(stirrups)) {
                // Find non-empty stirrup
                for (let i = 0; i < stirrups.length; i++) {
                    if (stirrups[i] && stirrups[i] !== '-') {
                        return stirrups[i];
                    }
                }
            }
            return '-';
        },

        /**
         * Merge backbone + addons into display label
         */
        _getMergedLabel(backbone, ...addons) {
            if (!backbone || !backbone.Count) return '-';

            const backboneStr = backbone.DisplayString || `${backbone.Count}D${backbone.Diameter}`;

            // Find max addon (by count)
            let maxAddon = null;
            let maxCount = 0;
            addons.forEach(a => {
                if (a && a.Count > maxCount) {
                    maxCount = a.Count;
                    maxAddon = a;
                }
            });

            if (!maxAddon) return backboneStr;

            const addonStr = maxAddon.DisplayString || `${maxAddon.Count}D${maxAddon.Diameter}`;

            // Show as "Backbone + Addon" to clearly distinguish
            return `${backboneStr}+${addonStr}`;
        },

        /**
         * Legacy support for string arrays [layer][position]
         */
        _getLegacyRebarString(rebarArray) {
            if (!rebarArray || !Array.isArray(rebarArray)) return null;

            const parts = [];
            for (let layer = 0; layer < rebarArray.length; layer++) {
                const layerData = rebarArray[layer];
                if (Array.isArray(layerData)) {
                    // Get first non-empty value
                    for (let pos = 0; pos < layerData.length; pos++) {
                        const val = layerData[pos];
                        if (val && typeof val === 'string' && val !== '-') {
                            parts.push(val);
                            break;
                        }
                    }
                }
            }

            return parts.length > 0 ? parts.join(' + ') : null;
        },

        /**
         * Handle row click - highlight span
         */
        onRowClick(index) {
            global.Beam?.State?.highlightSpan(index);
            global.Beam?.Renderer?.render();
            this.render();
        },

        showReport(index) {
            global.Beam?.Actions?.showCalculationReport(index);
        },

        /**
         * Edit span (open modal or inline edit)
         */
        editSpan(index) {
            const beamState = global.Beam?.State;
            const span = beamState?.currentGroup?.Spans?.[index];
            if (!span) return;

            // Highlight the span
            beamState.highlightSpan(index);
            global.Beam?.Renderer?.render();
            this.render();

            // Show edit notification
            showToast?.(`Đang chỉnh sửa ${span.SpanId}`, 'info');

            // TODO: Open edit modal for detailed editing
            // For now, mark as manually modified if user edits
        },

        /**
         * Scroll table to highlighted row
         */
        scrollToHighlighted() {
            const index = global.Beam?.State?.highlightedSpanIndex;
            if (index < 0) return;

            const row = document.querySelector(`[data-span-index="${index}"]`);
            if (row) {
                row.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        },

        /**
         * Get summary statistics for all spans
         */
        getSummary() {
            const spans = global.Beam?.State?.currentGroup?.Spans || [];

            return {
                spanCount: spans.length,
                totalLength: spans.reduce((sum, s) => sum + (s.Length || 0), 0),
                hasManualEdits: spans.some(s => s.IsManualModified)
            };
        }
    };

    // Export to global namespace
    global.Beam = global.Beam || {};
    global.Beam.Table = BeamTable;

})(window);
