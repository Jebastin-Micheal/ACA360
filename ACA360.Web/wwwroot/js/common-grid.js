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
            viewMode: 'Table'
        }
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

        $.ajax({
            url: config.url,
            type: 'GET',
            data: requestData,
            traditional: true,
            success: function (html) {
                $(config.container).html(html);
                if ($(config.selectors.loader).length) $(config.selectors.loader).hide();
                if (typeof config.onLoad === 'function') config.onLoad();  
            },
            error: function (xhr) {
                console.error("Grid Reload Error:", xhr);
                if ($(config.selectors.loader).length) $(config.selectors.loader).hide();
            }
        });
    }
    

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