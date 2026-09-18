/* ═══════════════════════════════════════════════════════════════════════
   dashboard-receipt-id.js
   ═══════════════════════════════════════════════════════════════════════
   Receipt ID — Hybrid Badge + Modal
   Backward-compat: supports both Dashboard (globals) and Index modal (context)

   Public API:
     loadReceiptBadge(ctx?)   — render inline badge into a container
     openReceiptModal(ctx?)   — open the receipt center modal
     prepareAddReceipt()      — switch right-form to Add mode
     editReceiptId(id)        — load a receipt into right-form for edit
     deleteReceiptId(id)      — delete a receipt
     saveReceiptId()          — save (add/update) the right-form
     toggleRcptForm(show)     — show/hide form panel (mobile)
     clearReceiptForm()       — reset right-form

   ctx shape (all optional — falls back to Dashboard globals when omitted):
     {
       employerId:        42,
       employerServiceId: 89,
       serviceId:         17,
       planYear:          '2024',
       serviceName:       '1095-C Reporting',
       badgeContainer:    '#receiptBadgeContainerModal'  // CSS selector
     }

   Dependencies: jQuery, Bootstrap 5, SweetAlert2
   Optional Dashboard globals: svcData, #activeSvcSel, #hdnEmployerId
   ═══════════════════════════════════════════════════════════════════════ */

(function () {
    'use strict';

    /* ══════════════════════════════════════════════════════════════════
       STATE
    ══════════════════════════════════════════════════════════════════ */

    var _currentReceiptServiceId = 0;
    var _currentReceiptCatalogId = 0;
    var _currentReceiptPlanYear = '';
    var _currentReceiptEmployerId = 0;
    var _currentReceiptScopeToEmployer = false;  // true only when opened from a single employer/affiliate's own page
    var _currentBadgeContainer = '#receiptBadgeContainer';  // default for Dashboard
    var _receiptBadgeData = [];

    function isReceiptEligible(serviceId) { return true; }

    /* Local esc helper — falls back to Dashboard's global if available */
    function _esc(s) {
        if (typeof window.esc === 'function') return window.esc(s);
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    /* Resolve context — explicit ctx wins; otherwise read Dashboard globals */
    function resolveContext(ctx) {
        if (ctx && (ctx.employerServiceId || ctx.EmployerServiceId)) {
            return {
                employerId:        ctx.employerId        || ctx.EmployerId        || 0,
                employerServiceId: ctx.employerServiceId || ctx.EmployerServiceId || 0,
                serviceId:         ctx.serviceId         || ctx.ServiceId         || 0,
                planYear:          ctx.planYear          || ctx.PlanYear          || '',
                serviceName:       ctx.serviceName       || ctx.ServiceName       || 'Service',
                badgeContainer:    ctx.badgeContainer    || '#receiptBadgeContainer',
                // Opt-in only: the Employer page's own ledger sets this so it only ever sees
                // the currently viewed employer/affiliate's receipts. Tracker menu contexts
                // (Dashboard/Index) leave this unset and keep seeing every EIN on the service.
                scopeToEmployer:   !!(ctx.scopeToEmployer || ctx.ScopeToEmployer)
            };
        }
        // Index page stores context here
        if (window._currentSvcCtx && window._currentSvcCtx.employerServiceId) {
            return window._currentSvcCtx;
        }
        // Fallback: read Dashboard globals
        var idx = parseInt($('#activeSvcSel').val());
        if (isNaN(idx) || typeof svcData === 'undefined' || !svcData || !svcData[idx]) return null;

        var svc = svcData[idx];
        return {
            employerId:        parseInt($('#hdnEmployerId').val()) || 0,
            employerServiceId: svc.employerServiceId || svc.EmployerServiceId || 0,
            serviceId:         svc.serviceId         || svc.ServiceId         || 0,
            planYear:          svc.planYear          || svc.PlanYear          || '',
            serviceName:       svc.serviceName       || svc.ServiceName       || 'Service',
            badgeContainer:    '#receiptBadgeContainer',
            scopeToEmployer:   false
        };
    }


    /* ══════════════════════════════════════════════════════════════════
       INLINE BADGE
    ══════════════════════════════════════════════════════════════════ */

    window.loadReceiptBadge = function (ctx) {
        var c = resolveContext(ctx);
        var $container = $(c ? c.badgeContainer : '#receiptBadgeContainer');

        if (!c || !c.employerServiceId) {
            $container.hide();
            return;
        }

        // Update state used by modal/CRUD operations
        _currentReceiptEmployerId = c.employerId;
        _currentReceiptServiceId = c.employerServiceId;
        _currentReceiptCatalogId = c.serviceId;
        _currentReceiptPlanYear = c.planYear;
        _currentBadgeContainer = c.badgeContainer;
        _currentReceiptScopeToEmployer = !!c.scopeToEmployer;

        $container.show();

        // Only restricted to this employer/affiliate's own receipts when scopeToEmployer is set
        // (the Employer page's own ledger). Tracker menu contexts keep seeing every EIN.
        $.getJSON('/TrackerEmployer/GetReceiptIds', {
            employerServiceId: c.employerServiceId,
            employerId: c.scopeToEmployer ? c.employerId : 0
        }, function (data) {
            window.activeServiceReceipts = data || [];
            document.dispatchEvent(new CustomEvent('receiptsUpdated', { detail: { receipts: data } }));

            _receiptBadgeData = data || [];
            renderReceiptBadge(_receiptBadgeData, c.badgeContainer);
            renderReceiptDetailList(_receiptBadgeData);

        }).fail(function () {
            _receiptBadgeData = [];
            renderReceiptBadge([], c.badgeContainer);
            renderReceiptDetailList([]);
        });
    };

    function renderReceiptBadge(receipts, containerSel) {
        var $targets = $(containerSel || '#receiptBadgeContainer').add('#receiptBadgeContainerInline');
        var count = receipts.length;

        if (count === 0) {
            $targets.html(
                '<div class="rcpt-badge-bar no-receipt shadow-none border" style="cursor:pointer" title="Click to add receipt IDs">' +
                '  <div class="rcpt-badge-icon bg-label-secondary"><i class="bx bx-receipt" style="font-size:.7rem"></i></div>' +
                '  <span class="rcpt-badge-label text-muted">No receipts — Click to record</span>' +
                '  <i class="bx bx-plus ms-auto text-muted"></i>' +
                '</div>'
            );
            return;
        }

        var latest = receipts[0];
        var val = latest.receiptIdValue || latest.ReceiptIdValue || '';
        var rDate = latest.receivedDate || latest.ReceivedDate;
        var fmtDate = '';
        if (rDate) {
            var d = new Date(rDate);
            fmtDate = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
        }

        $targets.html(
            '<div class="rcpt-badge-bar has-receipt shadow-sm" style="cursor:pointer" title="Click to view all ' + count + ' receipt(s) — opens edit center">' +
            '  <div class="rcpt-badge-icon bg-primary text-white shadow-sm"><i class="bx bx-receipt" style="font-size:.7rem"></i></div>' +
            '  <span class="rcpt-badge-label text-primary fw-bold">Recent:</span>' +
            '  <span class="rcpt-badge-value text-heading fw-bold ms-1">' + _esc(val) + '</span>' +
            (fmtDate ? '<span class="rcpt-badge-date badge bg-label-secondary ms-2">' + fmtDate + '</span>' : '') +
            '  <span class="rcpt-badge-count badge rounded-pill bg-primary ms-auto">' + count + '</span>' +
            '  <i class="bx bx-edit-alt ms-1 text-primary" style="font-size:.95rem" title="Edit"></i>' +
            '</div>'
        );
    }


    function renderReceiptDetailList(receipts) {
        var $el = $('#receiptDetailList');
        if (!$el.length) return;

        if (!receipts || receipts.length === 0) {
            $el.html(
                '<div class="text-center text-muted py-3" style="font-size:0.78rem">' +
                '  <i class="bx bx-receipt d-block mb-1 opacity-25" style="font-size:1.8rem"></i>' +
                '  No receipt IDs recorded yet.' +
                '</div>'
            );
            return;
        }

        var html = '<div class="table-responsive"><table class="table table-sm table-hover mb-0" style="font-size:0.78rem">';
        html += '<thead class="table-light"><tr>' +
                '<th class="py-1 px-2">Receipt ID</th>' +
                '<th class="py-1 px-2">EIN / Affiliate</th>' +
                '<th class="py-1 px-2">Received Date</th>' +
                '<th class="py-1 px-2">Notes</th>' +
                '<th class="py-1 px-2 text-center" style="width:60px">Actions</th>' +
                '</tr></thead><tbody>';

        receipts.forEach(function (r) {
            var rid = r.receiptEntryId || r.ReceiptEntryId;
            var rval = r.receiptIdValue || r.ReceiptIdValue || '—';
            var ein = r.affiliateEIN || r.AffiliateEIN || '';
            var affiliateName = r.affiliateName || r.AffiliateName || '';
            var rdate = r.receivedDate || r.ReceivedDate;
            var fmtDate = rdate ? new Date(rdate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
            var notes = r.notes || r.Notes || '';
            var isPrimary = r.isPrimary || r.IsPrimary;

            html += '<tr>';
            html += '<td class="py-1 px-2"><span class="fw-bold text-heading" style="font-family:monospace">' + _esc(rval) + '</span></td>';
            html += '<td class="py-1 px-2">' +
                    '<span class="text-body-secondary">' + _esc(affiliateName) + '</span>' +
                    (ein ? ' <span class="badge bg-label-secondary" style="font-size:0.6rem">' + _esc(ein) + '</span>' : '') +
                    (isPrimary ? ' <span class="badge bg-label-success" style="font-size:0.6rem">Primary</span>' : '') +
                    '</td>';
            html += '<td class="py-1 px-2 text-muted">' + fmtDate + '</td>';
            html += '<td class="py-1 px-2 text-muted">' + (notes ? _esc(notes) : '<span class="opacity-50">—</span>') + '</td>';
            html += '<td class="py-1 px-2 text-center">' +
                    '<button type="button" class="btn btn-xs btn-icon btn-label-primary border-0 p-0" title="Edit" onclick="openReceiptModal(); setTimeout(function(){ editReceiptId(' + rid + '); }, 400)">' +
                    '<i class="bx bx-edit-alt" style="font-size:0.85rem"></i></button> ' +
                    '<button type="button" class="btn btn-xs btn-icon btn-label-danger border-0 p-0" title="Delete" onclick="deleteReceiptId(' + rid + ')">' +
                    '<i class="bx bx-trash" style="font-size:0.85rem"></i></button>' +
                    '</td>';
            html += '</tr>';
        });

        html += '</tbody></table></div>';
        $el.html(html);
    }

    /* ══════════════════════════════════════════════════════════════════
       MODAL — Open + Load
    ══════════════════════════════════════════════════════════════════ */

    window.openReceiptModal = function (ctx) {
        var c = resolveContext(ctx);
        if (!c || !c.employerServiceId) return;

        _currentReceiptEmployerId = c.employerId;
        _currentReceiptServiceId = c.employerServiceId;
        _currentReceiptCatalogId = c.serviceId;
        _currentReceiptPlanYear = c.planYear;
        _currentReceiptScopeToEmployer = !!c.scopeToEmployer;
        // Don't overwrite _currentBadgeContainer — keep whatever loadReceiptBadge set,
        // so post-save list refresh updates the right inline badge.

        $('#receiptModalTitle').text('Receipt Center');
        $('#receiptModalSubtitle').text((c.serviceName || 'Service') + ' — ' + (c.planYear || '—'));

        loadReceiptEINDropdown();
        loadReceiptList();
        clearReceiptForm();

        bootstrap.Modal.getOrCreateInstance(document.getElementById('receiptIdModal')).show();
    };

    window.prepareAddReceipt = function () {
        clearReceiptForm();
        $('#rcptDrawerTitle').html('<i class="bx bx-plus-circle me-2 fs-4 text-primary"></i> Add Receipt ID');
        $('#btnSaveRcptLabel').text('Save Receipt');
        $('.rcpt-card').removeClass('active border-primary').addClass('border-light');
        $('#rcptIdInput').focus();
    };

    window.toggleRcptForm = function (show) {
        if (show) $('#rcptFormPanel').removeClass('d-none');
        else $('#rcptFormPanel').addClass('d-none');
    };

    function loadReceiptEINDropdown() {
        var empId = _currentReceiptEmployerId || $('#hdnEmployerId').val();
        $.getJSON('/TrackerEmployer/GetAffiliates', { employerId: empId }, function (data) {
            var sel = $('#rcptEINSelect');
            sel.html('<option value="">— Select EIN —</option>');

            // GetAffiliates always returns the whole family (main + every affiliate).
            // Only restrict to the single currently-viewed entry when opened from the
            // Employer page's own ledger (scopeToEmployer) — Tracker menu contexts still
            // need to pick any sibling EIN, so they keep seeing the full family.
            var list = data || [];
            if (_currentReceiptScopeToEmployer) {
                var scoped = list.filter(function (a) {
                    var id = a.affiliateId || a.AffiliateId;
                    return id === empId;
                });
                if (scoped.length) list = scoped;
            }

            list.forEach(function (a) {
                var name = a.affiliateName || a.AffiliateName;
                var ein = a.ein || a.EIN || 'No EIN';
                var isMain = a.isMain || a.IsMain;
                var id = a.affiliateId || a.AffiliateId;
                var label = name + ' (' + ein + ')';
                if (isMain) label += ' [Primary]';
                sel.append('<option value="' + id + '">' + label + '</option>');
            });

            // Single result — pre-select it since there's nothing else to choose from.
            if (_currentReceiptScopeToEmployer && list.length === 1) {
                sel.val(list[0].affiliateId || list[0].AffiliateId);
            }
        });
    }

    function loadReceiptList(impactedAffiliateId) {
        // Only scoped to the currently-viewed employer/affiliate when opened from the
        // Employer page's own ledger (scopeToEmployer). Tracker menu contexts still
        // manage every EIN sharing this EmployerServiceId from one place.
        $.getJSON('/TrackerEmployer/GetReceiptIds', {
            employerServiceId: _currentReceiptServiceId,
            employerId: _currentReceiptScopeToEmployer ? _currentReceiptEmployerId : 0
        }, function (data) {
            window.activeServiceReceipts = data || [];
            document.dispatchEvent(new CustomEvent('receiptsUpdated', {
                detail: { receipts: data, impactedAffiliateId: impactedAffiliateId }
            }));

            var container = $('#receiptListContainer');
            var count = (data || []).length;
            $('#receiptCountLarge').text(count);
            $('#receiptCount').text(count + ' receipt' + (count === 1 ? '' : 's') + ' found');

            var recentCount = 0;
            var sevenDaysAgo = new Date();
            sevenDaysAgo.setDate(sevenDaysAgo.getDate() - 7);

            if (!data || !data.length) {
                $('#receiptSyncCount').text('0');
                container.html(
                    '<div class="text-center text-muted py-5">' +
                    '  <i class="bx bx-receipt d-block mb-3 opacity-25" style="font-size:3.5rem"></i>' +
                    '  <h6 class="fw-bold mb-1">No receipts yet</h6>' +
                    '  <p class="x-small px-4">Receipt IDs recorded for this service will appear here.</p>' +
                    '</div>'
                );
                _receiptBadgeData = [];
                renderReceiptBadge([], _currentBadgeContainer);
                renderReceiptDetailList([]);
                return;
            }

            var groups = {};
            data.forEach(function (r) {
                var key = r.affiliateEINId || r.AffiliateEINId;
                if (!groups[key]) {
                    groups[key] = {
                        name: r.affiliateName || r.AffiliateName || 'Unknown',
                        ein: r.affiliateEIN || r.AffiliateEIN || '',
                        isPrimary: r.isPrimary || r.IsPrimary || false,
                        receipts: []
                    };
                }
                groups[key].receipts.push(r);
                var rDate = r.receivedDate || r.ReceivedDate;
                if (rDate && new Date(rDate) > sevenDaysAgo) recentCount++;
            });

            $('#receiptSyncCount').text(recentCount);

            var html = '';
            Object.keys(groups).forEach(function (einId) {
                var g = groups[einId];
                html += '<div class="rcpt-group mb-4">';
                html += '  <div class="d-flex align-items-center justify-content-between mb-2 px-1 text-uppercase letter-spacing-xsmall">';
                html += '    <div class="d-flex align-items-center gap-2">';
                html += '      <span class="fw-bold x-small text-primary">' + _esc(g.name) + '</span>';
                html += '      <span class="badge bg-label-secondary p-1 px-2" style="font-size:0.6rem">' + _esc(g.ein) + '</span>';
                if (g.isPrimary) {
                    html += '    <span class="badge bg-label-success p-1 px-2" style="font-size:0.6rem">Primary</span>';
                }
                html += '    </div>';
                html += '    <span class="text-muted x-small">' + g.receipts.length + ' item(s)</span>';
                html += '  </div>';

                g.receipts.forEach(function (r) {
                    var rid = r.receiptEntryId || r.ReceiptEntryId;
                    var rval = r.receiptIdValue || r.ReceiptIdValue || '';
                    var rdate = r.receivedDate || r.ReceivedDate;
                    var fmtDate = rdate ? new Date(rdate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
                    var statusClass = (rval && rdate) ? 'bg-success' : 'bg-warning';

                    html += '  <div class="rcpt-card card shadow-none border mb-2 cursor-pointer" data-id="' + rid + '">';
                    html += '    <div class="card-body p-3">';
                    html += '      <div class="d-flex justify-content-between align-items-start">';
                    html += '        <div class="d-flex gap-3 align-items-center cursor-pointer flex-grow-1" onclick="editReceiptId(' + rid + ')">';
                    html += '          <div class="rcpt-badge-icon bg-label-primary rounded-circle d-flex align-items-center justify-content-center" style="width:32px; height:32px"><i class="bx bx-receipt"></i></div>';
                    html += '          <div>';
                    html += '            <div class="d-flex align-items-center gap-2 mb-0">';
                    html += '              <span class="fw-bold text-heading" style="font-family:monospace; font-size:0.95rem">' + _esc(rval) + '</span>';
                    html += '              <span class="status-dot ' + statusClass + '"></span>';
                    html += '            </div>';
                    html += '            <div class="text-muted fs-tiny"><i class="bx bx-calendar-event me-1"></i>' + fmtDate + '</div>';
                    html += '          </div>';
                    html += '        </div>';
                    html += '        <div class="d-flex gap-1">';
                    html += '          <button type="button" class="btn btn-sm btn-icon btn-label-primary border-0" title="Edit" onclick="editReceiptId(' + rid + ')">';
                    html += '            <i class="bx bx-edit-alt"></i>';
                    html += '          </button>';
                    html += '          <button type="button" class="btn btn-sm btn-icon btn-label-danger border-0" title="Delete" onclick="deleteReceiptId(' + rid + ')">';
                    html += '            <i class="bx bx-trash"></i>';
                    html += '          </button>';
                    html += '        </div>';
                    html += '      </div>';
                    html += '    </div>';
                    html += '  </div>';
                });

                html += '</div>';
            });

            container.html(html);

            // Refresh inline badge and detail list
            _receiptBadgeData = data;
            renderReceiptBadge(data, _currentBadgeContainer);
            renderReceiptDetailList(data);
        });
    }


    /* ══════════════════════════════════════════════════════════════════
       CRUD
    ══════════════════════════════════════════════════════════════════ */

    window.saveReceiptId = function () {
        var einId = $('#rcptEINSelect').val();
        var receiptVal = $('#rcptIdInput').val().trim();
        var receiptDate = $('#rcptDateInput').val();
        var notes = $('#rcptNotesInput').val().trim();
        var editId = parseInt($('#rcptEditId').val()) || 0;

        if (!einId) { Swal.fire('Check Input', 'Please select an affiliate/EIN.', 'warning'); return; }
        if (!receiptVal) { Swal.fire('Check Input', 'Please enter the Receipt ID value.', 'warning'); return; }

        var $btn = $('#btnSaveReceipt');
        $btn.addClass('disabled').html('<span class="spinner-border spinner-border-sm me-2"></span> Saving...');

        $.post('/TrackerEmployer/SaveReceiptId', {
            ReceiptEntryId: editId,
            EmployerId: _currentReceiptEmployerId || $('#hdnEmployerId').val(),
            AffiliateEINId: einId,
            EmployerServiceId: _currentReceiptServiceId,
            ServiceId: _currentReceiptCatalogId,
            ReceiptIdValue: receiptVal,
            ReceivedDate: receiptDate || null,
            PlanYear: _currentReceiptPlanYear,
            Notes: notes || null
        }).done(function (r) {
            if (r.success) {
                Swal.fire({ icon: 'success', title: 'Data Secured', text: editId > 0 ? 'Receipt updated successfully.' : 'New receipt recorded.', timer: 1500, showConfirmButton: false });
                clearReceiptForm();
                loadReceiptList(einId);
            } else {
                Swal.fire('Error', r.message || 'Transmission failed. Please retry.', 'error');
            }
        }).always(function () {
            $btn.removeClass('disabled').html('<i class="bx bx-save me-2"></i> <span id="btnSaveRcptLabel">' + (editId > 0 ? 'Update Receipt' : 'Save Receipt') + '</span>');
        });
    };

    window.editReceiptId = function (id) {
        $.getJSON('/TrackerEmployer/GetReceiptIds', {
            employerServiceId: _currentReceiptServiceId
        }, function (data) {
            var r = (data || []).find(function (x) { return (x.receiptEntryId || x.ReceiptEntryId) === id; });
            if (!r) return;

            $('#rcptEditId').val(id);
            $('#rcptEINSelect').val(r.affiliateEINId || r.AffiliateEINId);
            $('#rcptIdInput').val(r.receiptIdValue || r.ReceiptIdValue || '');
            var d = r.receivedDate || r.ReceivedDate;
            if (d) $('#rcptDateInput').val(new Date(d).toISOString().split('T')[0]);
            $('#rcptNotesInput').val(r.notes || r.Notes || '');

            $('#rcptDrawerTitle').html('<i class="bx bx-edit-alt me-2 fs-4 text-primary"></i> Edit Receipt ID');
            $('#btnSaveRcptLabel').text('Update Receipt');

            $('.rcpt-card').removeClass('border-primary shadow-md active').addClass('border-light');
            var $active = $('.rcpt-card[data-id="' + id + '"]');
            $active.addClass('border-primary shadow-md active').removeClass('border-light');

            $('#rcptIdInput').focus();
        });
    };

    window.deleteReceiptId = function (id) {
        Swal.fire({
            title: 'Permanent Removal',
            text: 'Are you sure you want to delete this receipt ID record?',
            icon: 'warning',
            showCancelButton: true,
            confirmButtonText: 'Yes, Delete',
            customClass: { confirmButton: 'btn btn-danger me-3', cancelButton: 'btn btn-label-secondary' },
            buttonsStyling: false
        }).then(function (result) {
            if (result.isConfirmed) {
                $.post('/TrackerEmployer/DeleteReceiptId', { id: id }).done(function (r) {
                    if (r.success) {
                        Swal.fire({ icon: 'success', title: 'Removed', timer: 1000, showConfirmButton: false });
                        loadReceiptList();
                    }
                });
            }
        });
    };

    window.clearReceiptForm = function () {
        $('#rcptEditId').val('0');
        $('#rcptEINSelect').val('');
        $('#rcptIdInput').val('');
        $('#rcptDateInput').val('');
        $('#rcptNotesInput').val('');
        $('#rcptDrawerTitle').html('<i class="bx bx-plus-circle me-2 fs-4 text-primary"></i> Add Receipt ID');
        $('#btnSaveRcptLabel').text('Save Receipt');
    };


    /* ══════════════════════════════════════════════════════════════════
       AUTO-WIRE: Modal-mode badge containers (data-* attributes)
    ══════════════════════════════════════════════════════════════════ */

    /* When _ServicePanel renders inside a modal it places a div like:
         <div id="receiptBadgeContainerModal"
              data-emp-id="..."
              data-emp-svc-id="..."
              data-svc-id="..."
              data-plan-year="..."
              data-svc-name="...">
       Auto-load it whenever the svcModal is shown. */

    // Delegated click handler — works inside dynamically-loaded modals
    $(document).off('click.rcpt', '.rcpt-badge-bar').on('click.rcpt', '.rcpt-badge-bar', function (e) {
        e.preventDefault();
        e.stopPropagation();
        window.openReceiptModal();
    });
    $(document).on('shown.bs.modal', '#svcModal', function () {
        var $box = $('#receiptBadgeContainerModal');
        if ($box.length === 0) return;

        var ctx = {
            employerId:        $box.data('emp-id'),
            employerServiceId: $box.data('emp-svc-id'),
            serviceId:         $box.data('svc-id'),
            planYear:          $box.data('plan-year'),
            serviceName:       $box.data('svc-name'),
            badgeContainer:    '#receiptBadgeContainerModal'
        };

        if (ctx.employerServiceId) {
            window.loadReceiptBadge(ctx);
        }
    });


    /* ══════════════════════════════════════════════════════════════════
       CSS INJECTION
    ══════════════════════════════════════════════════════════════════ */

    $('head').append(
        '<style>' +

        '.rcpt-badge-bar{display:flex;align-items:center;gap:10px;padding:8px 12px;' +
        'border-radius:10px;cursor:pointer;margin-top:0;transition:all .2s ease; border:1px solid transparent}' +
        '.rcpt-badge-bar.has-receipt{background:var(--bs-primary-bg-subtle); color:var(--bs-primary)}' +
        '.rcpt-badge-bar.has-receipt:hover{background:var(--bs-primary-border-subtle); border-color:var(--bs-primary); transform:translateY(-1px)}' +
        '.rcpt-badge-bar.no-receipt{background:var(--bs-tertiary-bg); border:1px solid var(--bs-border-color)}' +
        '.rcpt-badge-bar.no-receipt:hover{background:var(--bs-secondary-bg-subtle); border-color:var(--bs-primary)}' +

        '.rcpt-badge-icon{width:22px;height:22px;border-radius:6px;display:flex;' +
        'align-items:center;justify-content:center;font-size:.7rem;flex-shrink:0}' +

        '.rcpt-badge-label{font-size:.75rem; letter-spacing:.01em}' +
        '.rcpt-badge-value{font-size:.8rem; font-family:"Public Sans", -apple-system, sans-serif}' +
        '.rcpt-badge-count{font-size:.7rem; font-weight:600; min-width:20px; text-align:center}' +

        '.rcpt-card{transition:all .25s ease; border-radius:12px !important; border-width:1.5px !important}' +
        '.rcpt-card:hover:not(.active){transform:translateY(-3px); box-shadow:0 6px 15px rgba(var(--bs-primary-rgb),0.1) !important; border-color:var(--bs-primary-border-subtle) !important}' +
        '.rcpt-card.active{border-color:var(--bs-primary) !important; background-color:rgba(var(--bs-primary-rgb),0.02) !important; box-shadow:0 4px 10px rgba(var(--bs-primary-rgb),0.12) !important}' +

        '.status-dot{width:8px;height:8px;border-radius:50%;display:inline-block}' +
        '.status-dot.bg-success{background-color:var(--bs-success)}' +
        '.status-dot.bg-warning{background-color:var(--bs-warning)}' +

        '.bg-lighter{background-color:var(--bs-tertiary-bg)}' +
        '.letter-spacing-px{letter-spacing:1px}' +
        '.letter-spacing-xsmall{letter-spacing:0.04em}' +

        '</style>'
    );
   
})();
