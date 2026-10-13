/* ==========================================================================
   COMMON GRID MANAGER (jQuery/AJAX)
   Handles Pagination, Search, Sorting, Page Size, and View Mode
   ========================================================================== */
var GridManager = (function () {

    var config = {
        url: '',                    // Controller Action URL
        container: '#gridContainer', // Where list loads
        cookieName: 'grid_view_mode',
        // --- Selectors ---
        selectors: {
            search: '.grid-search-input',
            pageSize: '.grid-pagesize-select',
            refresh: '.grid-refresh-btn',
            pagination: '.grid-page-link',
            sort: '.grid-sort-header',
            viewToggle: '.grid-view-btn',
            loader: '#loader'
        },

        // --- Layout Config ---
        layout: {
            listColumn: '#mainListColumn',
            detailColumn: '#mainDetailColumn',
            detailContainer: '#commonDetailContainer'
        },

        // --- Current State ---
        state: {
            pageIndex: 1,
            pageSize: 10,
            search: '',
            sortColumn: '0',
            sortOrder: 'asc',
            viewMode: 'Table',
            hiddenColumns: []          // header texts the user hid
        },

        // --- Remembered per user (idea #17) ---
        // rememberKey: defaults to the list's address; false turns remembering off.
        // rememberFilters: selectors of the page's own filter fields to keep as well. Their values
        // are put back before the restored reload, so additionalParams() reads them as usual.
        rememberKey: null,
        rememberFilters: []
    };
    // 1. Load ViewMode based on which button the server rendered as active
    var activeMode = $(config.selectors.viewToggle + '.active').data('mode');
    if (activeMode) {
        config.state.viewMode = activeMode;
    }
    var searchTimer;
    var defaultDetailHtml = "";
    // --- COOKIE HELPERS ---
    function setCookie(name, value, days = 30) {
        var expires = "";
        var date = new Date();
        date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
        expires = "; expires=" + date.toUTCString();
        document.cookie = name + "=" + (value || "") + expires + "; path=/";
    }

    function getCookie(name) {
        var nameEQ = name + "=";
        var ca = document.cookie.split(';');
        for (var i = 0; i < ca.length; i++) {
            var c = ca[i];
            while (c.charAt(0) == ' ') c = c.substring(1, c.length);
            if (c.indexOf(nameEQ) == 0) return c.substring(nameEQ.length, c.length);
        }
        return null;
    }
    // --- INIT ---
    function init(options) {
        $.extend(config, options);

        // Page size the grid starts with (and the refresh button resets to).
        // Pages can override it via state.pageSize in their init options.
        config.defaultPageSize = (options && options.state && options.state.pageSize) ? options.state.pageSize : 10;

        // 1. Load ViewMode from Cookie if it exists
        var savedMode = getCookie(config.cookieName);
        if (savedMode) {
            config.state.viewMode = savedMode;
        }

        // 2. Set initial UI based on the state (from cookie or default)
        updateLayout(config.state.viewMode);
        updateToggleButtons(config.state.viewMode);

        // Sync the page-size dropdown to the configured default, then read it back
        var $pageSize = $(config.selectors.pageSize);
        if ($pageSize.length && config.defaultPageSize) $pageSize.val(String(config.defaultPageSize));
        var initialSize = $pageSize.val();
        if (initialSize) config.state.pageSize = initialSize;
        // Cache the initial HTML of the detail container
        if ($(config.layout.detailContainer).length) {
            defaultDetailHtml = $(config.layout.detailContainer).html();
        }
        bindEvents();
        // Once per page: some pages call init() again on every filter change.
        if (!restoreStarted) { restoreStarted = true; restoreState(); }
    }

    // --- EVENTS ---
    function bindEvents() {

        // 1. Search (Debounced) - Only triggers if 3+ characters or empty
        $(document).on('keyup', config.selectors.search, function () {
            var val = $(this).val();

            // Ignore keys that don't change the search text (arrows, Tab, Enter,
            // modifiers…). Without this, pressing ↓/↑ while the search box is focused
            // fires an empty-search reload that snaps the list back to the first record.
            if (val === config.state.search) return;
            clearTimeout(searchTimer);

            searchTimer = setTimeout(function () {
                // Trigger search ONLY if:
                // 1. The value is 3 characters or more
                // 2. OR the value is empty (to reset the list when they clear the box)
                if (val.length >= 3 || val.length === 0) {
                    config.state.search = val;
                    config.state.pageIndex = 1; // Always reset to page 1 on search
                    reload();
                }
            }, 500); // 500ms delay
        });
        // 2. Page Size
        $(document).on('change', config.selectors.pageSize, function () {
            config.state.pageSize = $(this).val();
            config.state.pageIndex = 1;
            reload();
        });

        // 3. Refresh
        $(document).on('click', config.selectors.refresh, function () {
            $(config.selectors.search).val('');
            config.state.search = '';
            config.state.pageIndex = 1;
            var $pageSizeDropdown = $(config.selectors.pageSize);
            $pageSizeDropdown.val(String(config.defaultPageSize || 10));
            config.state.pageSize = config.defaultPageSize || 10;
            config.state.sortColumn = '0';
            config.state.sortOrder = 'asc';
            // Restore the right-side detail view to its default state
            if ($(config.layout.detailContainer).length && defaultDetailHtml !== "") {
                $(config.layout.detailContainer).html(defaultDetailHtml);
            }
            // Reset external filters if using a common class
            $('.filter-input').val('');
            // Page-specific filter reset (e.g. Employee page sidebar + quick filters)
            if (typeof config.onRefresh === 'function') config.onRefresh();
            reload();
        });

        // 4. Pagination
        $(document).on('click', config.container + ' ' + config.selectors.pagination, function (e) {
            e.preventDefault();
            var page = $(this).data('page');
            if (page && page !== config.state.pageIndex) {
                config.state.pageIndex = page;
                reload();
            }
        });

        // 5. Sorting
        $(document).on('click', config.selectors.sort, function (e) {
            e.preventDefault();
            var col = $(this).data('column');
            var order = $(this).data('sort');
            config.state.pageIndex = 1;
            config.state.sortColumn = col;
            config.state.sortOrder = order;
            reload();
        });

        // 6. View Mode Toggle
        $(document).on('click', config.selectors.viewToggle, function (e) {
            e.preventDefault();
            var mode = $(this).data('mode');
            var prefix = $(this).data('prefix'); // This gets 'employer', 'plan', or 'employee' from HTML
            if (config.state.viewMode !== mode) {
                config.state.viewMode = mode;
                // 1. Update the UI immediately for a snappy user experience
                updateLayout(mode);
                updateToggleButtons(mode);
                reload();
                if (typeof config.onViewChange === 'function') {
                    config.onViewChange(mode);
                }
                // 2. SAVE TO SQL DATABASE VIA AJAX (Replacing setCookie)
                if (prefix) {
                    $.ajax({
                        url: '/UserPreference/SaveViewMode', // Matches the Controller endpoint
                        type: 'POST',
                        headers: { 'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').first().val() || '' },
                        data: {
                            moduleKey: prefix,
                            viewMode: mode
                        },
                        success: function () {
                            console.log('View mode saved to database successfully.');
                        },
                        error: function (xhr) {
                            console.error('Failed to save view mode to database:', xhr);
                        }
                    });
                }
            }
        });
    }

    // --- LAYOUT SWITCHER ---
    function updateLayout(mode) {
        var $list = $(config.layout.listColumn);
        var $detail = $(config.layout.detailColumn);
        if (mode === 'Table') {
            $list.removeClass('col-lg-4 col-md-5').addClass('col-12');
            $detail.addClass('d-none').removeClass('col-lg-8 col-md-7');
            $(config.container).removeClass('split-mode-list');
            // Restore the right-side detail view to its default state
            if ($(config.layout.detailContainer).length && defaultDetailHtml !== "") {
                $(config.layout.detailContainer).html(defaultDetailHtml);
            }
        } else {
            $list.removeClass('col-12').addClass('col-lg-4 col-md-5');
            $detail.removeClass('d-none').addClass('col-lg-8 col-md-7');
            $(config.container).addClass('split-mode-list');
        }
    }
    function updateToggleButtons(mode) {
        $(config.selectors.viewToggle).each(function () {
            var $btn = $(this);
            if ($btn.data('mode') === mode) {
                $btn.removeClass('btn-outline-primary').addClass('active btn-primary');
            } else {
                $btn.removeClass('active btn-primary').addClass('btn-outline-primary');
            }
        });
    }
    function reload(overrides) {
        if ($(config.selectors.loader).length) $(config.selectors.loader).show();

        // 1. Apply overrides to state if provided (this updates config.state)
        if (overrides) {
            $.extend(config.state, overrides);
        }

        // ✅ FIX: Force the state search to match the current input value
        // This ensures that if the input was cleared, the state is cleared too.
        var currentInputValue = $(config.selectors.search).val();
        config.state.search = (currentInputValue !== undefined) ? currentInputValue : config.state.search;

        // 2. Gather the base state
        var baseState = {
            pageIndex: config.state.pageIndex,
            pageSize: config.state.pageSize,
            search: config.state.search, // This will now be "" if cleared
            sortColumn: config.state.sortColumn,
            sortOrder: config.state.sortOrder,
            viewMode: config.state.viewMode
        };

        // 3. Gather additional filters
        var extraParams = {};
        if (typeof config.additionalParams === 'function') {
            extraParams = config.additionalParams();
        }

        // 4. Merge (extraParams might contain a 'Search' key - make sure it doesn't overwrite baseState.search with old data)
        var requestData = $.extend({}, baseState, extraParams);

        // ✅ Ensure we don't have duplicate search keys with different values
        if (requestData.Search !== undefined) requestData.search = requestData.Search;

        var seq = ++requestSeq;
        $.ajax({
            url: config.url,
            type: 'GET',
            data: requestData,
            traditional: true,
            success: function (html) {
                // An older request finishing late must not replace a newer list.
                if (seq !== requestSeq) return;
                $(config.container).html(html);
                applyColumns();
                saveStateSoon();
                if ($(config.selectors.loader).length) $(config.selectors.loader).hide();
                if (typeof config.onLoad === 'function') config.onLoad();  
            },
            error: function (xhr) {
                console.error("Grid Reload Error:", xhr);
                if ($(config.selectors.loader).length) $(config.selectors.loader).hide();
            }
        });
    }
    

    // --- REMEMBERED STATE (idea #17) ---
    var requestSeq = 0;
    var saveTimer = null;
    var restoring = false;
    var restoreStarted = false;

    function gridKey() {
        if (config.rememberKey === false) return null;
        if (config.rememberKey) return String(config.rememberKey).toLowerCase();
        if (!config.url) return null;
        try { return new URL(config.url, window.location.origin).pathname.toLowerCase(); } catch (e) { return null; }
    }

    function token() { return $('input[name="__RequestVerificationToken"]').first().val() || ''; }

    function currentFilters() {
        var out = {};
        (config.rememberFilters || []).forEach(function (sel) {
            var $el = $(sel);
            if (!$el.length) return;
            if ($el.is(':checkbox')) out[sel] = $el.map(function () { return this.checked ? this.value : null; }).get();
            else out[sel] = $el.val();
        });
        return out;
    }

    function saveStateSoon() {
        var key = gridKey();
        if (!key || restoring) return;
        clearTimeout(saveTimer);
        saveTimer = setTimeout(function () {
            var st = config.state;
            var isDefault = !st.search && String(st.pageSize) === String(config.defaultPageSize || 10) &&
                String(st.sortColumn) === '0' && st.sortOrder === 'asc' && !(st.hiddenColumns || []).length;
            var filters = currentFilters();
            var anyFilter = Object.keys(filters).some(function (k) {
                var v = filters[k];
                return Array.isArray(v) ? v.length > 0 : (v !== '' && v != null);
            });
            var payload = (isDefault && !anyFilter) ? '{}' : JSON.stringify({
                search: st.search || '', pageSize: st.pageSize, sortColumn: st.sortColumn, sortOrder: st.sortOrder,
                hiddenColumns: st.hiddenColumns || [], filters: filters
            });
            $.ajax({ url: '/UserPreference/SaveGridState', type: 'POST', data: { key: key, state: payload, __RequestVerificationToken: token() } });
        }, 800);
    }

    function restoreState() {
        var key = gridKey();
        if (!key) { applyColumns(); return; }
        restoring = true;
        $.getJSON('/UserPreference/GridState', { key: key })
            .done(function (saved) {
                restoring = false;
                if (!saved || !Object.keys(saved).length) { applyColumns(); return; }
                var changed = false;
                if (saved.pageSize && String(saved.pageSize) !== String(config.state.pageSize)) {
                    config.state.pageSize = saved.pageSize;
                    $(config.selectors.pageSize).val(String(saved.pageSize));
                    changed = true;
                }
                if (saved.sortColumn != null && (String(saved.sortColumn) !== String(config.state.sortColumn) || (saved.sortOrder || 'asc') !== config.state.sortOrder)) {
                    config.state.sortColumn = saved.sortColumn;
                    config.state.sortOrder = saved.sortOrder || 'asc';
                    changed = true;
                }
                if (saved.search) {
                    config.state.search = saved.search;
                    $(config.selectors.search).val(saved.search).trigger('input');
                    changed = true;
                }
                config.state.hiddenColumns = Array.isArray(saved.hiddenColumns) ? saved.hiddenColumns : [];
                var f = saved.filters || {};
                Object.keys(f).forEach(function (sel) {
                    if ((config.rememberFilters || []).indexOf(sel) < 0) return;   // only fields the page still names
                    var $el = $(sel), v = f[sel];
                    if (!$el.length) return;
                    if ($el.is(':checkbox')) $el.each(function () { this.checked = Array.isArray(v) && v.indexOf(this.value) >= 0; });
                    else $el.val(v);
                    changed = true;
                });
                if (changed) { config.state.pageIndex = 1; reload(); }
                else applyColumns();
            })
            .fail(function () { restoring = false; applyColumns(); });
    }

    // Columns are hidden by header text, so a column keeps its setting when others move.
    function headerText(th) { return $.trim($(th).text()).replace(/\s+/g, ' '); }

    function applyColumns() {
        var $table = $(config.container).find('table').first();
        var $wrap = $('.grid-columns-wrapper');
        if (!$table.length) { $wrap.addClass('d-none'); return; }
        var $ths = $table.find('thead tr').last().children('th');
        var hidden = config.state.hiddenColumns || [];
        var $menu = $('.grid-columns-menu').empty();
        $ths.each(function (i) {
            var text = headerText(this);
            var hide = !!text && hidden.indexOf(text) >= 0;
            $(this).toggleClass('d-none', hide);
            $table.find('tbody tr').each(function () {
                var $cells = $(this).children('td');
                if ($cells.length === $ths.length) $cells.eq(i).toggleClass('d-none', hide);
            });
            if (text) {
                var id = 'gridcol_' + i;
                $menu.append($('<div class="form-check mb-1"></div>')
                    .append($('<input class="form-check-input grid-column-toggle" type="checkbox">').attr('id', id).attr('data-column-text', text).prop('checked', !hide))
                    .append($('<label class="form-check-label"></label>').attr('for', id).text(text)));
            }
        });
        if ($menu.children().length) {
            $menu.append('<div class="border-top pt-2 mt-1"><button type="button" class="btn btn-sm btn-link p-0 grid-columns-reset">Show all columns</button></div>');
            $wrap.removeClass('d-none');
        } else {
            $wrap.addClass('d-none');
        }
    }

    $(document).on('change', '.grid-column-toggle', function () {
        var text = $(this).attr('data-column-text');
        var hidden = (config.state.hiddenColumns || []).filter(function (h) { return h !== text; });
        if (!this.checked) hidden.push(text);
        config.state.hiddenColumns = hidden;
        applyColumns();
        saveStateSoon();
    });
    $(document).on('click', '.grid-columns-reset', function () {
        config.state.hiddenColumns = [];
        applyColumns();
        saveStateSoon();
    });

    // ✅ FIX 3: Expose setPageIndex manually if needed, or rely on reload param
    function setPageIndex(page) {
        config.state.pageIndex = page;
        reload();
    }

    // Public API
    return {
        init: init,
        reload: reload,
        setPageIndex: setPageIndex,
        getState: function () {
            return config.state;
        }
    };
})();