/* ═══════════════════════════════════════════════════════════
   SHARED NOTES — table-format notes list + searchable category
   dropdown, reusable for Employer/Employee/Plan/Dependent.
   Generalized from the Tracker Dashboard.cshtml inline script
   (openCatDrop/searchCat/selectCat/loadNotes/applyNoteFilters).

   Usage: one instance per wrapper element with
   data-entity-type / data-entity-id attributes, e.g.:
     <div class="notes-widget" data-entity-type="Employer" data-entity-id="123">...</div>
   Call initNotesWidget(wrapperEl) once the partial is in the DOM.
   ═══════════════════════════════════════════════════════════ */

(function (window) {
    'use strict';

    function getAntiForgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function getJson(url, params, onSuccess, onError) {
        var query = Object.keys(params)
            .map(function (k) { return encodeURIComponent(k) + '=' + encodeURIComponent(params[k] == null ? '' : params[k]); })
            .join('&');
        fetch(url + '?' + query, { credentials: 'same-origin' })
            .then(function (res) {
                if (!res.ok) throw new Error('Request failed: ' + res.status);
                return res.json();
            })
            .then(onSuccess)
            .catch(onError || function () {});
    }

    function postForm(url, fields, onSuccess) {
        var body = Object.keys(fields)
            .map(function (k) { return encodeURIComponent(k) + '=' + encodeURIComponent(fields[k] == null ? '' : fields[k]); })
            .join('&');
        fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body
        })
            .then(function (res) { return res.json(); })
            .then(onSuccess)
            .catch(function () {});
    }

    var CATEGORY_GROUPS = [
        { label: 'General', items: ['General Note', 'Follow Up', 'FTE'] },
        { label: 'Employer', items: ['Employer Name', 'EIN', 'Affiliate', 'Minimum Essential Coverage', 'Minimum Value', 'Funding', 'Premium', 'Waiting Period', 'Coverage Offered', 'Plan Renewal'] },
        { label: 'Individual', items: ['Employee Note', 'Dependent Note'] },
        { label: 'Processing', items: ['DA-Annual Processing Note', 'DA-Special Processing Note'] },
        { label: 'Business', items: ['Broker', 'Service Agreement', 'Sales-General', 'Sales-Renewal', 'Accounting-Billing', 'Accounting-Special Date', 'Termination'] },
        { label: 'Compliance', items: ['Penalty-Potential Penalty', 'Penalty-Letter Received', 'Receipt ID', 'FT William', 'SourceOne', 'State Filing', 'Do Not Mail', 'E-File Only'] },
        { label: 'Management', items: ['Management-Special Note', 'Escalated Issue'] }
    ];

    function friendlyName(raw) {
        var s = (raw || '').toString().trim();
        if (!s) return 'System';
        if (s.indexOf('@') === -1) return s;
        var local = s.split('@')[0];
        return local.charAt(0).toUpperCase() + local.slice(1);
    }

    function esc(s) {
        return (s || '').toString()
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    function buildCategoryDropdownHtml(ctx, includeAllOption) {
        var html = '';
        if (includeAllOption) {
            html += '<div class="cat-option" data-val="" onclick="NotesWidget.selectCat(\'' + ctx + '\',\'All Categories\',\'\')">All Categories</div>';
        }
        CATEGORY_GROUPS.forEach(function (group) {
            html += '<div class="cat-group-label">' + esc(group.label) + '</div>';
            group.items.forEach(function (item) {
                html += '<div class="cat-option" data-val="' + esc(item) + '" onclick="NotesWidget.selectCat(\'' + ctx + '\',\'' + esc(item).replace(/'/g, "\\'") + '\',\'' + esc(item).replace(/'/g, "\\'") + '\')">' + esc(item) + '</div>';
            });
        });
        html += '<div class="cat-no-results" style="display:none">No matches</div>';
        return html;
    }

    var widgets = {};

    function getWidget(root) {
        var id = root.getAttribute('data-widget-id');
        return widgets[id];
    }

    function initNotesWidget(root) {
        if (!root) return;
        var widgetId = 'nw_' + Math.random().toString(36).slice(2, 9);
        root.setAttribute('data-widget-id', widgetId);

        var entityType = root.getAttribute('data-entity-type');
        var entityId = root.getAttribute('data-entity-id');
        var currentUserName = root.getAttribute('data-current-user') || 'System';
        var readOnly = root.getAttribute('data-readonly') === 'true';
        var displayMode = root.getAttribute('data-display-mode') === 'cards' ? 'cards' : 'table';
        var sessionFilingYear = parseInt(root.getAttribute('data-filing-year'), 10) || new Date().getFullYear();

        var state = {
            entityType: entityType,
            entityId: entityId,
            currentUserName: currentUserName,
            readOnly: readOnly,
            displayMode: displayMode,
            sessionFilingYear: sessionFilingYear,
            allNotes: [],
            filteredNotes: [],
            visibleCount: 5,
            filterCategory: '',
            filterUser: '',
            filterYear: ''
        };
        widgets[widgetId] = state;

        // Populate filter + modal category dropdowns
        var filterDrop = root.querySelector('[data-role="filterCatDrop"]');
        var modalDrop = root.querySelector('[data-role="modalCatDrop"]');
        if (filterDrop) filterDrop.innerHTML = buildCategoryDropdownHtml(widgetId + '-filter', true);
        if (modalDrop) modalDrop.innerHTML = buildCategoryDropdownHtml(widgetId + '-modal', false);

        loadNotes(root);
        loadAuthors(root);

        return widgetId;
    }

    function loadNotes(root) {
        var widgetId = root.getAttribute('data-widget-id');
        var state = widgets[widgetId];
        var listEl = root.querySelector('[data-role="notesTableBody"]');
        var isCards = state.displayMode === 'cards';
        if (listEl) listEl.innerHTML = isCards
            ? '<div class="text-center text-muted py-4 small">Loading notes…</div>'
            : '<tr><td colspan="5" class="text-center text-muted py-4 small">Loading notes…</td></tr>';

        getJson('/Notes/GetNotesByEntity', {
            entityType: state.entityType,
            entityId: state.entityId
        }, function (data) {
            state.allNotes = data || [];
            applyFilters(root);
        }, function () {
            if (listEl) listEl.innerHTML = isCards
                ? '<div class="text-center text-danger py-3 small">Error loading notes.</div>'
                : '<tr><td colspan="5" class="text-center text-danger py-3 small">Error loading notes.</td></tr>';
        });
    }

    var CHIP_COLOR_CLASSES = ['bg-label-primary', 'bg-label-success', 'bg-label-info', 'bg-label-warning', 'bg-label-secondary', 'bg-label-dark'];

    function categoryColorClass(category) {
        var str = category || 'General Note';
        var hash = 0;
        for (var i = 0; i < str.length; i++) { hash = (hash * 31 + str.charCodeAt(i)) >>> 0; }
        return CHIP_COLOR_CLASSES[hash % CHIP_COLOR_CLASSES.length];
    }

    function loadAuthors(root) {
        var widgetId = root.getAttribute('data-widget-id');
        var state = widgets[widgetId];
        var userSelect = root.querySelector('[data-role="filterUser"]');
        if (!userSelect) return;

        getJson('/Notes/GetNoteAuthors', {
            entityType: state.entityType,
            entityId: state.entityId
        }, function (authors) {
            var html = '<option value="">All Users</option>';
            (authors || []).forEach(function (a) {
                html += '<option value="' + a.userId + '">' + esc(friendlyName(a.userName)) + '</option>';
            });
            userSelect.innerHTML = html;
        });
    }

    function applyFilters(root) {
        var widgetId = root.getAttribute('data-widget-id');
        var state = widgets[widgetId];
        var searchBox = root.querySelector('[data-role="noteSearchBox"]');
        var searchQuery = (searchBox ? searchBox.value : '').toLowerCase().trim();

        var filtered = state.allNotes.filter(function (n) {
            var matchesCat = !state.filterCategory || n.category === state.filterCategory;
            var matchesUser = !state.filterUser || String(n.createdByUserId) === String(state.filterUser);
            var matchesYear = !state.filterYear || String(n.filingYear) === String(state.filterYear);
            var matchesSearch = !searchQuery ||
                (n.noteText || '').toLowerCase().indexOf(searchQuery) !== -1 ||
                (n.createdByName || '').toLowerCase().indexOf(searchQuery) !== -1;
            return matchesCat && matchesUser && matchesYear && matchesSearch;
        });

        var countEl = root.querySelector('[data-role="noteCount"]');
        if (countEl) {
            if (filtered.length === 0) {
                countEl.classList.add('d-none');
            } else {
                countEl.classList.remove('d-none');
                countEl.textContent = filtered.length;
            }
        }

        var statusEl = root.querySelector('[data-role="noteFilterStatus"]');
        if (statusEl) {
            statusEl.textContent = (state.filterCategory || state.filterUser || state.filterYear || searchQuery)
                ? filtered.length + ' of ' + state.allNotes.length : '';
        }

        state.filteredNotes = filtered;
        state.visibleCount = 5;
        renderTable(root, filtered, state);
    }

    function renderTable(root, notes, state) {
        var wrap = root.querySelector('[data-role="notesTableBody"]');
        if (!wrap) return;

        var emptyMsg = state.displayMode === 'cards'
            ? '<div class="text-center text-muted py-4 small">No notes matched filters.</div>'
            : '<tr><td colspan="5" class="text-center text-muted py-4 small">No notes matched filters.</td></tr>';
        if (!notes.length) {
            wrap.innerHTML = emptyMsg;
            return;
        }

        notes.sort(function (a, b) {
            if (!!b.isPinned !== !!a.isPinned) return b.isPinned ? 1 : -1;
            return new Date(b.createdDate) - new Date(a.createdDate);
        });

        wrap.innerHTML = state.displayMode === 'cards'
            ? renderCards(root, notes, state)
            : renderRows(root, notes, state);
    }

    function renderCards(root, notes, state) {
        var widgetId = root.getAttribute('data-widget-id');
        var PAGE_SIZE = 5;
        var visible = notes.slice(0, state.visibleCount);
        var remaining = notes.length - visible.length;

        var cardsHtml = visible.map(function (n) {
            var d = new Date(n.createdDate);
            var formattedDate = isNaN(d.getTime()) ? '' : d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
            var creator = friendlyName((n.createdByName || '').toString().trim() || state.currentUserName);
            var initials = creator.split(/\s+/).map(function (p) { return p.charAt(0); }).join('').slice(0, 2).toUpperCase() || '?';
            var colorCls = categoryColorClass(n.category);
            var avatarCls = categoryColorClass(creator);
            var pinnedCardClass = n.isPinned ? ' note-card-pinned' : '';
            var fyBadge = n.filingYear ? '<span class="badge bg-label-info border-0 notes-meta-badge ms-1">FY ' + n.filingYear + '</span>' : '';
            var sourceBadge = n.sourceSystem ? '<span class="badge bg-label-secondary border-0 notes-meta-badge ms-1">' + esc(n.sourceSystem) + '</span>' : '';

            var pinBtn = state.readOnly ? '' :
                '<button type="button" class="btn btn-icon btn-sm rounded-circle ' + (n.isPinned ? 'btn-warning' : 'btn-label-secondary') + ' note-action-btn" onclick="NotesWidget.togglePin(\'' + widgetId + '\',' + n.noteId + ')" title="' + (n.isPinned ? 'Unpin' : 'Pin as confirmed') + '"><i class="bx ' + (n.isPinned ? 'bxs-pin' : 'bx-pin') + '"></i></button>';
            var actionBtns = state.readOnly ? '' :
                '<div class="d-flex gap-1">'
                + pinBtn
                + '<button type="button" class="btn btn-icon btn-sm btn-label-primary rounded-circle note-action-btn" onclick="NotesWidget.openEdit(\'' + widgetId + '\',' + n.noteId + ')" title="Edit"><i class="bx bx-pencil"></i></button>'
                + '<button type="button" class="btn btn-icon btn-sm btn-label-danger rounded-circle note-action-btn" onclick="NotesWidget.remove(\'' + widgetId + '\',' + n.noteId + ')" title="Delete"><i class="bx bx-trash"></i></button>'
                + '</div>';

            return '<div class="card note-card mb-2 shadow-sm border-0' + pinnedCardClass + '">'
                + '<div class="card-body p-3">'

                // ── Row 1: category badge + confirmed + date ──
                + '<div class="d-flex align-items-center justify-content-between mb-2">'
                +   '<div class="d-flex align-items-center gap-1 flex-wrap">'
                +     '<span class="badge ' + colorCls + ' border-0 notes-cat-badge">' + esc(n.category || 'General Note') + '</span>'
                +     (n.isPinned ? '<span class="badge bg-label-warning border-0 notes-cat-badge"><i class="bx bxs-pin me-1"></i>Confirmed</span>' : '')
                +   '</div>'
                +   '<span class="notes-meta-text text-muted text-nowrap">' + formattedDate + '</span>'
                + '</div>'

                // ── Row 2: note text ──
                + '<p class="note-text-clamp mb-3 notes-body-text">' + esc(n.noteText) + '</p>'

                // ── Row 3: author + filing year + actions ──
                + '<div class="d-flex align-items-center justify-content-between">'
                +   '<div class="d-flex align-items-center gap-2">'
                +     '<span class="avatar avatar-xs"><span class="avatar-initial rounded-circle ' + avatarCls + '">' + esc(initials) + '</span></span>'
                +     '<div>'
                +       '<div class="notes-author-name">' + esc(creator) + '</div>'
                +       '<div class="d-flex align-items-center gap-1">' + fyBadge + sourceBadge + '</div>'
                +     '</div>'
                +   '</div>'
                +   actionBtns
                + '</div>'

                + '</div>'
                + '</div>';
        }).join('');

        var showMoreHtml = remaining > 0
            ? '<div class="text-center py-2">'
            +   '<button type="button" class="btn btn-sm btn-label-primary rounded-pill notes-show-more-btn" onclick="NotesWidget.showMore(\'' + widgetId + '\')">'
            +     '<i class="bx bx-chevron-down me-1"></i>Show more <span class="badge bg-primary ms-1">' + remaining + '</span>'
            +   '</button>'
            + '</div>'
            : (notes.length > PAGE_SIZE
                ? '<div class="text-center py-1"><span class="text-muted notes-meta-text">All ' + notes.length + ' notes shown</span></div>'
                : '');

        return cardsHtml + showMoreHtml;
    }

    function renderRows(root, notes, state) {
        var widgetId = root.getAttribute('data-widget-id');

        return notes.map(function (n) {
            var d = new Date(n.createdDate);
            var formattedDate = isNaN(d.getTime()) ? '' : d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
            var creator = friendlyName((n.createdByName || "").toString().trim() || state.currentUserName);
            var pinnedRowClass = n.isPinned ? ' notes-row-pinned' : '';
            var pinnedBadge = n.isPinned
                ? '<span class="badge bg-label-warning border-0 ms-1" title="Confirmed — pinned so this is not re-asked"><i class="bx bxs-pin"></i> Confirmed</span>'
                : '';
            var sourceBadge = n.sourceSystem ? ' <span class="badge bg-label-info border-0 notes-badge-sm">' + esc(n.sourceSystem) + '</span>' : '';

            var pinBtn = state.readOnly ? '' :
                '<button type="button" class="btn btn-sm p-0 ' + (n.isPinned ? 'text-warning' : 'text-muted') + ' me-2" onclick="NotesWidget.togglePin(\'' + widgetId + '\',' + n.noteId + ')" title="' + (n.isPinned ? 'Unpin' : 'Pin as confirmed') + '"><i class="bx ' + (n.isPinned ? 'bxs-pin' : 'bx-pin') + '"></i></button>';

            return '<tr class="' + pinnedRowClass + '">'
                + '<td class="text-nowrap">' + formattedDate + '</td>'
                + '<td><span class="badge bg-label-secondary border-0">' + esc(n.category || 'General Note') + '</span>' + pinnedBadge + '</td>'
                + '<td class="notes-cell-text">' + esc(n.noteText) + '</td>'
                + '<td class="text-nowrap">' + esc(creator) + sourceBadge + '</td>'
                + (state.readOnly ? '' :
                    '<td class="text-nowrap">'
                    + pinBtn
                    + '<button type="button" class="btn btn-sm p-0 text-primary me-2" onclick="NotesWidget.openEdit(\'' + widgetId + '\',' + n.noteId + ')" title="Edit"><i class="bx bx-pencil"></i></button>'
                    + '<button type="button" class="btn btn-sm p-0 text-danger" onclick="NotesWidget.remove(\'' + widgetId + '\',' + n.noteId + ')" title="Delete"><i class="bx bx-trash"></i></button>'
                    + '</td>')
                + '</tr>';
        }).join('');
    }

    function getRoot(widgetId) {
        return document.querySelector('[data-widget-id="' + widgetId + '"]');
    }

    function openAdd(widgetId) {
        var root = getRoot(widgetId);
        if (!root) return;
        resetForm(widgetId);
        showForm(root, true);
    }

    function openEdit(widgetId, noteId) {
        var root = getRoot(widgetId);
        var state = widgets[widgetId];
        if (!root || !state) return;
        var note = state.allNotes.find(function (n) { return n.noteId === noteId; });
        if (!note) return;

        root.querySelector('[data-role="noteEditId"]').value = noteId;
        setCategoryWidget(widgetId, 'modal', note.category || '');
        root.querySelector('[data-role="noteText"]').value = note.noteText || '';
        setEditingBanner(root, true);
        showForm(root, true);
    }

    function resetForm(widgetId) {
        var root = getRoot(widgetId);
        if (!root) return;
        root.querySelector('[data-role="noteEditId"]').value = '0';
        root.querySelector('[data-role="noteText"]').value = '';
        setCategoryWidget(widgetId, 'modal', '');
        setEditingBanner(root, false);
    }

    function setEditingBanner(root, show) {
        var banner = root.querySelector('[data-role="noteEditingBanner"]');
        if (banner) banner.style.display = show ? '' : 'none';
    }

    function showForm(root, show) {
        var form = root.querySelector('[data-role="inlineNoteForm"]');
        if (!form) return;
        form.classList.toggle('d-none', !show);
        if (show) {
            var textarea = form.querySelector('[data-role="noteText"]');
            if (textarea) textarea.focus();
        }
    }

    function cancelForm(widgetId) {
        var root = getRoot(widgetId);
        if (!root) return;
        showForm(root, false);
        resetForm(widgetId);
    }

    function save(widgetId) {
        var root = getRoot(widgetId);
        var state = widgets[widgetId];
        if (!root || !state) return;

        var category = root.querySelector('[data-role="noteCategoryHidden"]').value;
        var text = root.querySelector('[data-role="noteText"]').value.trim();
        var noteId = parseInt(root.querySelector('[data-role="noteEditId"]').value, 10) || 0;

        if (!category) { window.Swal ? Swal.fire('Required', 'Please select a category.', 'warning') : alert('Please select a category.'); return; }
        if (!text) { window.Swal ? Swal.fire('Required', 'Please enter note text.', 'warning') : alert('Please enter note text.'); return; }

        postForm('/Notes/SaveNote', {
            __RequestVerificationToken: getAntiForgeryToken(),
            NoteId: noteId,
            EntityType: state.entityType,
            EntityId: state.entityId,
            Category: category,
            NoteText: text,
            FilingYear: state.sessionFilingYear
        }, function (r) {
            if (r && r.success) {
                showForm(root, false);
                resetForm(widgetId);
                loadNotes(root);
            }
        });
    }

    function remove(widgetId, noteId) {
        var root = getRoot(widgetId);
        if (!root) return;

        function doDelete() {
            postForm('/Notes/DeleteNoteById', { __RequestVerificationToken: getAntiForgeryToken(), id: noteId }, function (res) {
                if (res && res.success) loadNotes(root);
            });
        }

        if (window.Swal) {
            Swal.fire({ title: 'Delete this note?', icon: 'warning', showCancelButton: true, confirmButtonText: 'Delete', confirmButtonColor: '#d33' })
                .then(function (r) { if (r.isConfirmed) doDelete(); });
        } else if (confirm('Delete this note?')) {
            doDelete();
        }
    }

    function togglePin(widgetId, noteId) {
        var root = getRoot(widgetId);
        var state = widgets[widgetId];
        if (!root || !state) return;

        var note = state.allNotes.find(function (n) { return n.noteId === noteId; });
        if (note && !note.isPinned) {
            var pinnedCount = state.allNotes.filter(function (n) { return n.isPinned; }).length;
            if (pinnedCount >= 10) {
                if (window.Swal) {
                    Swal.fire('Limit Reached', 'You can pin a maximum of 10 notes. Unpin one before pinning another.', 'warning');
                } else {
                    alert('You can pin a maximum of 10 notes. Unpin one before pinning another.');
                }
                return;
            }
        }

        postForm('/Notes/TogglePin', { __RequestVerificationToken: getAntiForgeryToken(), id: noteId }, function (r) {
            if (r && r.success) {
                if (note) note.isPinned = r.isPinned;
                applyFilters(root);
            }
        });
    }

    /* ── Re-entrant open for a singleton widget (e.g. a global sidebar
       offcanvas reused across many rows/entities) ── */
    function openFor(root, entityType, entityId) {
        if (!root) return;
        var widgetId = root.getAttribute('data-widget-id');

        if (!widgetId) {
            root.setAttribute('data-entity-type', entityType);
            root.setAttribute('data-entity-id', entityId);
            widgetId = initNotesWidget(root);
            resetForm(widgetId);
            return;
        }

        var state = widgets[widgetId];
        if (!state) return;

        state.entityType = entityType;
        state.entityId = entityId;
        root.setAttribute('data-entity-type', entityType);
        root.setAttribute('data-entity-id', entityId);

        state.filterCategory = '';
        state.filterUser = '';
        state.filterYear = '';
        setCategoryWidget(widgetId, 'filter', '');
        var searchBox = root.querySelector('[data-role="noteSearchBox"]');
        if (searchBox) searchBox.value = '';
        var yearSelect = root.querySelector('[data-role="filterYear"]');
        if (yearSelect) yearSelect.value = '';

        showForm(root, false);
        resetForm(widgetId);
        loadNotes(root);
        loadAuthors(root);
    }

    function showMore(widgetId) {
        var root = getRoot(widgetId);
        var state = widgets[widgetId];
        if (!root || !state) return;
        state.visibleCount += 5;
        var wrap = root.querySelector('[data-role="notesTableBody"]');
        if (wrap) wrap.innerHTML = renderCards(root, state.filteredNotes, state);
    }

    function onSearch(widgetId) {
        var root = getRoot(widgetId);
        if (root) applyFilters(root);
    }

    function onYearFilter(widgetId, value) {
        var state = widgets[widgetId];
        if (!state) return;
        state.filterYear = value;
        applyFilters(getRoot(widgetId));
    }

    function onUserFilter(widgetId, value) {
        var state = widgets[widgetId];
        if (!state) return;
        state.filterUser = value;
        applyFilters(getRoot(widgetId));
    }

    function clearFilters(widgetId) {
        var root = getRoot(widgetId);
        var state = widgets[widgetId];
        if (!root || !state) return;
        state.filterCategory = '';
        state.filterUser = '';
        state.filterYear = '';
        setCategoryWidget(widgetId, 'filter', '');
        var searchBox = root.querySelector('[data-role="noteSearchBox"]');
        if (searchBox) searchBox.value = '';
        var userSelect = root.querySelector('[data-role="filterUser"]');
        if (userSelect) userSelect.value = '';
        var yearSelect = root.querySelector('[data-role="filterYear"]');
        if (yearSelect) yearSelect.value = '';
        applyFilters(root);
    }

    /* ── Category dropdown (searchable) ── */

    function dropdownCtxParts(ctx) {
        // ctx is "<widgetId>-filter" or "<widgetId>-modal"
        var idx = ctx.lastIndexOf('-');
        return { widgetId: ctx.slice(0, idx), kind: ctx.slice(idx + 1) };
    }

    function catEls(ctx) {
        var parts = dropdownCtxParts(ctx);
        var root = getRoot(parts.widgetId);
        if (!root) return null;
        var prefix = parts.kind === 'filter' ? 'filterCat' : 'modalCat';
        return {
            root: root,
            kind: parts.kind,
            widgetId: parts.widgetId,
            input: root.querySelector('[data-role="' + prefix + 'Input"]'),
            drop: root.querySelector('[data-role="' + prefix + 'Drop"]'),
            hidden: root.querySelector(parts.kind === 'filter' ? '[data-role="filterCatHidden"]' : '[data-role="noteCategoryHidden"]')
        };
    }

    function openCatDrop(ctx) {
        var c = catEls(ctx);
        if (!c) return;
        c.drop.classList.add('open');
    }

    function closeCatDrop(ctx) {
        var c = catEls(ctx);
        if (!c) return;
        c.drop.classList.remove('open');
        if (!c.hidden.value) c.input.value = '';
        searchCat(ctx, '');
    }

    function searchCat(ctx, query) {
        var c = catEls(ctx);
        if (!c) return;
        var q = (query || '').toLowerCase().trim();
        var anyVisible = false;
        var opts = c.drop.querySelectorAll('.cat-option');
        var labels = c.drop.querySelectorAll('.cat-group-label');
        opts.forEach(function (opt) {
            var match = opt.textContent.toLowerCase().indexOf(q) !== -1;
            opt.classList.toggle('hidden', !match);
            if (match) anyVisible = true;
        });
        labels.forEach(function (lbl) {
            var next = lbl.nextElementSibling;
            var hasVisible = false;
            while (next && !next.classList.contains('cat-group-label') && !next.classList.contains('cat-no-results')) {
                if (!next.classList.contains('hidden')) { hasVisible = true; break; }
                next = next.nextElementSibling;
            }
            lbl.style.display = hasVisible ? '' : 'none';
        });
        var noRes = c.drop.querySelector('.cat-no-results');
        if (noRes) noRes.style.display = anyVisible ? 'none' : 'block';
    }

    function selectCat(ctx, label, val) {
        var c = catEls(ctx);
        if (!c) return;
        c.input.value = label || 'All Categories';
        c.hidden.value = val;
        c.drop.classList.remove('open');

        if (c.kind === 'filter') {
            var state = widgets[c.widgetId];
            if (state) {
                state.filterCategory = val;
                applyFilters(c.root);
            }
        }
    }

    function setCategoryWidget(widgetId, kind, value) {
        var ctx = widgetId + '-' + kind;
        var c = catEls(ctx);
        if (!c || !c.input || !c.hidden) return;
        c.hidden.value = value;
        c.input.value = value || (kind === 'filter' ? '' : '');
    }

    document.addEventListener('click', function (e) {
        document.querySelectorAll('.cat-search-wrap').forEach(function (wrap) {
            if (!wrap.contains(e.target)) {
                wrap.querySelectorAll('.cat-dropdown.open').forEach(function (d) { d.classList.remove('open'); });
            }
        });
    });

    window.NotesWidget = {
        init: initNotesWidget,
        openFor: openFor,
        openAdd: openAdd,
        openEdit: openEdit,
        cancelForm: cancelForm,
        save: save,
        remove: remove,
        togglePin: togglePin,
        showMore: showMore,
        onSearch: onSearch,
        onYearFilter: onYearFilter,
        onUserFilter: onUserFilter,
        clearFilters: clearFilters,
        openCatDrop: openCatDrop,
        searchCat: searchCat,
        selectCat: selectCat
    };
})(window);
