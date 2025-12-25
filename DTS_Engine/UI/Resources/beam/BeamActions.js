/**
 * BeamActions.js - Beam User Actions
 * FIX: Added editRebar action
 */
(function (global) {
    'use strict';

    const BeamActions = {
        populateOptionDropdown() {
            const sel = document.getElementById('optionSelect');
            if (!sel) return;

            const beamState = global.Beam?.State;
            const group = beamState?.currentGroup;
            const options = [];

            if (group?.SelectedDesign) {
                const isInvalid = group.SelectedDesign.IsValid === false;
                const name = group.SelectedDesign.OptionName || 'Phương án đã chốt';
                options.push({
                    value: 'locked',
                    text: `⭐ ${name} ${isInvalid ? '⚠️ THIẾU THÉP' : '(Đang dùng)'}`,
                    selected: beamState.selectedOptionKey === 'locked'
                });
            }

            const backboneOpts = group?.BackboneOptions || [];
            if (backboneOpts.length === 0) {
                options.push({
                    value: 'none',
                    text: '⏳ Chưa tính (Nhấn "Tính thép")',
                    disabled: true
                });
            } else {
                backboneOpts.forEach((opt, i) => {
                    options.push({
                        value: String(i),
                        text: `${opt.OptionName || 'Option ' + (i + 1)} - D${opt.BackboneDiameter || '?'}${i === 0 ? ' ★Best' : ''}`,
                        selected: beamState?.selectedOptionKey === String(i)
                    });
                });
            }

            if (global.Dts?.UI?.populateDropdown) {
                global.Dts.UI.populateDropdown(sel, options);
            } else {
                sel.innerHTML = '';
                options.forEach(opt => {
                    const optEl = document.createElement('option');
                    optEl.value = opt.value;
                    optEl.textContent = opt.text;
                    if (opt.selected) optEl.selected = true;
                    if (opt.disabled) optEl.disabled = true;
                    sel.appendChild(optEl);
                });
            }
        },

        onOptionSelect(value) {
            global.Beam?.State?.selectOption(value);
            this.applyOptionToSpans();
            this.updateMetrics();
            global.Beam?.Renderer?.render();
            global.Beam?.Table?.render();
        },

        applyOptionToSpans() {
            const beamState = global.Beam?.State;
            const group = beamState?.currentGroup;
            const opt = beamState?.getSelectedOption();

            if (!group?.Spans?.length || !opt) return;

            const backboneTop = `${opt.BackboneCount_Top || 2}D${opt.BackboneDiameter || opt.BackboneDiameter_Top || 20}`;
            const backboneBot = `${opt.BackboneCount_Bot || 2}D${opt.BackboneDiameter || opt.BackboneDiameter_Bot || 20}`;

            group.Spans.forEach(span => {
                if (!span._userEdited && !span.IsManualModified) {
                    if (!span.TopRebar) span.TopRebar = [[], [], []];
                    if (!span.BotRebar) span.BotRebar = [[], [], []];

                    if (!span.TopRebar[0]) span.TopRebar[0] = [];
                    if (!span.BotRebar[0]) span.BotRebar[0] = [];

                    span.TopRebar[0][0] = backboneTop;
                    span.TopRebar[0][1] = backboneTop;
                    span.TopRebar[0][2] = backboneTop;
                    span.BotRebar[0][0] = backboneBot;
                    span.BotRebar[0][1] = backboneBot;
                    span.BotRebar[0][2] = backboneBot;

                    span.TopBackbone = { Count: opt.BackboneCount_Top, Diameter: opt.BackboneDiameter || opt.BackboneDiameter_Top };
                    span.BotBackbone = { Count: opt.BackboneCount_Bot, Diameter: opt.BackboneDiameter || opt.BackboneDiameter_Bot };
                }
            });
        },

        updateMetrics() {
            const opt = global.Beam?.State?.getSelectedOption();
            if (!opt) return;

            const setEl = (id, val) => {
                const el = document.getElementById(id);
                if (el) el.textContent = val;
            };

            const dia = opt.BackboneDiameter || opt.BackboneDiameter_Top || '?';
            setEl('metricBackbone', `${opt.BackboneCount_Top || 2}D${dia} / ${opt.BackboneCount_Bot || 2}D${dia}`);
            setEl('metricWeight', opt.TotalSteelWeight ? `${opt.TotalSteelWeight.toFixed(1)} kg` : '-');
            setEl('metricScore', opt.TotalScore ? `${opt.TotalScore.toFixed(0)}/100` : '-');
            setEl('metricWaste', opt.WastePercentage != null ? `${opt.WastePercentage.toFixed(1)}%` : '-');
        },

        lockDesign() {
            const beamState = global.Beam?.State;
            const group = beamState?.currentGroup;
            if (!group) return;

            const opt = beamState?.getSelectedOption();
            if (!opt) {
                this.showToast('Chưa có phương án để chốt', 'warning');
                return;
            }

            const lockedDesign = JSON.parse(JSON.stringify(opt));

            lockedDesign._capturedSpans = [];
            if (group.Spans) {
                group.Spans.forEach((span, idx) => {
                    lockedDesign._capturedSpans.push({
                        SpanId: span.SpanId,
                        SpanIndex: span.SpanIndex ?? idx,
                        TopRebar: span.TopRebar ? JSON.parse(JSON.stringify(span.TopRebar)) : null,
                        BotRebar: span.BotRebar ? JSON.parse(JSON.stringify(span.BotRebar)) : null,
                        Stirrup: span.Stirrup ? [...span.Stirrup] : null,
                        WebBar: span.WebBar ? [...span.WebBar] : null,
                        SideBar: span.SideBar,
                        TopBackbone: span.TopBackbone,
                        BotBackbone: span.BotBackbone,
                        TopAddLeft: span.TopAddLeft,
                        TopAddMid: span.TopAddMid,
                        TopAddRight: span.TopAddRight,
                        BotAddLeft: span.BotAddLeft,
                        BotAddMid: span.BotAddMid,
                        BotAddRight: span.BotAddRight,
                        _userEdited: span._userEdited || span.IsManualModified
                    });
                });
            }

            group.SelectedDesign = lockedDesign;
            group.LockedAt = new Date().toISOString();
            group.IsManuallyEdited = true;
            beamState.selectedOptionKey = 'locked';

            this.populateOptionDropdown();
            this.updateLockStatus();
            this.showToast('✓ Đã chốt phương án (bao gồm chỉnh sửa)', 'success');

            this.sendToHost('LOCK_DESIGN',
                `${beamState.currentGroupIndex}|${JSON.stringify(lockedDesign)}`);
        },

        unlockDesign() {
            const beamState = global.Beam?.State;
            const group = beamState?.currentGroup;
            if (!group) return;

            group.SelectedDesign = null;
            group.LockedAt = null;
            beamState.selectedOptionKey = group.BackboneOptions?.length > 0
                ? String(group.SelectedBackboneIndex || 0)
                : null;

            this.populateOptionDropdown();
            this.updateLockStatus();
            this.showToast('Đã mở khóa phương án', 'info');

            this.sendToHost('UNLOCK_DESIGN', { groupIndex: beamState.currentGroupIndex });
        },

        toggleLock() {
            const group = global.Beam?.State?.currentGroup;
            if (group?.SelectedDesign) {
                this.unlockDesign();
            } else {
                this.lockDesign();
            }
        },

        restoreLockedDesign() {
            const beamState = global.Beam?.State;
            const group = beamState?.currentGroup;
            if (!group?.SelectedDesign) return;

            const captured = group.SelectedDesign._capturedSpans;
            if (!captured || !Array.isArray(captured)) {
                this.showToast('Không có dữ liệu span để restore', 'warning');
                return;
            }

            captured.forEach(cs => {
                const span = group.Spans?.find(s => s.SpanId === cs.SpanId)
                    || group.Spans?.[cs.SpanIndex];
                if (!span) return;

                if (cs.TopRebar) span.TopRebar = JSON.parse(JSON.stringify(cs.TopRebar));
                if (cs.BotRebar) span.BotRebar = JSON.parse(JSON.stringify(cs.BotRebar));
                if (cs.Stirrup) span.Stirrup = [...cs.Stirrup];
                if (cs.WebBar) span.WebBar = [...cs.WebBar];
                span.SideBar = cs.SideBar;
                span.TopBackbone = cs.TopBackbone;
                span.BotBackbone = cs.BotBackbone;
                span.TopAddLeft = cs.TopAddLeft;
                span.TopAddMid = cs.TopAddMid;
                span.TopAddRight = cs.TopAddRight;
                span.BotAddLeft = cs.BotAddLeft;
                span.BotAddMid = cs.BotAddMid;
                span.BotAddRight = cs.BotAddRight;
            });

            global.Beam?.Renderer?.render();
            global.Beam?.Table?.render();
            this.showToast('✓ Đã restore dữ liệu từ phương án đã chốt', 'success');
        },

        updateLockStatus() {
            const group = global.Beam?.State?.currentGroup;
            const lockBtn = document.getElementById('lockBtn');
            const lockStatus = document.getElementById('lockStatus');

            if (group?.SelectedDesign) {
                if (lockBtn) {
                    lockBtn.innerHTML = '<i class="fa-solid fa-lock-open"></i> Mở';
                    lockBtn.className = 'px-2 py-1 bg-slate-600 hover:bg-slate-500 text-white rounded text-xs flex items-center gap-1';
                }
                lockStatus?.classList.remove('hidden');
            } else {
                if (lockBtn) {
                    lockBtn.innerHTML = '<i class="fa-solid fa-lock"></i> Chốt';
                    lockBtn.className = 'px-2 py-1 bg-amber-600 hover:bg-amber-500 text-white rounded text-xs flex items-center gap-1';
                }
                lockStatus?.classList.add('hidden');
            }
        },

        sendToHost(action, data) {
            if (window.chrome?.webview?.postMessage) {
                if (action === 'LOCK_DESIGN' && typeof data === 'string') {
                    window.chrome.webview.postMessage(`${action}|${data}`);
                } else {
                    window.chrome.webview.postMessage(`${action}|${JSON.stringify(data)}`);
                }
            }
        },

        // FIX: Add editRebar action to communicate with C#
        editRebar(spanIndex, position) {
            const beamState = global.Beam?.State;
            if (beamState) {
                // Format: EDIT_REBAR|{"GroupIndex":..., "SpanIndex":..., "Position":"top"/"bot"}
                const payload = {
                    GroupIndex: beamState.currentGroupIndex,
                    SpanIndex: spanIndex,
                    Position: position
                };
                this.sendToHost('EDIT_REBAR', payload);
                console.log('Sent EDIT_REBAR:', payload);
            }
        },

        save() {
            const beamState = global.Beam?.State;
            const data = {
                groups: beamState?.groups || []
            };
            this.sendToHost('SAVE', data);
            this.showToast('✓ Đã lưu', 'success');
        },

        showToast(message, type = 'info') {
            if (global.Dts?.UI?.showToast) {
                global.Dts.UI.showToast(message, type);
            } else {
                const existing = document.querySelector('.toast');
                if (existing) existing.remove();

                const toast = document.createElement('div');
                toast.className = 'toast fixed bottom-4 left-1/2 -translate-x-1/2 px-4 py-2 rounded shadow-lg text-white text-sm z-50';
                toast.style.background = type === 'success' ? '#22c55e' : type === 'warning' ? '#f59e0b' : '#3b82f6';
                toast.textContent = message;
                document.body.appendChild(toast);
                setTimeout(() => toast.remove(), 3000);
            }
        }
    };

    global.Beam = global.Beam || {};
    global.Beam.Actions = BeamActions;

})(window);
