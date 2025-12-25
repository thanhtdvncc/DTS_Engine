/**
 * BeamRenderer.js - Beam Canvas Rendering
 * Updated to support Label Hit Testing for Rebar Editing
 */
(function (global) {
    'use strict';

    const BeamRenderer = {
        config: {
            BEAM_HEIGHT: 80,
            MIN_SPAN_WIDTH: 80,
            MAX_CANVAS_WIDTH: 1600,
            CANVAS_PADDING: 30,
            SUPPORT_GAP: 15,
            LAYER_OFFSET: 4,
            BAR_THICKNESS: 2
        },

        colors: {
            beamFill: '#e2e8f0',
            beamStroke: '#64748b',
            highlightFill: '#dbeafe',
            highlightStroke: '#3b82f6',
            rebarTop: '#dc2626',
            rebarBot: '#2563eb',
            rebarTopLight: '#fca5a5',
            rebarBotLight: '#93c5fd',
            dimension: '#64748b',
            label: '#1e293b',
            supportFill: '#475569'
        },

        render() {
            const canvas = document.getElementById('beamCanvas');
            const ctx = canvas?.getContext('2d');
            if (!ctx) return;

            const beamState = global.Beam?.State;
            const group = beamState?.currentGroup;

            if (!group?.Spans?.length) {
                this._drawNoData(ctx, canvas);
                return;
            }

            const spans = group.Spans;
            const layout = this._calculateLayout(spans, canvas.width);

            global.Dts?.Renderer?.clear();
            global.Dts?.Renderer?.beginTransform();

            // Reset hit arrays
            beamState.spanBounds = [];
            beamState.labelHits = []; // FIX: Initialize label hits array

            this._drawAllSpans(ctx, spans, layout, beamState);

            global.Dts?.Renderer?.endTransform();
            global.Dts?.Renderer?.drawBoxZoomOverlay();
            global.Dts?.Renderer?.updateZoomIndicator();
        },

        _calculateLayout(spans, canvasWidth) {
            const numSpans = spans.length;
            const lengths = spans.map(s => s.Length || 1);
            const totalLength = lengths.reduce((a, b) => a + b, 0);

            const availableWidth = Math.min(
                this.config.MAX_CANVAS_WIDTH,
                canvasWidth
            ) - this.config.CANVAS_PADDING * 2 - numSpans * this.config.SUPPORT_GAP;

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

        _drawAllSpans(ctx, spans, layout, beamState) {
            let x = layout.startX;
            const beamY = layout.beamY;

            spans.forEach((span, i) => {
                const w = layout.widths[i];

                if (i === 0) {
                    this._drawSupport(ctx, x, beamY);
                    x += 5;
                }

                beamState.spanBounds.push({
                    x, y: beamY,
                    width: w, height: this.config.BEAM_HEIGHT,
                    index: i
                });

                const isHighlighted = i === beamState.highlightedSpanIndex;
                this._drawSpan(ctx, x, beamY, w, span, isHighlighted);
                this._drawRebarNLayers(ctx, x, beamY, w, span);

                // Pass beamState to store label hits
                this._drawLabels(ctx, x, beamY, w, span, beamState, i);

                x += w;
                this._drawSupport(ctx, x, beamY);
                x += this.config.SUPPORT_GAP;
            });
        },

        _drawNoData(ctx, canvas) {
            canvas.width = 400;
            ctx.fillStyle = '#f8fafc';
            ctx.fillRect(0, 0, 400, 180);
            ctx.fillStyle = '#94a3b8';
            ctx.font = '14px sans-serif';
            ctx.textAlign = 'center';
            ctx.fillText('Không có dữ liệu nhịp', 200, 90);
        },

        _drawSpan(ctx, x, y, w, span, isHighlighted) {
            ctx.fillStyle = isHighlighted ? this.colors.highlightFill : this.colors.beamFill;
            ctx.strokeStyle = isHighlighted ? this.colors.highlightStroke : this.colors.beamStroke;
            ctx.lineWidth = isHighlighted ? 2 : 1;
            ctx.fillRect(x, y, w, this.config.BEAM_HEIGHT);
            ctx.strokeRect(x, y, w, this.config.BEAM_HEIGHT);
        },

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

        _drawRebarNLayers(ctx, x, y, w, span) {
            const topY = y + 6;
            const botY = y + this.config.BEAM_HEIGHT - 6;

            if (span.TopBackbone) this._drawRebarLine(ctx, x, topY, w, span.TopBackbone, 'top', 0, true);

            const baseLayerTop = span.TopBackbone ? this._getLayerCount(span.TopBackbone) : 0;
            if (span.TopAddLeft) this._drawRebarLine(ctx, x, topY, w * 0.25, span.TopAddLeft, 'top', baseLayerTop, false);
            if (span.TopAddMid) this._drawRebarLine(ctx, x + w * 0.25, topY, w * 0.5, span.TopAddMid, 'top', baseLayerTop, false);
            if (span.TopAddRight) this._drawRebarLine(ctx, x + w * 0.75, topY, w * 0.25, span.TopAddRight, 'top', baseLayerTop, false);

            if (span.BotBackbone) this._drawRebarLine(ctx, x, botY, w, span.BotBackbone, 'bot', 0, true);

            const baseLayerBot = span.BotBackbone ? this._getLayerCount(span.BotBackbone) : 0;
            if (span.BotAddLeft) this._drawRebarLine(ctx, x, botY, w * 0.25, span.BotAddLeft, 'bot', baseLayerBot, false);
            if (span.BotAddMid) this._drawRebarLine(ctx, x + w * 0.15, botY, w * 0.7, span.BotAddMid, 'bot', baseLayerBot, false);
            if (span.BotAddRight) this._drawRebarLine(ctx, x + w * 0.75, botY, w * 0.25, span.BotAddRight, 'bot', baseLayerBot, false);
        },

        _drawRebarLine(ctx, startX, startY, length, info, pos, startLayer, isBackbone) {
            if (!info || !info.Count || info.Count <= 0) return;

            const isTop = pos === 'top';
            const color = isBackbone
                ? (isTop ? this.colors.rebarTop : this.colors.rebarBot)
                : (isTop ? this.colors.rebarTopLight : this.colors.rebarBotLight);

            ctx.strokeStyle = color;
            ctx.lineWidth = isBackbone ? this.config.BAR_THICKNESS : this.config.BAR_THICKNESS - 0.5;

            const layers = info.LayerCounts || [info.Count];

            layers.forEach((count, idx) => {
                if (count <= 0) return;
                const layerIdx = startLayer + idx;
                const offset = layerIdx * this.config.LAYER_OFFSET * (isTop ? 1 : -1);

                ctx.beginPath();
                ctx.moveTo(startX, startY + offset);
                ctx.lineTo(startX + length, startY + offset);
                ctx.stroke();
            });
        },

        _getLayerCount(info) {
            if (!info) return 0;
            if (info.LayerCounts && info.LayerCounts.length > 0) return info.LayerCounts.length;
            return 1;
        },

        _drawLabels(ctx, x, y, w, span, beamState, spanIndex) {
            ctx.font = 'bold 11px sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillStyle = this.colors.label;
            ctx.fillText(span.SpanId || '', x + w / 2, y + this.config.BEAM_HEIGHT / 2);

            ctx.font = '10px sans-serif';

            // === TOP LABEL & HIT BOX ===
            const topLabel = this._getSummaryLabel(span.TopBackbone, span.TopAddLeft, span.TopAddMid, span.TopAddRight);
            ctx.fillStyle = this.colors.rebarTop;
            const topY = y - 12;
            ctx.fillText(topLabel, x + w / 2, topY);

            // Register Top Hit (approximate text metrics)
            const topMetrics = ctx.measureText(topLabel);
            if (beamState && beamState.labelHits) {
                beamState.labelHits.push({
                    x: x + w / 2 - topMetrics.width / 2,
                    y: topY - 6,
                    w: topMetrics.width,
                    h: 12,
                    spanIndex: spanIndex,
                    position: 'top'
                });
            }

            // === BOT LABEL & HIT BOX ===
            const botLabel = this._getSummaryLabel(span.BotBackbone, span.BotAddMid, span.BotAddLeft, span.BotAddRight);
            ctx.fillStyle = this.colors.rebarBot;
            const botY = y + this.config.BEAM_HEIGHT + 24;
            ctx.fillText(botLabel, x + w / 2, botY);

            // Register Bot Hit
            const botMetrics = ctx.measureText(botLabel);
            if (beamState && beamState.labelHits) {
                beamState.labelHits.push({
                    x: x + w / 2 - botMetrics.width / 2,
                    y: botY - 6,
                    w: botMetrics.width,
                    h: 12,
                    spanIndex: spanIndex,
                    position: 'bot'
                });
            }

            ctx.fillStyle = this.colors.dimension;
            const lengthText = `${(span.Length || 0).toFixed(2)}m`;
            ctx.fillText(lengthText, x + w / 2, y + this.config.BEAM_HEIGHT + 12);

            const sectionText = `${span.Width || 0}×${span.Height || 0}`;
            ctx.fillText(sectionText, x + w / 2, y - 2);
        },

        _getSummaryLabel(backbone, ...addons) {
            if (!backbone || !backbone.Count) return '';
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
            return `${backboneStr} + ${addonStr}`;
        },

        /**
         * FIX: Hit test function for labels
         */
        hitTestLabel(x, y) {
            const beamState = global.Beam?.State;
            if (!beamState || !beamState.labelHits) return null;

            // Simple point-in-rect check with some padding
            const padding = 5;
            for (const hit of beamState.labelHits) {
                if (x >= hit.x - padding && x <= hit.x + hit.w + padding &&
                    y >= hit.y - padding && y <= hit.y + hit.h + padding) {
                    return hit;
                }
            }
            return null;
        }
    };

    global.Beam = global.Beam || {};
    global.Beam.Renderer = BeamRenderer;

})(window);
