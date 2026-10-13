/* ═══════════════════════════════════════════════════════════════════════
   ACA360 Dashboard — UX Enhancement Pack (v2)
   ═══════════════════════════════════════════════════════════════════════
   FEATURE 1: Enhanced Keyboard Shortcuts with section navigation
   FEATURE 2: Auto-load primary service on page load
   FEATURE 3: Unsaved changes guard
   ═══════════════════════════════════════════════════════════════════════
   Replaces the previous dashboard-keyboard-shortcuts.js entirely.
   
   Add to Dashboard.cshtml @section Scripts:
   <script src="~/js/tracker_dashboard-ux-pack.js"></script>
   ═══════════════════════════════════════════════════════════════════════ */

(function () {
    'use strict';

    /* ══════════════════════════════════════════════════════════════════
       FEATURE 1: ENHANCED KEYBOARD SHORTCUTS
    ══════════════════════════════════════════════════════════════════ */

    var TAB_MAP = {
        '1': 'pane-empdetail',
        '2': 'pane-affiliates',
        '3': 'pane-broker',
        '4': 'pane-other'
    };

    var TAB_NAMES = {
        '1': 'Employer details',
        '2': 'Affiliates',
        '3': 'Broker',
        '4': 'Other details'
    };

    // CHANGED: use document.body.classList instead of #editBtns
    function isEditMode() {
        return document.body.classList.contains('ws-edit');
    }

    function isTyping() {
        var el = document.activeElement || {};
        var tag = el.tagName || '';
        return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable;
    }

    $(document).on('keydown', function (e) {

        // ── Ctrl+S / Cmd+S → Save ──
        if ((e.ctrlKey || e.metaKey) && e.key === 's') {
            e.preventDefault();

            var noteModal = document.getElementById('addNoteModal');
            if (noteModal && noteModal.classList.contains('show')) {
                if (typeof saveNote === 'function') {
                    saveNote();
                    showToast('Saving note…', 'bx-save');
                }
                return;
            }

            // CHANGED: no btnSvcUpdate — check svc-dash-sel is unlocked instead
            var $svcSel = $('#activeSvcSel, #svcStatusSel');
            var svcUnlocked = $svcSel.length && !$svcSel.first().hasClass('svc-dash-locked');
            if (svcUnlocked && typeof saveSvcPanel === 'function') {
                saveSvcPanel();
                showToast('Saving service…', 'bx-save');
                return;
            }

            if (isEditMode() && typeof saveDashboard === 'function') {
                saveDashboard();
                showToast('Saving…', 'bx-save');
            }
            return;
        }

        // ── Esc → Cancel / close ──
        if (e.key === 'Escape') {
            if ($('#kbShortcutPanel').length) {
                $('#kbShortcutPanel').remove();
                return;
            }
            var openModal = document.querySelector('.modal.show');
            if (openModal) {
                var inst = bootstrap.Modal.getInstance(openModal);
                if (inst) inst.hide();
                return;
            }
            if (isEditMode() && typeof cancelEdit === 'function') {
                e.preventDefault();
                cancelEdit();
                showToast('Edit cancelled', 'bx-x');
            }
            return;
        }

        if (isTyping()) return;

        // ── E → Enter edit mode (double-click first unlocked field) ──
        if (e.key === 'e' || e.key === 'E') {
            if (!isEditMode() && typeof startEdit === 'function') {
                e.preventDefault();
                startEdit();
                showToast('Edit mode', 'bx-pencil');
            }
            return;
        }

        // ── N → New note ──
        if (e.key === 'n' || e.key === 'N') {
            e.preventDefault();
            if (typeof openAddNote === 'function') openAddNote();
            showToast('New note', 'bx-note');
            return;
        }

        // ── 1-4 → Switch employer tabs ──
        if (TAB_MAP[e.key]) {
            e.preventDefault();
            var targetPane = TAB_MAP[e.key];
            var $tabBtn = $('[data-tab="' + targetPane + '"]');
            if ($tabBtn.length) {
                $tabBtn.trigger('click');
                showToast(TAB_NAMES[e.key], 'bx-columns');
                var $pane = $('#' + targetPane);
                if ($pane.length) $pane[0].scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }
            return;
        }

        // ── G → Focus bottom service grid ──
        if (e.key === 'g' || e.key === 'G') {
            e.preventDefault();
            var $grid = $('#svcTableBody');
            if ($grid.length) {
                $grid.closest('.card, .col-12')[0]?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                var $activeRow = $grid.find('tr.svc-active');
                if (!$activeRow.length) {
                    var $firstRow = $grid.find('tr:first');
                    if ($firstRow.length) $firstRow.trigger('click');
                }
                showToast('Service grid', 'bx-grid-alt');
            }
            return;
        }

        // ── D → Focus service detail panel ──
        if (e.key === 'd' || e.key === 'D') {
            e.preventDefault();
            var $detail = $('.svc-right-card');
            if ($detail.length) {
                $detail[0].scrollIntoView({ behavior: 'smooth', block: 'start' });
                $('#activeSvcSel').focus();
                showToast('Service detail', 'bx-detail');
            }
            return;
        }

        // ── ? → Shortcut cheatsheet ──
        if (e.key === '?') {
            e.preventDefault();
            toggleShortcutPanel();
            return;
        }
    });

    /* ── Toast ── */
    function showToast(message, icon) {
        $('.kb-toast').remove();
        var $t = $('<div class="kb-toast"><i class="bx ' + icon + '"></i> ' + message + '</div>');
        $('body').append($t);
        setTimeout(function () { $t.addClass('kb-toast-show'); }, 10);
        setTimeout(function () {
            $t.removeClass('kb-toast-show');
            setTimeout(function () { $t.remove(); }, 200);
        }, 1200);
    }

    /* ── Shortcut cheatsheet ── */
    function toggleShortcutPanel() {
        if ($('#kbShortcutPanel').length) { $('#kbShortcutPanel').remove(); return; }

        var shortcuts = [
            ['E', 'Enter edit mode'],
            ['Esc', 'Cancel / close modal'],
            ['Ctrl+S', 'Save changes'],
            ['N', 'New note'],
            ['1–4', 'Employer tabs'],
            ['G', 'Go to service grid'],
            ['D', 'Go to service detail'],
            ['?', 'This panel']
        ];

        var rows = '';
        for (var i = 0; i < shortcuts.length; i++) {
            rows += '<div class="kb-row"><kbd>' + shortcuts[i][0] + '</kbd><span>' + shortcuts[i][1] + '</span></div>';
        }

        var html =
            '<div id="kbShortcutPanel" class="kb-panel">' +
            '  <div class="kb-panel-head"><span>Keyboard shortcuts</span>' +
            '    <button onclick="$(\'#kbShortcutPanel\').remove()" class="kb-panel-close">&times;</button>' +
            '  </div>' +
            '  <div class="kb-panel-body">' + rows + '</div>' +
            '</div>';

        $('body').append(html);
        setTimeout(function () {
            $(document).one('click', function (ev) {
                if (!$(ev.target).closest('#kbShortcutPanel').length) {
                    $('#kbShortcutPanel').remove();
                }
            });
        }, 100);
    }


    /* ══════════════════════════════════════════════════════════════════
       FEATURE 2: AUTO-LOAD PRIMARY SERVICE
    ══════════════════════════════════════════════════════════════════ */

    function autoLoadPrimaryService() {
        if (typeof svcData === 'undefined' || !svcData || svcData.length === 0) return;
        if ($('#svcTableBody tr.svc-active').length > 0) return;

        var priorityOrder = ['sold', 'opportunity'];
        var primaryIdx = -1;

        for (var p = 0; p < priorityOrder.length; p++) {
            for (var i = 0; i < svcData.length; i++) {
                var st = (svcData[i].status || '').toLowerCase();
                if (st === priorityOrder[p]) { primaryIdx = i; break; }
            }
            if (primaryIdx >= 0) break;
        }

        if (primaryIdx < 0) primaryIdx = 0;

        var $sel = $('#activeSvcSel');
        if ($sel.length) $sel.val(primaryIdx);

        var $row = $('#gRow' + primaryIdx);
        if ($row.length) {
            $('#svcTableBody tr').removeClass('svc-active');
            $row.addClass('svc-active');
        }

        if (typeof openSvcPanel === 'function') openSvcPanel(primaryIdx);
    }

    var _origBuildSvcGrid = window.buildSvcGrid;
    if (typeof _origBuildSvcGrid === 'function') {
        window.buildSvcGrid = function (data) {
            _origBuildSvcGrid(data);
            setTimeout(autoLoadPrimaryService, 100);
        };
    }

    $(function () {
        var gridBody = document.getElementById('svcTableBody');
        if (gridBody) {
            var observer = new MutationObserver(function () {
                if (!$('#svcTableBody tr.svc-active').length
                    && typeof svcData !== 'undefined' && svcData && svcData.length > 0) {
                    setTimeout(autoLoadPrimaryService, 50);
                }
            });
            observer.observe(gridBody, { childList: true });
        }
    });

    $(function () { setTimeout(autoLoadPrimaryService, 800); });


    /* ══════════════════════════════════════════════════════════════════
       FEATURE 3: UNSAVED CHANGES GUARD  — updated for dblclick pattern
       ══════════════════════════════════════════════════════════════════
       CHANGES FROM ORIGINAL:
       - isEditMode() now checks body.classList.contains('ws-edit')
         instead of #editBtns visibility (no Edit button in new design)
       - captureFormState() called when first field is unlocked via
         dblclick (ws-edit class is added by startEdit())
       - cancelEdit() confirm guard still works — cancelEdit() in
         Dashboard.cshtml restores values and removes ws-edit class
       - dirty dot on #btnSave (the always-visible Save button)
       - Tab switch reminder still works unchanged
    ══════════════════════════════════════════════════════════════════ */

    var _formDirty = false;
    var _initialFormState = '';

    function captureFormState() {
        var $form = $('#frmDashboard');
        if ($form.length) {
            _initialFormState = $form.serialize();
            _formDirty = false;
            updateDirtyDot();
        }
    }

    function checkDirty() {
        var $form = $('#frmDashboard');
        if (!$form.length || !_initialFormState) return false;
        return $form.serialize() !== _initialFormState;
    }

    function updateDirtyDot() {
        // CHANGED: target #btnSave (always-visible save button, not #editBtns)
        var $saveBtn = $('#btnSave');
        if (_formDirty) {
            if (!$saveBtn.find('.dirty-dot').length) {
                $saveBtn.prepend('<span class="dirty-dot"></span>');
            }
        } else {
            $saveBtn.find('.dirty-dot').remove();
        }
    }

    // Monitor form changes — only when ws-edit is active
    $(document).on('input change', '#frmDashboard input, #frmDashboard select, #frmDashboard textarea', function () {
        if (!isEditMode()) return;
        _formDirty = checkDirty();
        updateDirtyDot();
    });

    // CHANGED: hook startEdit → capture initial state when edit begins
    var _origStartEdit = window.startEdit;
    if (typeof _origStartEdit === 'function') {
        window.startEdit = function () {
            _origStartEdit.apply(this, arguments);
            setTimeout(captureFormState, 100);
        };
    }

    // Hook cancelEdit → confirm if dirty
    var _origCancelEdit = window.cancelEdit;
    if (typeof _origCancelEdit === 'function') {
        window.cancelEdit = function () {
            if (_formDirty) {
                Swal.fire({
                    title: 'Unsaved changes',
                    text: 'You have unsaved changes. Discard them?',
                    icon: 'warning',
                    showCancelButton: true,
                    confirmButtonColor: '#696cff',
                    cancelButtonColor: '#8592a3',
                    confirmButtonText: 'Discard',
                    cancelButtonText: 'Keep editing'
                }).then(function (result) {
                    if (result.isConfirmed) {
                        _formDirty = false;
                        _initialFormState = '';
                        updateDirtyDot();
                        _origCancelEdit.apply(this, arguments);
                    }
                });
            } else {
                _origCancelEdit.apply(this, arguments);
            }
        };
    }

    // Hook saveDashboard → reset dirty on save
    var _origSaveDashboard = window.saveDashboard;
    if (typeof _origSaveDashboard === 'function') {
        window.saveDashboard = function () {
            _formDirty = false;
            _initialFormState = '';
            updateDirtyDot();
            _origSaveDashboard.apply(this, arguments);
        };
    }

    // Browser close / navigate away
    $(window).on('beforeunload', function (e) {
        if (_formDirty && isEditMode()) {
            e.preventDefault();
            e.returnValue = '';
            return 'You have unsaved changes.';
        }
    });

    // Back button intercept
    $(document).on('click', '.btn-back', function (e) {
        if (_formDirty && isEditMode()) {
            e.preventDefault();
            e.stopImmediatePropagation();
            Swal.fire({
                title: 'Unsaved changes',
                text: 'Leave without saving?',
                icon: 'warning',
                showCancelButton: true,
                confirmButtonColor: '#696cff',
                cancelButtonColor: '#8592a3',
                confirmButtonText: 'Leave',
                cancelButtonText: 'Stay'
            }).then(function (result) {
                if (result.isConfirmed) {
                    _formDirty = false;
                    window.location.href = '/TrackerEmployer/Index';
                }
            });
            return false;
        }
    });

    // Remind on tab switch while editing
    $(document).on('click', '.emp-tab-nav [data-tab]', function () {
        if (_formDirty && isEditMode()) {
            showToast('Unsaved changes — remember to save', 'bx-error-circle');
        }
    });


    /* ══════════════════════════════════════════════════════════════════
       CSS INJECTION
    ══════════════════════════════════════════════════════════════════ */
    $('head').append(
        '<style>' +

        '.kb-toast{position:fixed;bottom:24px;left:50%;transform:translateX(-50%) translateY(10px);' +
        'background:#232333;color:#fff;padding:8px 18px;border-radius:8px;font-size:13px;' +
        'display:flex;align-items:center;gap:6px;opacity:0;transition:all .2s ease;z-index:9999;' +
        'pointer-events:none;font-family:inherit;}' +
        '.kb-toast-show{opacity:.92;transform:translateX(-50%) translateY(0);}' +
        '.kb-toast .bx{font-size:15px;}' +

        '#kbShortcutPanel{position:fixed;bottom:24px;right:24px;width:270px;' +
        'background:#fff;border:1px solid #e9ecef;border-radius:10px;' +
        'box-shadow:0 8px 24px rgba(0,0,0,.12);z-index:9998;font-family:inherit;}' +
        '.kb-panel-head{display:flex;justify-content:space-between;align-items:center;' +
        'padding:12px 16px;border-bottom:1px solid #f0f2f4;font-size:13px;font-weight:600;color:#333;}' +
        '.kb-panel-close{background:none;border:none;font-size:18px;color:#888;cursor:pointer;' +
        'line-height:1;padding:0 2px;}' +
        '.kb-panel-close:hover{color:#333;}' +
        '.kb-panel-body{padding:10px 16px 14px;}' +
        '.kb-row{display:flex;justify-content:space-between;align-items:center;' +
        'padding:5px 0;font-size:12px;color:#566a7f;}' +
        '.kb-row kbd{background:#f4f5f7;border:1px solid #ddd;border-radius:4px;' +
        'padding:2px 8px;font-size:11px;font-family:inherit;color:#333;min-width:36px;text-align:center;}' +

        '.dirty-dot{display:inline-block;width:7px;height:7px;border-radius:50%;' +
        'background:#ff9f43;animation:dirtyPulse 1.5s ease-in-out infinite;margin-right:4px;vertical-align:middle;}' +
        '@keyframes dirtyPulse{0%,100%{opacity:1}50%{opacity:.4}}' +

        '.kb-hint{position:fixed;bottom:24px;right:24px;background:#696cff;color:#fff;' +
        'padding:8px 14px;border-radius:8px;font-size:12px;opacity:0;' +
        'transform:translateY(8px);transition:all .3s ease;z-index:9997;pointer-events:none;}' +
        '.kb-hint-show{opacity:.9;transform:translateY(0);}' +
        '.kb-hint kbd{background:rgba(255,255,255,.2);border:1px solid rgba(255,255,255,.3);' +
        'border-radius:3px;padding:1px 6px;font-size:11px;font-family:inherit;color:#fff;}' +

        '</style>'
    );


    /* ══════════════════════════════════════════════════════════════════
       INIT
    ══════════════════════════════════════════════════════════════════ */
    $(function () {
        if (!sessionStorage.getItem('aca360_kb_hint_v3')) {
            var $h = $('<div class="kb-hint">Press <kbd>?</kbd> for keyboard shortcuts</div>');
            $('body').append($h);
            setTimeout(function () { $h.addClass('kb-hint-show'); }, 500);
            setTimeout(function () {
                $h.removeClass('kb-hint-show');
                setTimeout(function () { $h.remove(); }, 300);
            }, 4000);
            sessionStorage.setItem('aca360_kb_hint_v3', '1');
        }
    });

})();