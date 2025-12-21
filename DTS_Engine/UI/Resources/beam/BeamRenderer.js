/**
 * BeamRenderer.js - Beam Canvas Rendering
 * Draws beam spans, supports, rebar, and dimensions.
 * Supports N spans and N layers dynamically.
 */
(function (global) {
    'use strict';

    const BeamRenderer = {
        // ===== CONFIGURATION (from settings) =====
        config: {
            BEAM_HEIGHT: 80,
            MIN_SPAN_WIDTH: 80,
            MAX_CANVAS_WIDTH: 1600,
            CANVAS_PADDING: 30,
            SUPPORT_GAP: 15,
            LAYER_OFFSET: 4,  // Distance between rebar layers (px)
            BAR_THICKNESS: 2  // Base thickness for rebar lines
        },

        // ===== COLORS =====
        colors: {
            beamFill: '#e2e8f0',
            beamStroke: '#64748b',
            highlightFill: '#dbeafe',
            highlightStroke: '#3b82f6',
            rebarTop: '#dc2626',
            rebarBot: '#2563eb',
            rebarTopLight: '#fca5a5',  // Lighter for addon
            rebarBotLight: '#93c5fd',  // Lighter for addon
            dimension: '#64748b',
            label: '#1e293b',
            supportFill: '#475569'
        },

        /**
         * Main render function
         */
        render() {
            const canvas = document.getElementById('beamCanvas');
            const ctx = canvas?.getContext('2d');
            if (!ctx) return;

            const beamState = global.Beam?.State;
            const group = beamState?.currentGroup;

            // No data - show message
            if (!group?.Spans?.length) {
                this._drawNoData(ctx, canvas);
                return;
            }

            // Calculate layout for N spans
            const spans = group.Spans;
            const layout = this._calculateLayout(spans, canvas.width);

            // Clear and setup
            global.Dts?.Renderer?.clear();
            global.Dts?.Renderer?.beginTransform();

            // Draw all spans
            beamState.spanBounds = [];
            this._drawAllSpans(ctx, spans, layout, beamState);

            // End transform
            global.Dts?.Renderer?.endTransform();

            // Draw overlays
            global.Dts?.Renderer?.drawBoxZoomOverlay();
            global.Dts?.Renderer?.updateZoomIndicator();
        },

        /**
         * Calculate layout for N spans
         */
        _calculateLayout(spans, canvasWidth) {
            const numSpans = spans.length;
            const lengths = spans.map(s => s.Length || 1);
            const totalLength = lengths.reduce((a, b) => a + b, 0);

            // Calculate available width
            const availableWidth = Math.min(
                this.config.MAX_CANVAS_WIDTH,
                canvasWidth
            ) - this.config.CANVAS_PADDING * 2 - numSpans * this.config.SUPPORT_GAP;

            // Proportional widths
            const widths = lengths.map(len => {
                const ratio = len / totalLength;
                const width = availableWidth * ratio;
                return Math.max(this.config.MIN_SPAN_WIDTH, width);
            });

            return {
                widths,
                startX: this.config.CANVAS_PADDING,
                beamY: 60
            };
        },

        /**
         * Draw all spans with supports
         */
        _drawAllSpans(ctx, spans, layout, beamState) {
            let x = layout.startX;
            const beamY = layout.beamY;

            spans.forEach((span, i) => {
                const w = layout.widths[i];

                // Left support (first span only)
                if (i === 0) {
                    this._drawSupport(ctx, x, beamY);
                    x += 5;
                }

                // Store bounds for hit testing
                beamState.spanBounds.push({
                    x, y: beamY,
                    width: w, height: this.config.BEAM_HEIGHT,
                    index: i
                });

                // Draw span
                const isHighlighted = i === beamState.highlightedSpanIndex;
                this._drawSpan(ctx, x, beamY, w, span, isHighlighted);

                // Draw rebar (N layers)
                this._drawRebarNLayers(ctx, x, beamY, w, span);

                // Labels and dimensions
                this._drawLabels(ctx, x, beamY, w, span);

                x += w;

                // Support after each span
                this._drawSupport(ctx, x, beamY);
                x += this.config.SUPPORT_GAP;
            });
        },

        /**
         * Draw "no data" message
         */
        _drawNoData(ctx, canvas) {
            canvas.width = 400;
            ctx.fillStyle = '#f8fafc';
            ctx.fillRect(0, 0, 400, 180);
            ctx.fillStyle = '#94a3b8';
            ctx.font = '14px sans-serif';
            ctx.textAlign = 'center';
            ctx.fillText('Không có dữ liệu nhịp', 200, 90);
        },

        /**
         * Draw beam span rectangle
         */
        _drawSpan(ctx, x, y, w, span, isHighlighted) {
            ctx.fillStyle = isHighlighted ? this.colors.highlightFill : this.colors.beamFill;
            ctx.strokeStyle = isHighlighted ? this.colors.highlightStroke : this.colors.beamStroke;
            ctx.lineWidth = isHighlighted ? 2 : 1;
            ctx.fillRect(x, y, w, this.config.BEAM_HEIGHT);
            ctx.strokeRect(x, y, w, this.config.BEAM_HEIGHT);
        },

        /**
         * Draw support symbol (triangle)
         */
        _drawSupport(ctx, x, y) {
            const h = 15;
            ctx.beginPath();
            ctx.moveTo(x, y + this.config.BEAM_HEIGHT);
            ctx.lineTo(x - 6, y + this.config.BEAM_HEIGHT + h);
            ctx.lineTo(x + 6, y + this.config.BEAM_HEIGHT + h);
            ctx.closePath();
            ctx.fillStyle = this.colors.supportFill;
            ctx.fill();
        },

        /**
         * Draw rebar lines with N-layer support
         * Backbone runs full length, Addons are position-specific
         */
        _drawRebarNLayers(ctx, x, y, w, span) {
            const topY = y + 6;
            const botY = y + this.config.BEAM_HEIGHT - 6;

            // === TOP REBAR ===
            // 1. Backbone (full length)
            if (span.TopBackbone) {
                this._drawRebarLine(ctx, x, topY, w, span.TopBackbone, 'top', 0, true);
            }

            // 2. Addons (position-specific)
            const baseLayerTop = span.TopBackbone ? this._getLayerCount(span.TopBackbone) : 0;
            
            if (span.TopAddLeft) {
                // Left addon: 0 to 0.25L
                this._drawRebarLine(ctx, x, topY, w * 0.25, span.TopAddLeft, 'top', baseLayerTop, false);
            }
            if (span.TopAddMid) {
                // Mid addon: 0.25L to 0.75L
                this._drawRebarLine(ctx, x + w * 0.25, topY, w * 0.5, span.TopAddMid, 'top', baseLayerTop, false);
            }
            if (span.TopAddRight) {
                // Right addon: 0.75L to L
                this._drawRebarLine(ctx, x + w * 0.75, topY, w * 0.25, span.TopAddRight, 'top', baseLayerTop, false);
            }

            // === BOT REBAR ===
            // 1. Backbone (full length)
            if (span.BotBackbone) {
                this._drawRebarLine(ctx, x, botY, w, span.BotBackbone, 'bot', 0, true);
            }

            // 2. Addons
            const baseLayerBot = span.BotBackbone ? this._getLayerCount(span.BotBackbone) : 0;

            if (span.BotAddLeft) {
                this._drawRebarLine(ctx, x, botY, w * 0.25, span.BotAddLeft, 'bot', baseLayerBot, false);
            }
            if (span.BotAddMid) {
                // Mid addon: 0.15L to 0.85L (longer for bottom)
                this._drawRebarLine(ctx, x + w * 0.15, botY, w * 0.7, span.BotAddMid, 'bot', baseLayerBot, false);
            }
            if (span.BotAddRight) {
                this._drawRebarLine(ctx, x + w * 0.75, botY, w * 0.25, span.BotAddRight, 'bot', baseLayerBot, false);
            }
        },

        /**
         * Draw a single rebar line (handles N layers)
         */
        _drawRebarLine(ctx, startX, startY, length, info, pos, startLayer, isBackbone) {
            if (!info || !info.Count || info.Count <= 0) return;

            const isTop = pos === 'top';
            const color = isBackbone
                ? (isTop ? this.colors.rebarTop : this.colors.rebarBot)
                : (isTop ? this.colors.rebarTopLight : this.colors.rebarBotLight);
            
            ctx.strokeStyle = color;
            ctx.lineWidth = isBackbone ? this.config.BAR_THICKNESS : this.config.BAR_THICKNESS - 0.5;

            // Get layer breakdown
            const layers = info.LayerCounts || [info.Count];

            layers.forEach((count, idx) => {
                if (count <= 0) return;
                
                const layerIdx = startLayer + idx;
                // Top goes down (+), Bot goes up (-)
                const offset = layerIdx * this.config.LAYER_OFFSET * (isTop ? 1 : -1);

                ctx.beginPath();
                ctx.moveTo(startX, startY + offset);
                ctx.lineTo(startX + length, startY + offset);
                ctx.stroke();
            });
        },

        /**
         * Get number of layers from RebarInfo
         */
        _getLayerCount(info) {
            if (!info) return 0;
            if (info.LayerCounts && info.LayerCounts.length > 0) {
                return info.LayerCounts.length;
            }
            return 1;
        },

        /**
         * Draw span labels and dimensions
         */
        _drawLabels(ctx, x, y, w, span) {
            // Span ID (centered)
            ctx.fillStyle = this.colors.label;
            ctx.font = 'bold 11px sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(span.SpanId || '', x + w / 2, y + this.config.BEAM_HEIGHT / 2);

            // Rebar Labels
            ctx.font = '10px sans-serif';

            // Top Label (summarize all top rebar)
            const topLabel = this._getSummaryLabel(span.TopBackbone, span.TopAddLeft, span.TopAddMid, span.TopAddRight);
            ctx.fillStyle = this.colors.rebarTop;
            ctx.fillText(topLabel, x + w / 2, y - 12);

            // Bot Label (summarize all bot rebar)
            const botLabel = this._getSummaryLabel(span.BotBackbone, span.BotAddMid, span.BotAddLeft, span.BotAddRight);
            ctx.fillStyle = this.colors.rebarBot;
            ctx.fillText(botLabel, x + w / 2, y + this.config.BEAM_HEIGHT + 24);

            // Length dimension
            ctx.fillStyle = this.colors.dimension;
            const lengthText = `${(span.Length || 0).toFixed(2)}m`;
            ctx.fillText(lengthText, x + w / 2, y + this.config.BEAM_HEIGHT + 12);

            // Section size
            const sectionText = `${span.Width || 0}×${span.Height || 0}`;
            ctx.fillText(sectionText, x + w / 2, y - 2);
        },

        /**
         * Get summary label for rebar (Backbone + max Addon)
         */
        _getSummaryLabel(backbone, ...addons) {
            if (!backbone || !backbone.Count) return '';

            // Find max addon
            let maxAddon = null;
            let maxCount = 0;
            addons.forEach(a => {
                if (a && a.Count > maxCount) {
                    maxCount = a.Count;
                    maxAddon = a;
                }
            });

            const backboneStr = backbone.DisplayString || `${backbone.Count}D${backbone.Diameter}`;

            if (!maxAddon) return backboneStr;

            const addonStr = maxAddon.DisplayString || `${maxAddon.Count}D${maxAddon.Diameter}`;

            // Show as "Backbone + Addon" to distinguish continuous vs additional
            return `${backboneStr} + ${addonStr}`;
        },

        /**
         * Update configuration from settings
         */
        updateConfig(settings) {
            if (!settings) return;
            // Can be extended to read settings from DtsSettings
        }
    };

    // Export to global namespace
    global.Beam = global.Beam || {};
    global.Beam.Renderer = BeamRenderer;

})(window);
